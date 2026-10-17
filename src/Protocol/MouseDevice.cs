// 设备会话：枚举控制口、打开、串行化收发、busy 重试、断线关闭。
// 所有命令都在调用线程上同步完成（一次约 67 ms），上层负责放到后台线程。
using System;
using System.Collections.Generic;
using System.Threading;
using RazerMouse.Native;

namespace RazerMouse.Protocol
{
    internal sealed class MouseDevice : IDisposable
    {
        public const int RazerVid = 0x1532;
        public const int ControlReportLength = 91;

        /// <summary>DeathAdder V3 Pro：00B6 有线 / 00B7 无线 / 00C2、00C3 同型号变体。</summary>
        public static readonly ushort[] DeathAdderV3ProPids = { 0x00B6, 0x00B7, 0x00C2, 0x00C3 };

        private const int ReplyWaitMs = 60;
        private const int MaxRetry = 5;

        private readonly object _gate = new object();
        private IntPtr _handle = HidApi.InvalidHandle;
        private bool _disposed;

        public string Path { get; private set; }
        public ushort Pid { get; private set; }
        public ushort Version { get; private set; }
        public ushort UsagePage { get; private set; }
        public int FeatureLength { get; private set; }
        public byte Txn { get; set; }

        private MouseDevice()
        {
            Txn = RazerReport.DefaultTxn;
        }

        public bool IsOpen
        {
            get { lock (_gate) { return _handle != HidApi.InvalidHandle; } }
        }

        /// <summary>找到并打开控制接口；没有设备时抛 InvalidOperationException（中文原因）。</summary>
        public static MouseDevice Open()
        {
            List<HidInterface> all = HidApi.Enumerate(RazerVid);

            var candidates = new List<HidInterface>();
            foreach (HidInterface iface in all)
            {
                // 控制口判据：feature report 足够 91 字节，且是鼠标主集合（usagePage=1, usage=2）
                if (iface.FeatureLength >= ControlReportLength && iface.UsagePage == 0x01)
                    candidates.Add(iface);
            }
            if (candidates.Count == 0)
                throw new InvalidOperationException("没有找到 Razer 鼠标控制接口（设备未连接，或不是已适配的型号）");

            // 优先已知 PID，其次鼠标主集合，再其次 feature 长度最大的
            candidates.Sort(delegate(HidInterface a, HidInterface b)
            {
                int ka = Score(a);
                int kb = Score(b);
                if (ka != kb) return kb.CompareTo(ka);
                return b.FeatureLength.CompareTo(a.FeatureLength);
            });

            HidInterface device = candidates[0];
            IntPtr handle = HidApi.OpenDevice(device.Path);
            if (handle == HidApi.InvalidHandle)
            {
                throw new InvalidOperationException(
                    "打开设备失败（CreateFileW 被拒绝）；可能需要重新插拔 dongle，或另一个程序正独占该设备");
            }

            var result = new MouseDevice();
            result._handle = handle;
            result.Path = device.Path;
            result.Pid = device.Pid;
            result.Version = device.Version;
            result.UsagePage = device.UsagePage;
            result.FeatureLength = device.FeatureLength;
            return result;
        }

        private static int Score(HidInterface iface)
        {
            int score = 0;
            if (Array.IndexOf(DeathAdderV3ProPids, iface.Pid) >= 0) score += 100;
            if (iface.UsagePage == 0x01 && iface.Usage == 0x02) score += 10;
            if (iface.FeatureLength >= ControlReportLength) score += 1;
            return score;
        }

        /// <summary>发一条命令并取回复。status 必须为 0x02，否则抛 RazerException。</summary>
        public byte[] Ask(byte commandClass, byte commandId, byte dataSize, byte[] args)
        {
            return Ask(commandClass, commandId, dataSize, args, Txn);
        }

        public byte[] Ask(byte commandClass, byte commandId, byte dataSize, byte[] args, byte txn)
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException("MouseDevice");
                if (_handle == HidApi.InvalidHandle)
                    throw new InvalidOperationException("设备未连接");

                byte[] request = RazerReport.Build(commandClass, commandId, dataSize, args, txn);
                if (!HidApi.HidD_SetFeature(_handle, request, RazerReport.Length))
                {
                    int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    throw new InvalidOperationException(
                        string.Format("发送命令失败（Win32 错误 {0}），设备可能已断开", error));
                }

                var reply = new byte[RazerReport.Length];
                for (int attempt = 0; attempt < MaxRetry; attempt++)
                {
                    Thread.Sleep(ReplyWaitMs);   // 不能发完立刻读，读到的是请求回显（status=0x01）
                    if (!HidApi.HidD_GetFeature(_handle, reply, RazerReport.Length))
                    {
                        int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                        throw new InvalidOperationException(
                            string.Format("读取回复失败（Win32 错误 {0}），设备可能已断开", error));
                    }
                    if (reply[1] != 0x01) break; // 0x01 = busy，再等一轮
                }

                byte status = reply[1];
                if (status != 0x02)
                {
                    throw new RazerException(status, string.Format(
                        "命令 0x{0:X2}/0x{1:X2} 失败：{2}",
                        commandClass, commandId, RazerReport.StatusText(status)));
                }
                return reply;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (_handle != HidApi.InvalidHandle)
                {
                    HidApi.CloseHandle(_handle);
                    _handle = HidApi.InvalidHandle;
                }
            }
        }
    }
}
