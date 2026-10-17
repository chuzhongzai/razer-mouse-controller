// 托盘图标 = 一块电池：填充比例就是电量，颜色表示健康度，闪电表示在充电。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using RazerMouse.App;

namespace RazerMouse.Ui
{
    internal sealed class TrayIconRenderer : IDisposable
    {
        private readonly Dictionary<string, Icon> _cache = new Dictionary<string, Icon>();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        public Icon Get(MouseStatus status, bool alert)
        {
            bool online = status.Connected;
            bool charging = status.Charging && online;
            int percent = status.HasBattery ? status.BatteryPercent : -1;
            if (percent >= 0)
            {
                percent = (percent / 5) * 5;               // 按 5% 分桶，减少图标数量
                if (percent > 100) percent = 100;
            }

            string key = (online ? "1" : "0") + (charging ? "1" : "0")
                         + (alert ? "1" : "0") + ":" + percent;
            Icon cached;
            if (_cache.TryGetValue(key, out cached)) return cached;

            Icon icon = Build(percent, charging, online, alert);
            _cache[key] = icon;
            return icon;
        }

        private static Icon Build(int percent, bool charging, bool online, bool alert)
        {
            using (var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                Color level = !online ? Color.FromArgb(0x60, 0x60, 0x6C)
                    : (alert || percent <= 10 ? Theme.Danger
                        : (percent <= 20 ? Theme.Warning : Theme.Accent));
                Color outline = alert ? Theme.Danger : Color.FromArgb(0xE6, 0xE6, 0xEE);

                var body = new Rectangle(1, 9, 24, 15);
                var cap = new Rectangle(26, 13, 4, 7);

                using (var outlinePen = new Pen(outline, 2f))
                using (GraphicsPath bodyPath = Theme.Rounded(body, 3))
                {
                    g.DrawPath(outlinePen, bodyPath);
                }
                using (var capBrush = new SolidBrush(outline))
                {
                    g.FillRectangle(capBrush, cap);
                }

                if (percent > 0)
                {
                    int innerWidth = (int)Math.Round((body.Width - 6) * (percent / 100.0));
                    if (innerWidth < 2) innerWidth = 2;
                    var fill = new Rectangle(body.X + 3, body.Y + 3, innerWidth, body.Height - 6);
                    using (var brush = new SolidBrush(level))
                    {
                        g.FillRectangle(brush, fill);
                    }
                }

                if (!online)
                {
                    using (var pen = new Pen(Theme.Danger, 2.4f))
                    {
                        g.DrawLine(pen, 4, 26, 25, 7);
                    }
                }
                else if (charging)
                {
                    Point[] bolt = new Point[]
                    {
                        new Point(17, 4), new Point(9, 18), new Point(14, 18),
                        new Point(12, 28), new Point(21, 13), new Point(16, 13)
                    };
                    using (var brush = new SolidBrush(Color.White))
                    {
                        g.FillPolygon(brush, bolt);
                    }
                }

                IntPtr handle = bmp.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                    {
                        return (Icon)temporary.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        public void Dispose()
        {
            foreach (Icon icon in _cache.Values) icon.Dispose();
            _cache.Clear();
        }
    }
}
