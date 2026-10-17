// 主面板：电量 + DPI 档位 + 回报率 + 写入方式。无边框深色、点外面自动收起。
using System;
using System.Drawing;
using System.Windows.Forms;
using RazerMouse.App;

namespace RazerMouse.Ui
{
    internal sealed class PanelForm : DarkForm
    {
        private const int Pad = 16;
        private const int PanelWidth = 360;
        private const int PanelHeight = 352;

        private readonly MouseService _service;
        private readonly Settings _settings;

        private readonly Chip[] _stageChips = new Chip[5];
        private readonly Chip[] _pollChips = new Chip[3];
        private readonly Chip _storeChip;
        private readonly Chip _restoreChip;
        private readonly Chip _autostartChip;
        private readonly Chip _refreshChip;
        private readonly Chip _exitChip;
        private readonly ToolTip _tips = new ToolTip();

        private MouseStatus _status = new MouseStatus();

        private bool _dragging;
        private bool _allowHide = true;   // 弹出子对话框时不因失焦而收起
        private Point _dragOrigin;
        private Point _dragStart;

        /// <summary>点「退出程序」时触发。</summary>
        public event EventHandler ExitRequested;

        /// <summary>面板里改了开机自启后触发，托盘菜单据此同步勾选。</summary>
        public event EventHandler AutostartChanged;

        public PanelForm(MouseService service, Settings settings)
        {
            _service = service;
            _settings = settings;

            DrawBorder = true;
            Radius = 12;
            TopMost = true;
            ClientSize = new Size(PanelWidth, PanelHeight);
            Text = "雷蛇鼠标控制器";

            BuildStageChips();
            BuildPollChips();

            _storeChip = new Chip();
            _storeChip.Bounds = new Rectangle(Pad, 252, 168, 32);
            _storeChip.Font = Theme.Body;
            _storeChip.Click += delegate { ToggleStore(); };
            Controls.Add(_storeChip);

            _restoreChip = new Chip();
            _restoreChip.Bounds = new Rectangle(Pad + 176, 252, 168, 32);
            _restoreChip.Font = Theme.Body;
            _restoreChip.Text = "恢复到启动时";
            _restoreChip.Click += delegate { RestoreAsked(); };
            Controls.Add(_restoreChip);

            _autostartChip = new Chip();
            _autostartChip.Bounds = new Rectangle(Pad, 292, 118, 30);
            _autostartChip.Font = Theme.Body;
            _autostartChip.Click += delegate { ToggleAutostart(); };
            Controls.Add(_autostartChip);

            _refreshChip = new Chip();
            _refreshChip.Bounds = new Rectangle(Pad + 126, 292, 96, 30);
            _refreshChip.Font = Theme.Body;
            _refreshChip.Ghost = true;
            _refreshChip.Text = "立即刷新";
            _refreshChip.TextColor = Theme.Muted;
            _refreshChip.Click += delegate { _service.RefreshAll(); };
            Controls.Add(_refreshChip);

            _exitChip = new Chip();
            _exitChip.Bounds = new Rectangle(PanelWidth - Pad - 98, 292, 98, 30);
            _exitChip.Font = Theme.Body;
            _exitChip.Ghost = true;
            _exitChip.Text = "退出程序";
            _exitChip.TextColor = Theme.Muted;
            _exitChip.Click += delegate
            {
                EventHandler handler = ExitRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            Controls.Add(_exitChip);

            UpdateStoreChip();
            UpdateAutostartChip();
        }

        // ---------------- 构建 ----------------

        private void BuildStageChips()
        {
            const int gap = 7;
            int width = (PanelWidth - Pad * 2 - gap * 4) / 5;
            for (int i = 0; i < 5; i++)
            {
                var chip = new Chip();
                chip.Bounds = new Rectangle(Pad + i * (width + gap), 124, width, 40);
                chip.Text = "--";
                chip.Enabled = false;
                int index = i;
                chip.Click += delegate { ChooseStage(index); };
                chip.DoubleClick += delegate { EditStage(index); };
                _tips.SetToolTip(chip, "第 " + (i + 1) + " 档（双击改数值）");
                _stageChips[i] = chip;
                Controls.Add(chip);
            }
        }

        private void BuildPollChips()
        {
            int[] rates = { 125, 500, 1000 };
            const int gap = 8;
            int width = (PanelWidth - Pad * 2 - gap * 2) / 3;
            for (int i = 0; i < rates.Length; i++)
            {
                var chip = new Chip();
                chip.Bounds = new Rectangle(Pad + i * (width + gap), 196, width, 34);
                int hz = rates[i];
                chip.Text = hz + " Hz";
                chip.Click += delegate { _service.SetPolling(hz); };
                _pollChips[i] = chip;
                Controls.Add(chip);
            }
        }

        // ---------------- 状态刷新 ----------------

        public void ApplyStatus(MouseStatus status)
        {
            _status = status;
            RefreshChips();
            Invalidate();
            if (Visible) Update();
        }

        private void RefreshChips()
        {
            bool online = _status.Connected;
            int count = _status.Stages.Count;

            for (int i = 0; i < _stageChips.Length; i++)
            {
                Chip chip = _stageChips[i];
                bool exists = i < count;
                chip.Enabled = online && exists && !_status.Busy;
                chip.Text = exists ? _status.Stages[i].X.ToString() : "--";
                chip.Selected = exists && (_status.ActiveStage == i + 1);
            }

            int[] rates = { 125, 500, 1000 };
            for (int i = 0; i < _pollChips.Length; i++)
            {
                Chip chip = _pollChips[i];
                chip.Enabled = online && !_status.Busy;
                chip.Selected = online && _status.PollingHz == rates[i];
            }

            _storeChip.Enabled = online && !_status.Busy;
            _restoreChip.Enabled = _service.HasInitialSnapshot;
            UpdateStoreChip();
        }

        private void UpdateStoreChip()
        {
            bool store = _settings.StoreOnboard;
            _storeChip.Text = store ? "写入板载内存：开" : "写入板载内存：关";
            _storeChip.Selected = store;
            _tips.SetToolTip(_storeChip, store
                ? "设置会写进鼠标自带内存，换电脑也保留"
                : "只改当前生效值，重启鼠标后恢复（推荐）");
        }

        private void UpdateAutostartChip()
        {
            bool on = Startup.IsEnabled();
            _autostartChip.Text = on ? "开机自启：开" : "开机自启：关";
            _autostartChip.Selected = on;
            _tips.SetToolTip(_autostartChip, on
                ? "登录 Windows 后自动在托盘启动（不弹面板）"
                : "点一下让程序随 Windows 启动");
        }

        private void ToggleAutostart()
        {
            bool next = !Startup.IsEnabled();
            if (!Startup.SetEnabled(next))
            {
                _allowHide = false;
                try
                {
                    MessageBox.Show(this, "改不了开机自启：\n\n" + Startup.LastError +
                        "\n\n可以手动把快捷方式放进 shell:startup 文件夹。",
                        "开机自启", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    _allowHide = true;
                }
                UpdateAutostartChip();
                return;
            }

            _settings.AutoStart = next;
            _settings.Save();
            UpdateAutostartChip();

            EventHandler handler = AutostartChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        // ---------------- 动作 ----------------

        private void ChooseStage(int index)
        {
            if (!_status.Connected || _status.Busy) return;
            if (index >= _status.Stages.Count) return;
            _service.SwitchStage(index + 1);
        }

        private void EditStage(int index)
        {
            if (!_status.Connected || _status.Busy) return;
            if (index >= _status.Stages.Count) return;

            _allowHide = false;
            try
            {
                int value;
                bool ok = ValueDialog.Ask(this, "修改第 " + (index + 1) + " 档",
                    "DPI 数值（X/Y 同步设置）", _status.Stages[index].X,
                    Protocol.MouseCommands.MinDpi, Protocol.MouseCommands.MaxDpi, out value);
                if (ok) _service.SetStageDpi(index + 1, value);
            }
            finally
            {
                _allowHide = true;
            }
        }

        private void ToggleStore()
        {
            _service.SetStoreOnboard(!_settings.StoreOnboard);
            UpdateStoreChip();
            Invalidate();
        }

        private void RestoreAsked()
        {
            _allowHide = false;
            try
            {
                DialogResult answer = MessageBox.Show(this,
                    "把 DPI、DPI 档位表和回报率恢复成程序启动时读到的值？",
                    "恢复到启动时", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (answer == DialogResult.Yes) _service.RestoreInitial();
            }
            finally
            {
                _allowHide = true;
            }
        }

        // ---------------- 显示与定位 ----------------

        public void ShowAtTray()
        {
            UpdateAutostartChip();   // 外部可能改过（命令行/菜单），每次弹出都对齐一次
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Point location;
            if (_settings.PanelX >= 0 && _settings.PanelY >= 0)
            {
                location = new Point(_settings.PanelX, _settings.PanelY);
                var probe = new Rectangle(location, Size);
                if (!work.Contains(probe))
                    location = new Point(work.Right - Width - 12, work.Bottom - Height - 12);
            }
            else
            {
                location = new Point(work.Right - Width - 12, work.Bottom - Height - 12);
            }

            Location = location;
            Show();
            Activate();
            BringToFront();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_allowHide && Visible) HidePanel();
        }

        public void HidePanel()
        {
            _settings.PanelX = Left;
            _settings.PanelY = Top;
            _settings.Save();
            Hide();
        }

        // ---------------- 绘制 ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Theme.Setup(g);

            bool online = _status.Connected;
            bool busy = _status.Busy;

            // 标题
            Theme.DrawText(g, "DeathAdder V3 Pro", Theme.Title, Theme.Text,
                new Rectangle(Pad, 10, 210, 22), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // 右上角状态
            string stateText = busy ? (_status.BusyText.Length > 0 ? _status.BusyText : "处理中…")
                : (online ? (_status.Charging ? "充电中" : "已连接") : "未连接");
            Color stateColor = busy ? Theme.Warning : (online ? Theme.Accent : Theme.Danger);
            var stateRect = new Rectangle(PanelWidth - Pad - 120, 10, 120, 22);
            Theme.DrawText(g, stateText, Theme.Small, stateColor, stateRect,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            using (var dot = new SolidBrush(stateColor))
            {
                Size textSize = TextRenderer.MeasureText(stateText, Theme.Small);
                int dotX = stateRect.Right - textSize.Width - 12;
                g.FillEllipse(dot, dotX, 18, 6, 6);
            }

            // 电量大字
            string percentText = _status.HasBattery ? _status.BatteryPercent + "%" : "--";
            Color levelColor = Theme.LevelColor(_status.BatteryPercent, _status.Charging);
            Theme.DrawText(g, percentText, Theme.Battery, online ? levelColor : Theme.Muted,
                new Rectangle(Pad - 3, 36, 120, 42), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // 电量说明
            string caption = !online ? "正在寻找设备…"
                : (!_status.HasBattery ? "读不到电量"
                    : (_status.Charging ? "正在充电" : "使用电池"));
            Theme.DrawText(g, caption, Theme.Body, online ? Theme.Text : Theme.Muted,
                new Rectangle(Pad + 108, 40, 140, 20), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            string updated = _status.LastError != null
                ? _status.LastError
                : (_status.UpdatedUtc == DateTime.MinValue
                    ? "等待数据"
                    : "更新于 " + _status.UpdatedUtc.ToLocalTime().ToString("HH:mm:ss"));
            Theme.DrawText(g, updated, Theme.Small,
                _status.LastError != null ? Theme.Warning : Theme.Muted,
                new Rectangle(Pad + 108, 60, PanelWidth - Pad * 2 - 108, 18),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // 电量条
            var track = new Rectangle(Pad, 88, PanelWidth - Pad * 2, 8);
            Theme.Fill(g, track, 4, Theme.Card);
            if (_status.HasBattery && _status.BatteryPercent > 0)
            {
                int fillWidth = (int)Math.Round(track.Width * (_status.BatteryPercent / 100.0));
                if (fillWidth < 8) fillWidth = 8;
                Theme.Fill(g, new Rectangle(track.X, track.Y, fillWidth, track.Height), 4,
                    online ? levelColor : Theme.Muted);
            }

            // 分区标题
            Theme.DrawText(g, "DPI 档位", Theme.Small, Theme.Muted,
                new Rectangle(Pad, 104, 120, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Theme.DrawText(g, "单击切换 · 双击改数值", Theme.Small, Theme.Muted,
                new Rectangle(PanelWidth - Pad - 200, 104, 200, 18),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            Theme.DrawText(g, "回报率", Theme.Small, Theme.Muted,
                new Rectangle(Pad, 176, 120, 18), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // 分隔线 / 离线提示
            if (online)
            {
                using (var pen = new Pen(Theme.Border, 1f))
                {
                    g.DrawLine(pen, Pad, 240, PanelWidth - Pad, 240);
                }
            }
            else
            {
                Theme.DrawText(g, "设备未连接：插好接收器或唤醒鼠标，每 2 秒自动重试",
                    Theme.Small, Theme.Danger, new Rectangle(Pad, 232, PanelWidth - Pad * 2, 18),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        // ---------------- 拖动 ----------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _dragStart = Location;
            _dragOrigin = Cursor.Position;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            Point now = Cursor.Position;
            Location = new Point(_dragStart.X + now.X - _dragOrigin.X, _dragStart.Y + now.Y - _dragOrigin.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }
    }
}
