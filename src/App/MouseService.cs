// 后台设备服务：独占一个线程 + 一个 HID 句柄，所有命令排队串行执行。
// 界面层永远只读 Status 快照、通过 Post* 方法下达动作，绝不直接碰设备。
using System;
using System.Collections.Generic;
using System.Threading;
using RazerMouse.Native;
using RazerMouse.Protocol;

namespace RazerMouse.App
{
    internal sealed class MouseStatus
    {
        public bool Connected;
        public string Serial = "";
        public string Firmware = "";
        public ushort Pid;

        public bool HasBattery;
        public int BatteryRaw;
        public int BatteryPercent;
        public bool Charging;

        public int DpiX;
        public int DpiY;
        public List<DpiStage> Stages = new List<DpiStage>();
        public int ActiveStage = 1;      // 1 起，和协议一致
        public int PollingHz;

        public bool Busy;
        public string BusyText = "";
        public string LastError;
        public DateTime UpdatedUtc = DateTime.MinValue;

        public int StageCount { get { return Stages.Count; } }

        public MouseStatus Clone()
        {
            var copy = (MouseStatus)MemberwiseClone();
            copy.Stages = new List<DpiStage>(Stages.Count);
            foreach (DpiStage stage in Stages) copy.Stages.Add(new DpiStage(stage.X, stage.Y));
            return copy;
        }
    }

    internal sealed class MouseService : IDisposable
    {
        private readonly object _lock = new object();
        private readonly Queue<Action> _jobs = new Queue<Action>();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly Thread _worker;
        private readonly Settings _settings;

        private volatile bool _stopping;
        private MouseDevice _device;
        private MouseStatus _current = new MouseStatus();

        private DateTime _nextConnect = DateTime.MinValue;
        private DateTime _nextPoll = DateTime.MaxValue;
        private DateTime _lastPollUtc = DateTime.MinValue;

        // 轮询节奏：放电 60 秒一次，充电中 30 秒一次（充电时电量约每分钟涨 1%，30 秒已经够及时）。
        private const int DischargePollSeconds = 60;
        private const int ChargingPollSeconds = 30;
        // 用户 2 秒内有过键鼠输入就跳过这一轮；但连续操作时最多推迟 5 分钟，低电量提醒不能被拖太久。
        private const int BusyGraceMs = 2000;
        private const double MaxDeferSeconds = 300;

        // 启动时的原始设置，供「恢复上次」与退出还原使用
        private bool _initialCaptured;
        private int _initialDpiX, _initialDpiY;
        private DpiStages _initialStages;
        private int _initialPolling;

        /// <summary>在后台线程触发。订阅方负责 marshal 到 UI 线程。</summary>
        public event EventHandler<MouseStatus> StatusChanged;

        /// <summary>在后台线程触发，参数为中文错误描述。</summary>
        public event EventHandler<string> Faulted;

        public MouseService(Settings settings)
        {
            _settings = settings;
            _worker = new Thread(Loop);
            _worker.IsBackground = true;
            _worker.Name = "RazerMouse.Device";
        }

        public MouseStatus Status
        {
            get { lock (_lock) { return _current.Clone(); } }
        }

        public bool HasInitialSnapshot
        {
            get { lock (_lock) { return _initialCaptured; } }
        }

        public void Start()
        {
            _nextConnect = DateTime.UtcNow;
            _worker.Start();
        }

        public void Dispose()
        {
            _stopping = true;
            _wake.Set();
            if (_worker.Join(1500)) return;
        }

        // ---------------- 对外的动作 ----------------

        /// <summary>尽快重新读一遍（电量/充电/DPI/档位/回报率）。</summary>
        public void RefreshAll()
        {
            Post(delegate
            {
                MouseDevice device = _device;
                if (device == null) { TryConnect(); return; }
                MouseStatus status = Status;
                try
                {
                    ReadEverything(device, status);
                    status.LastError = null;
                    Publish(status);
                }
                catch (Exception ex) { HandleFailure(ex); }
            });
        }

        public void SwitchStage(int activeStage)
        {
            Apply("正在切换档位", delegate(MouseDevice device)
            {
                DpiStages table = MouseCommands.ReadDpiStages(device);
                if (activeStage < 1 || activeStage > table.Stages.Count) return;
                table.Active = activeStage;
                MouseCommands.WriteDpiStages(device, table, _settings.StoreOnboard);
                DpiStage target = table.Stages[activeStage - 1];
                MouseCommands.WriteDpi(device, target.X, target.Y, _settings.StoreOnboard);
            });
        }

        public void SetStageDpi(int stage, int dpi)
        {
            Apply("正在写入 DPI " + dpi, delegate(MouseDevice device)
            {
                DpiStages table = MouseCommands.ReadDpiStages(device);
                if (stage < 1 || stage > table.Stages.Count) return;
                table.Stages[stage - 1] = new DpiStage(dpi, dpi);
                MouseCommands.WriteDpiStages(device, table, _settings.StoreOnboard);
                if (table.Active == stage)
                    MouseCommands.WriteDpi(device, dpi, dpi, _settings.StoreOnboard);
            });
        }

        public void SetPolling(int hz)
        {
            Apply("正在设置回报率", delegate(MouseDevice device)
            {
                MouseCommands.WritePolling(device, hz);
            });
        }

        public void SetStoreOnboard(bool store)
        {
            _settings.StoreOnboard = store;
            _settings.Save();
        }

        /// <summary>把启动时读到的值写回设备。</summary>
        public void RestoreInitial()
        {
            Apply("正在恢复上次设置", delegate(MouseDevice device) { WriteInitial(device); });
        }

        /// <summary>退出时同步还原（最多等 timeoutMs）。返回是否成功。</summary>
        public bool RestoreInitialAndWait(int timeoutMs)
        {
            lock (_lock) { if (!_initialCaptured) return false; }
            bool ok = false;
            var done = new ManualResetEvent(false);
            Post(delegate
            {
                try
                {
                    if (_device != null) WriteInitial(_device);
                    ok = true;
                }
                catch (Exception ex) { HandleFailure(ex); }
                finally { done.Set(); }
            });
            done.WaitOne(timeoutMs);
            return ok;
        }

        private void WriteInitial(MouseDevice device)
        {
            DpiStages snapshot;
            lock (_lock) { snapshot = _initialStages; }
            if (snapshot == null) return;
            MouseCommands.WriteDpiStages(device, snapshot, _settings.StoreOnboard);
            DpiStage target = snapshot.Stages[snapshot.Active - 1];
            MouseCommands.WriteDpi(device, target.X, target.Y, _settings.StoreOnboard);
            MouseCommands.WritePolling(device, _initialPolling);
        }

        // ---------------- 线程主体 ----------------

        private void Loop()
        {
            while (!_stopping)
            {
                _wake.WaitOne(ComputeWait());
                if (_stopping) break;

                DrainJobs();
                if (_stopping) break;

                try
                {
                    if (_device == null)
                    {
                        if (DateTime.UtcNow >= _nextConnect)
                        {
                            _nextConnect = DateTime.UtcNow.AddSeconds(2);
                            TryConnect();
                        }
                    }
                    else if (DateTime.UtcNow >= _nextPoll)
                    {
                        PollLight();
                    }
                }
                catch (Exception ex) { HandleFailure(ex); }
            }
            CloseDevice();
        }

        private int ComputeWait()
        {
            DateTime now = DateTime.UtcNow;
            double seconds = 1.0;
            if (_device == null)
            {
                seconds = (_nextConnect - now).TotalSeconds;
            }
            else
            {
                seconds = (_nextPoll - now).TotalSeconds;
            }
            if (seconds < 0.05) seconds = 0.05;
            if (seconds > 1.0) seconds = 1.0;
            return (int)(seconds * 1000);
        }

        private void Post(Action job)
        {
            lock (_lock) { _jobs.Enqueue(job); }
            _wake.Set();
        }

        private void DrainJobs()
        {
            while (true)
            {
                Action job;
                lock (_lock)
                {
                    if (_jobs.Count == 0) return;
                    job = _jobs.Dequeue();
                }
                try { job(); }
                catch (Exception ex) { HandleFailure(ex); }
            }
        }

        private void TryConnect()
        {
            MouseDevice device = null;
            try
            {
                device = MouseDevice.Open();
                _device = device;

                MouseStatus status = Status;
                status.Connected = true;
                status.Pid = device.Pid;
                ReadEverything(device, status);
                status.LastError = null;
                Publish(status);

                _lastPollUtc = DateTime.UtcNow;
                CaptureInitial(device);
                _nextPoll = DateTime.UtcNow.AddSeconds(PollSeconds());
            }
            catch (Exception ex)
            {
                if (device != null) { try { device.Dispose(); } catch { } }
                _device = null;
                MouseStatus status = Status;
                status.Connected = false;
                status.LastError = Describe(ex);
                Publish(status);
                _nextConnect = DateTime.UtcNow.AddSeconds(2);
            }
        }

        private void CaptureInitial(MouseDevice device)
        {
            lock (_lock)
            {
                if (_initialCaptured) return;
            }
            try
            {
                var dpi = MouseCommands.ReadDpi(device, false);
                var stages = MouseCommands.ReadDpiStages(device);
                int polling = MouseCommands.StandardCodeToHz(MouseCommands.ReadPollingCode(device));
                lock (_lock)
                {
                    _initialDpiX = dpi.X;
                    _initialDpiY = dpi.Y;
                    _initialStages = stages;
                    _initialPolling = polling > 0 ? polling : 1000;
                    _initialCaptured = true;
                }
            }
            catch (Exception ex) { HandleFailure(ex); }
        }

        /// <summary>周期性轻量刷新：只读电量与充电状态。</summary>
        private void PollLight()
        {
            MouseDevice device = _device;
            if (device == null) return;

            // 用户手正在动的时候先不问设备：一次电量查询是两条同步 Feature Report，
            // 每条都要等固件回 60 ms。没必要在人家正用鼠标的时候占这 130 ms ——
            // 停手 2 秒内补上；要是一直在操作，最多憋 5 分钟也必须读一次。
            bool busy = IdleTime.Milliseconds() < BusyGraceMs;
            bool overdue = (DateTime.UtcNow - _lastPollUtc).TotalSeconds >= MaxDeferSeconds;
            if (busy && !overdue)
            {
                _nextPoll = DateTime.UtcNow.AddSeconds(2);
                return;
            }

            _nextPoll = DateTime.UtcNow.AddSeconds(PollSeconds());

            MouseStatus status = Status;
            try
            {
                BatteryInfo battery = MouseCommands.ReadBattery(device);
                bool charging = MouseCommands.ReadCharging(device);
                status.Connected = true;
                status.HasBattery = true;
                status.BatteryRaw = battery.Raw;
                status.BatteryPercent = battery.Percent;
                status.Charging = charging;
                status.LastError = null;
                status.UpdatedUtc = DateTime.UtcNow;
                _lastPollUtc = status.UpdatedUtc;
                Publish(status);
            }
            catch (Exception ex) { HandleFailure(ex); }
        }

        private int PollSeconds()
        {
            MouseStatus status = Status;
            if (!status.Connected) return 2;
            // 充电中 30 秒（拔线能较快反映），放电 60 秒（电量掉得很慢，插线最坏 60 秒内看到）
            return status.Charging ? ChargingPollSeconds : DischargePollSeconds;
        }

        /// <summary>读全套状态（约 400ms，只在连接后与写入后调用）。</summary>
        private void ReadEverything(MouseDevice device, MouseStatus status)
        {
            if (string.IsNullOrEmpty(status.Serial)) status.Serial = MouseCommands.ReadSerial(device);
            if (string.IsNullOrEmpty(status.Firmware)) status.Firmware = MouseCommands.ReadFirmware(device);

            BatteryInfo battery = MouseCommands.ReadBattery(device);
            status.HasBattery = true;
            status.BatteryRaw = battery.Raw;
            status.BatteryPercent = battery.Percent;
            status.Charging = MouseCommands.ReadCharging(device);

            DpiStage dpi = MouseCommands.ReadDpi(device, false);
            status.DpiX = dpi.X;
            status.DpiY = dpi.Y;

            DpiStages table = MouseCommands.ReadDpiStages(device);
            status.Stages = table.Stages;
            status.ActiveStage = table.Active;

            int hz = MouseCommands.StandardCodeToHz(MouseCommands.ReadPollingCode(device));
            status.PollingHz = hz > 0 ? hz : 0;

            status.Connected = true;
            status.Busy = false;
            status.BusyText = "";
            status.UpdatedUtc = DateTime.UtcNow;
        }

        /// <summary>写入动作的统一外壳：置忙 → 执行 → 读回 → 发布。</summary>
        private void Apply(string busyText, Action<MouseDevice> mutation)
        {
            Post(delegate
            {
                MouseDevice device = _device;
                if (device == null)
                {
                    MouseStatus offline = Status;
                    offline.LastError = "鼠标未连接";
                    Publish(offline);
                    return;
                }

                MouseStatus status = Status;
                status.Busy = true;
                status.BusyText = busyText;
                Publish(status);

                try
                {
                    mutation(device);
                }
                catch (Exception ex)
                {
                    MouseStatus failed = Status;
                    failed.Busy = false;
                    failed.BusyText = "";
                    Publish(failed);
                    HandleFailure(ex);
                    return;
                }

                try
                {
                    MouseStatus after = Status;
                    ReadEverything(device, after);
                    after.LastError = null;
                    Publish(after);
                    _nextPoll = DateTime.UtcNow.AddSeconds(PollSeconds());
                }
                catch (Exception ex) { HandleFailure(ex); }
            });
        }

        // ---------------- 状态与错误 ----------------

        private void Publish(MouseStatus status)
        {
            lock (_lock) { _current = status.Clone(); }
            EventHandler<MouseStatus> handler = StatusChanged;
            if (handler != null)
            {
                try { handler(this, status.Clone()); } catch { }
            }
        }

        private void HandleFailure(Exception ex)
        {
            bool drop = ex is InvalidOperationException
                        || ex is ObjectDisposedException
                        || ex is System.IO.IOException;

            MouseStatus status = Status;
            status.LastError = Describe(ex);
            status.Busy = false;
            status.BusyText = "";
            if (drop)
            {
                CloseDevice();
                status.Connected = false;
                _nextConnect = DateTime.UtcNow.AddSeconds(2);
            }
            Publish(status);

            EventHandler<string> handler = Faulted;
            if (handler != null)
            {
                try { handler(this, status.LastError); } catch { }
            }
        }

        private void CloseDevice()
        {
            MouseDevice device = _device;
            _device = null;
            if (device == null) return;
            try { device.Dispose(); } catch { }
        }

        internal static string Describe(Exception ex)
        {
            var razer = ex as RazerException;
            if (razer != null)
            {
                if (razer.Status == 0x01) return "设备忙（命令被占用），稍后自动重试";
                if (razer.Status == 0x04) return "设备无响应（超时）";
                if (razer.Status == 0x05) return "设备不支持这条命令";
                if (razer.Status == 0x03) return "设备拒绝了这次写入";
                return RazerReport.StatusText(razer.Status);
            }
            if (ex is InvalidOperationException) return "与鼠标的通信中断，正在重新连接";
            if (ex is System.IO.IOException) return "与鼠标的通信中断，正在重新连接";
            if (ex is ArgumentException) return ex.Message;
            return ex.Message;
        }
    }
}
