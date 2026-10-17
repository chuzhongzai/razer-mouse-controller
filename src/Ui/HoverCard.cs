// 托盘悬停时弹出的迷你状态卡：鼠标停在图标上就能看到电量和当前设置。
using System;
using System.Drawing;
using System.Windows.Forms;
using RazerMouse.App;

namespace RazerMouse.Ui
{
    internal sealed class HoverCard : DarkForm
    {
        private MouseStatus _status = new MouseStatus();
        private bool _alert;

        public HoverCard()
        {
            DrawBorder = true;
            Radius = 10;
            Size = new Size(248, 118);
            TopMost = true;
            ShowInTaskbar = false;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public void Update(MouseStatus status, bool alert)
        {
            _status = status;
            _alert = alert;
            if (Visible) Invalidate();
        }

        public void ShowNear(Point screenPoint)
        {
            Point location = new Point(screenPoint.X + 14, screenPoint.Y - Height - 14);
            Rectangle work = Screen.FromPoint(screenPoint).WorkingArea;
            if (location.X + Width > work.Right) location.X = work.Right - Width - 4;
            if (location.X < work.Left) location.X = work.Left + 4;
            if (location.Y < work.Top) location.Y = screenPoint.Y + 22;
            if (location.Y + Height > work.Bottom) location.Y = work.Bottom - Height - 4;
            Location = location;
            if (!Visible) Show();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Theme.Setup(g);

            int pad = 12;
            var titleRect = new Rectangle(pad, 8, Width - pad * 2 - 52, 18);
            Theme.DrawText(g, string.IsNullOrEmpty(_status.Serial) ? "雷蛇鼠标" : "DeathAdder V3 Pro",
                Theme.BodyBold, Theme.Text, titleRect, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            string stateText = !_status.Connected ? "未连接" : (_status.Charging ? "充电中" : "已连接");
            Color stateColor = !_status.Connected ? Theme.Danger : Theme.Accent;
            var stateRect = new Rectangle(Width - pad - 52, 8, 52, 18);
            Theme.DrawText(g, stateText, Theme.Small, stateColor, stateRect,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            string percentText = _status.HasBattery
                ? _status.BatteryPercent.ToString() + "%"
                : "--";
            using (var big = new Font(Theme.Battery.FontFamily, 19f, FontStyle.Bold))
            {
                var bigRect = new Rectangle(pad - 2, 24, 92, 34);
                Theme.DrawText(g, percentText, big, Theme.LevelColor(_status.BatteryPercent, _status.Charging),
                    bigRect, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }

            string charging = !_status.HasBattery ? "无电量信息"
                : (_status.Charging ? "正在充电" : "使用电池");
            var chargingRect = new Rectangle(pad + 88, 34, Width - pad * 2 - 88, 18);
            Theme.DrawText(g, charging, Theme.Small, _status.Charging ? Theme.Accent : Theme.Muted,
                chargingRect, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            var barRect = new Rectangle(pad, 62, Width - pad * 2, 6);
            DrawBar(g, barRect, _status);

            string detail = _status.Connected
                ? string.Format("{0} DPI · {1} Hz · 第 {2} 档",
                    _status.DpiX, _status.PollingHz > 0 ? _status.PollingHz.ToString() : "?", _status.ActiveStage)
                : "打开面板可重新连接";
            var detailRect = new Rectangle(pad, 74, Width - pad * 2, 16);
            Theme.DrawText(g, detail, Theme.Small, _status.Connected ? Theme.Text : Theme.Muted,
                detailRect, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            string updated = _status.LastError != null
                ? _status.LastError
                : (_status.UpdatedUtc == DateTime.MinValue
                    ? "等待数据"
                    : "更新于 " + _status.UpdatedUtc.ToLocalTime().ToString("HH:mm:ss"));
            var updatedRect = new Rectangle(pad, 94, Width - pad * 2, 16);
            Theme.DrawText(g, updated, Theme.Small, _status.LastError != null ? Theme.Warning : Theme.Muted,
                updatedRect, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private void DrawBar(Graphics g, Rectangle bounds, MouseStatus status)
        {
            Theme.Fill(g, bounds, 3, Theme.Card);
            if (!status.HasBattery) return;
            int percent = status.BatteryPercent;
            if (percent <= 0) return;
            int width = (int)Math.Round(bounds.Width * (percent / 100.0));
            if (width < 3) width = 3;
            var fill = new Rectangle(bounds.X, bounds.Y, width, bounds.Height);
            Theme.Fill(g, fill, 3, Theme.LevelColor(percent, status.Charging));
        }
    }
}
