// 应用主体：托盘图标 + 面板 + 悬停卡 + 菜单 + 设备服务的生命周期。
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
        private readonly HoverCard _card;
        private readonly System.Windows.Forms.Timer _cardTimer;

        private EventWaitHandle _showEvent;
        private Thread _showWatcher;
        private MouseStatus _status = new MouseStatus();
        private Point _cardAnchor;
        private int _cardIdleMs;

        /// <summary>指针离托盘图标多远内仍算"还在悬停"（像素）。</summary>
        private const int CardAnchorRadius = 28;

        /// <summary>指针离开图标与卡片多久后收起悬停卡（毫秒）。</summary>
        private const int CardHideDelayMs = 900;
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

            _card = new HoverCard();

            _cardTimer = new System.Windows.Forms.Timer();
            _cardTimer.Interval = 100;           // 100ms 看一次指针，0.9 秒后收起
            _cardTimer.Tick += OnCardWatch;

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
            _tray.Text = "雷蛇鼠标控制器";
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
                _alertActive = true;
                _tray.Icon = _icons.Get(_status, true);
                _tray.ShowBalloonTip(8000, e.Title, e.Text,
                    e.Critical ? ToolTipIcon.Error : ToolTipIcon.Warning);
                _card.Update(_status, true);
            });
        }

        // ---------------- 界面更新（UI 线程） ----------------

        private void ApplyStatus(MouseStatus status)
        {
            _status = status;
            if (status.Connected && status.LastError == null) _faultShown = false;

            _tray.Icon = _icons.Get(status, _alertActive);
            _tray.Text = BuildTooltip(status);
            _panel.ApplyStatus(status);
            _card.Update(status, _alertActive);

            _alert.Update(status);
        }

        private static string BuildTooltip(MouseStatus status)
        {
            if (!status.Connected) return "雷蛇鼠标 · 未连接";

            string battery = status.HasBattery
                ? status.BatteryPercent + "%" + (status.Charging ? "（充电中）" : "")
                : "电量未知";
            string dpi = status.DpiX > 0 ? status.DpiX + " DPI" : "DPI 未知";
            return "雷蛇鼠标 · " + battery + "\n" + dpi + " · " + status.PollingHz + " Hz";
        }

        private void OnTrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            TogglePanel();
        }

        private void OnTrayMouseMove(object sender, MouseEventArgs e)
        {
            Point cursor = Cursor.Position;

            if (!_card.Visible)
            {
                if (_panel.Visible) return;          // 面板开着就不再叠一张卡
                _cardAnchor = cursor;
                _cardIdleMs = 0;
                _card.Update(_status, _alertActive);
                _card.ShowNear(cursor);
            }

            // 注意：指针离开托盘图标后 NotifyIcon.MouseMove 就不再触发了，
            // 所以"移走就收起"必须靠下面的宿卫定时器判断，不能只在这里判断。
            _cardIdleMs = 0;
            if (!_cardTimer.Enabled) _cardTimer.Start();
        }

        /// <summary>悬停卡宿卫：指针离开图标和卡片区域约 0.9 秒后自动收起。</summary>
        private void OnCardWatch(object sender, EventArgs e)
        {
            if (!_card.Visible)
            {
                _cardTimer.Stop();
                return;
            }

            if (_panel.Visible)
            {
                _cardTimer.Stop();
                _card.Hide();
                return;
            }

            Point cursor = Cursor.Position;

            bool onCard = _card.Bounds.Contains(cursor);
            bool onIcon = Math.Abs(cursor.X - _cardAnchor.X) <= CardAnchorRadius
                       && Math.Abs(cursor.Y - _cardAnchor.Y) <= CardAnchorRadius;

            if (onCard || onIcon)
            {
                _cardIdleMs = 0;
                return;
            }

            _cardIdleMs += _cardTimer.Interval;
            if (_cardIdleMs >= CardHideDelayMs)
            {
                _cardTimer.Stop();
                _card.Hide();
            }
        }

        public void ShowPanel()
        {
            _cardTimer.Stop();
            _card.Hide();
            _panel.ApplyStatus(_status);
            _panel.ShowAtTray();
            _alertActive = false;
            _tray.Icon = _icons.Get(_status, false);
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
                + "左键托盘图标打开面板，右键看菜单，悬停看状态。\n\n"
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

            _cardTimer.Stop();
            _cardTimer.Dispose();
            _card.Dispose();
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
