// 全局视觉：深色 + 雷蛇绿。所有界面元素都从这里取色取字，避免各画各的。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RazerMouse.Ui
{
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(0x17, 0x17, 0x1C);
        public static readonly Color Card = Color.FromArgb(0x23, 0x23, 0x2B);
        public static readonly Color CardHover = Color.FromArgb(0x2E, 0x2E, 0x38);
        public static readonly Color CardPressed = Color.FromArgb(0x38, 0x38, 0x44);
        public static readonly Color Border = Color.FromArgb(0x38, 0x38, 0x44);
        public static readonly Color Text = Color.FromArgb(0xF1, 0xF1, 0xF6);
        public static readonly Color Muted = Color.FromArgb(0x97, 0x97, 0xA6);
        public static readonly Color Accent = Color.FromArgb(0x44, 0xD6, 0x2C);
        public static readonly Color Warning = Color.FromArgb(0xF2, 0xB0, 0x1E);
        public static readonly Color Danger = Color.FromArgb(0xE5, 0x48, 0x4D);

        private static readonly string Family = PickFamily();

        public static readonly Font Title = new Font(Family, 10.5f, FontStyle.Bold);
        public static readonly Font Body = new Font(Family, 9f, FontStyle.Regular);
        public static readonly Font BodyBold = new Font(Family, 9f, FontStyle.Bold);
        public static readonly Font Small = new Font(Family, 8.25f, FontStyle.Regular);
        public static readonly Font Battery = new Font(Family, 26f, FontStyle.Bold);
        public static readonly Font Chip = new Font(Family, 9.75f, FontStyle.Bold);
        public static readonly Font Stage = new Font(Family, 8.25f, FontStyle.Regular);

        private static string PickFamily()
        {
            foreach (string name in new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" })
            {
                try
                {
                    using (var probe = new FontFamily(name)) { return name; }
                }
                catch { }
            }
            return "Segoe UI";
        }

        /// <summary>按电量给颜色：绿/黄/红。</summary>
        public static Color LevelColor(int percent, bool charging)
        {
            if (charging) return Accent;
            if (percent <= 10) return Danger;
            if (percent <= 20) return Warning;
            return Accent;
        }

        public static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Fill(Graphics g, Rectangle bounds, int radius, Color color)
        {
            using (GraphicsPath path = Rounded(bounds, radius))
            using (var brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        public static void Stroke(Graphics g, Rectangle bounds, int radius, Color color, float width)
        {
            using (GraphicsPath path = Rounded(bounds, radius))
            using (var pen = new Pen(color, width))
            {
                g.DrawPath(pen, path);
            }
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds,
            TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text, font, bounds, color, flags | TextFormatFlags.NoPadding);
        }

        public static void Setup(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        }
    }
}
