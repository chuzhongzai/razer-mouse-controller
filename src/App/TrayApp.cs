// 应用主体：托盘图标 + 面板 + 菜单 + 设备服务的生命周期。
// 悬停提示只用系统托盘文字（NotifyIcon.Text + BuildTooltip），不再自绘卡片。
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using RazerMouse.Ui;

namespace RazerMouse.App
{
    internal sealed class TrayApp : ApplicationContext, IDisposable
    {
        private readonly SynchronizationContext _ui;
        private readonly Settings _settings;
        private readonly MouseService _service;
        private readonly BatteryAlert _alert;
        private readonly TrayIconRenderer _icons = new TrayIconRenderer();

        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _autoStartItem;
        private readonly ToolStripMenuItem _restoreItem;
        private readonly PanelForm _panel;
        private readonly TextTip _tip = new TextTip();
        private readonly System.Windows.Forms.Timer _tipTimer;

        /// <summary>指针离托盘图标多远内仍算"还在悬停"（96 DPI 设计值，用时过 Theme.Z()）。</summary>
        private const int TipAnchorRadius = 26;

        private EventWaitHandle _showEvent;
        private Thread _showWatcher;
        private MouseStatus _status = new MouseStatus();
        private Point _tipAnchor;
        private bool _alertActive;
        private bool _disposed;
        private bool _faultShown;

        public TrayApp(bool startMinimized)
        {
            _ui = SynchronizationContext.Current;
            _settings = Settings.Load();
            _service = new MouseService(_settings);
            _alert = new BatteryAlert(_settings.LowBatteryThreshold);

            _panel = new PanelForm(_service, _settings);
            _panel.ExitRequested += delegate { Exit(); };
            _panel.AutostartChanged += delegate
            {
                // 面板里改了自启，托盘菜单的勾选跟着变
                if (_autoStartItem != null) _autoStartItem.Checked = Startup.IsEnabled();
            };

            _tipTimer = new System.Windows.Forms.Timer();
            _tipTimer.Interval = 50;    // 50ms 看一次指针：移开之后最迟 50ms 收起，肉眼就是"立刻"
            _tipTimer.Tick += OnTipWatch;

            _menu = new ContextMenuStrip();
            _menu.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
            _menu.BackColor = Theme.Card;
            _menu.ForeColor = Theme.Text;
            _menu.Font = Theme.Body;
            _menu.ShowImageMargin = false;

            var open = new ToolStripMenuItem("打开面板");
            open.Font = Theme.BodyBold;
            open.Click += delegate { ShowPanel(); };
            _menu.Items.Add(open);

            var refresh = new ToolStripMenuItem("立即刷新");
            refresh.Click += delegate { _service.RefreshAll(); };
            _menu.Items.Add(refresh);

            _menu.Items.Add(new ToolStripSeparator());

            _autoStartItem = new ToolStripMenuItem("开机自动启动");
            _autoStartItem.CheckOnClick = true;
            _autoStartItem.Checked = Startup.IsEnabled();
            _autoStartItem.Click += delegate
            {
                bool ok = Startup.SetEnabled(_autoStartItem.Checked);
                if (!ok)
                {
                    _autoStartItem.Checked = Startup.IsEnabled();
                    _tray.ShowBalloonTip(4000, "设置失败", "没权限写注册表启动项。", ToolTipIcon.Warning);
                    return;
                }
                _settings.AutoStart = _autoStartItem.Checked;
                _settings.Save();
            };
            _menu.Items.Add(_autoStartItem);
            _menu.Opening += delegate
            {
                // 每次打开菜单都按注册表实际状态刷新，避免与面板/外部改动不一致
                if (_autoStartItem != null) _autoStartItem.Checked = Startup.IsEnabled();
            };

            _restoreItem = new ToolStripMenuItem("退出时恢复启动时的设置");
            _restoreItem.CheckOnClick = true;
            _restoreItem.Checked = _settings.RestoreOnExit;
            _restoreItem.Click += delegate
            {
                _settings.RestoreOnExit = _restoreItem.Checked;
                _settings.Save();
            };
            _menu.Items.Add(_restoreItem);

            _menu.Items.Add(new ToolStripSeparator());

            var about = new ToolStripMenuItem("关于");
            about.Click += delegate { ShowAbout(); };
            _menu.Items.Add(about);

            var exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { Exit(); };
            _menu.Items.Add(exit);

            foreach (ToolStripItem item in _menu.Items)
            {
                item.ForeColor = Theme.Text;
            }

            _tray = new NotifyIcon();
            _tray.Icon = _icons.Get(_status, false);
            _tray.Text = string.Empty;   // 悬停文字由自绘 TextTip 提供，这里留空避免系统再弹一个重复的气泡
            _tray.Visible = true;
            _tray.ContextMenuStrip = _menu;
            _tray.MouseClick += OnTrayClick;
            _tray.MouseMove += OnTrayMouseMove;

            _service.StatusChanged += OnStatusChanged;
            _service.Faulted += OnFaulted;
            _alert.Raised += OnBatteryAlert;
            _service.Start();

            StartShowWatcher();

            if (!startMinimized) ShowPanel();
        }

        // ---------------- 设备事件（后台线程） ----------------

        private void OnStatusChanged(object sender, MouseStatus status)
        {
            Post(delegate { ApplyStatus(status); });
        }

        private void OnFaulted(object sender, string message)
        {
            Post(delegate
            {
                if (_faultShown) return;
                _faultShown = true;
                _tray.ShowBalloonTip(4000, "雷蛇鼠标控制器", message, ToolTipIcon.Warning);
            });
        }

        private void OnBatteryAlert(object sender, BatteryAlertEventArgs e)
        {
            Post(delegate
            {
                // 只有"电量不够"才把图标染红/橙；充满提示不该让图标变红
                _alertActive = e.Low;
                _tray.Icon = _icons.Get(_status, _alertActive);
                _tray.ShowBalloonTip(8000, e.Title, e.Text,
                    e.Critical ? ToolTipIcon.Error : ToolTipIcon.Warning);
            });
        }

        // ---------------- 界面更新（UI 线程） ----------------

        private void ApplyStatus(MouseStatus status)
        {
            _status = status;
            if (status.Connected && status.LastError == null) _faultShown = false;

            // 电量回升（或插上电）之后，图标要从红/橙恢复成绿，不能一直挂着告警色
            if (_alertActive && status.Connected && status.HasBattery
                && (status.Charging || status.BatteryPercent > _settings.LowBatteryThreshold + 10))
            {
                _alertActive = false;
            }

            _tray.Icon = _icons.Get(status, _alertActive);
            _panel.ApplyStatus(status);

            _alert.Update(status);
        }

        /// <summary>悬停气泡的文字：第一行是设备与电量，第二行是 DPI/回报率/档位。</summary>
        internal static string[] BuildTipLines(MouseStatus status)
        {
            if (!status.Connected)
            {
                return new string[] { "雷蛇鼠标", "未连接，插上接收器或唤醒鼠标" };
            }

            string battery = status.HasBattery
                ? status.BatteryPercent + "%" + (status.Charging ? " · 充电中" : "")
                : "电量未知";
            string dpi = status.DpiX > 0 ? status.DpiX + " DPI" : "DPI 未知";
            string stage = status.ActiveStage > 0 ? " · 第 " + status.ActiveStage + " 档" : "";

            return new string[]
            {
                "雷蛇鼠标 · " + battery,
                dpi + " · " + status.PollingHz + " Hz" + stage
            };
        }

        private void OnTrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            TogglePanel();
        }

        // ---------------- 悬停文字气泡 ----------------

        private void OnTrayMouseMove(object sender, MouseEventArgs e)
        {
            if (_panel.Visible) return;      // 面板开着就不用气泡了

            Point cursor = Cursor.Position;
            _tipAnchor = cursor;

            if (!_tip.Visible)
            {
                _tip.Update(BuildTipLines(_status));
                _tip.ShowAt(cursor);
            }

            // 指针离开图标后 NotifyIcon.MouseMove 不再触发，靠下面的定时器判断
            if (!_tipTimer.Enabled) _tipTimer.Start();
        }

        /// <summary>指针离开图标就立刻收起（50ms 一跳，不做延迟）。</summary>
        private void OnTipWatch(object sender, EventArgs e)
        {
            if (!_tip.Visible)
            {
                _tipTimer.Stop();
                return;
            }

            if (_panel.Visible)
            {
                HideTip();
                return;
            }

            Point cursor = Cursor.Position;
            int radius = Theme.Z(TipAnchorRadius);
            bool onIcon = Math.Abs(cursor.X - _tipAnchor.X) <= radius
                       && Math.Abs(cursor.Y - _tipAnchor.Y) <= radius;

            if (!onIcon) HideTip();
        }

        private void HideTip()
        {
            _tipTimer.Stop();
            if (_tip.Visible) _tip.Hide();
        }

        public void ShowPanel()
        {
            HideTip();
            _panel.ApplyStatus(_status);
            _panel.ShowAtTray();
            _alertActive = false;
            _tray.Icon = _icons.Get(_status, false);
            _service.RefreshAll();   // 弹出即重读一次，面板最多等 0.4 秒变新鲜
        }

        private void TogglePanel()
        {
            if (_panel.Visible) _panel.HidePanel();
            else ShowPanel();
        }

        private void ShowAbout()
        {
            _alertActive = false;
            _tray.Icon = _icons.Get(_status, false);
            MessageBox.Show(
                "雷蛇鼠标控制器 1.0\n\n"
                + "原生 Win32/HID 直连，不装驱动、不联网。\n"
                + "左键托盘图标打开面板，右键看菜单，鼠标停在图标上会显示状态文字。\n\n"
                + "由 DeepSeek-V4.1-Flash 在 DeepSeek Harness 上完成\n"
                + "2026-10-17 · github.com/chuzhongzai/razer-mouse-controller\n\n"
                + "配置文件：" + Settings.FilePath,
                "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------------- 单实例唤醒 ----------------

        private void StartShowWatcher()
        {
            _showEvent = SingleInstance.CreateShowEvent();
            if (_showEvent == null) return;

            _showWatcher = new Thread(delegate()
            {
                while (!_disposed)
                {
                    if (!_showEvent.WaitOne(500)) continue;
                    if (_disposed) return;
                    Post(delegate { ShowPanel(); });
                }
            });
            _showWatcher.IsBackground = true;
            _showWatcher.Name = "RazerMouse.ShowWatcher";
            _showWatcher.Start();
        }

        private void Post(SendOrPostCallback callback)
        {
            if (_ui != null) _ui.Post(callback, null);
            else callback(null);
        }

        // ---------------- 退出 ----------------

        public void Exit()
        {
            if (_disposed) return;

            if (_settings.RestoreOnExit && _service.HasInitialSnapshot)
            {
                _service.RestoreInitialAndWait(3000);
            }

            _panel.HidePanel();
            Dispose();
            ExitThread();
        }

        public new void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _tray.Visible = false;
                _tray.MouseClick -= OnTrayClick;
                _tray.MouseMove -= OnTrayMouseMove;
                _tray.Dispose();
            }
            catch { }

            _tipTimer.Stop();
            _tipTimer.Dispose();
            _tip.Dispose();
            _panel.Dispose();
            _menu.Dispose();

            _service.StatusChanged -= OnStatusChanged;
            _service.Faulted -= OnFaulted;
            _service.Dispose();
            _icons.Dispose();

            if (_showEvent != null)
            {
                try { _showEvent.Set(); } catch { }
                try { _showEvent.Dispose(); } catch { }
                _showEvent = null;
            }
        }
    }
}