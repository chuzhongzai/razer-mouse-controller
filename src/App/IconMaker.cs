// --make-icon：生成 assets\app.ico（exe 图标 + 托盘回落图标共用）。
// 画法：深色圆角底 + 绿色电池，跟界面色板一致。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace RazerMouse.App
{
    internal static class IconMaker
    {
        private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

        public static int Run(string path)
        {
            try
            {
                string folder = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                byte[][] images = new byte[Sizes.Length][];
                for (int i = 0; i < Sizes.Length; i++)
                {
                    using (Bitmap bmp = Draw(Sizes[i]))
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        images[i] = ms.ToArray();
                    }
                }

                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var w = new BinaryWriter(fs))
                {
                    w.Write((ushort)0);              // reserved
                    w.Write((ushort)1);              // type = icon
                    w.Write((ushort)Sizes.Length);   // 图像数量

                    int offset = 6 + 16 * Sizes.Length;
                    for (int i = 0; i < Sizes.Length; i++)
                    {
                        int size = Sizes[i];
                        w.Write((byte)(size >= 256 ? 0 : size));   // 宽（256 写 0）
                        w.Write((byte)(size >= 256 ? 0 : size));   // 高
                        w.Write((byte)0);                          // 调色板数
                        w.Write((byte)0);                          // reserved
                        w.Write((ushort)1);                        // planes
                        w.Write((ushort)32);                       // bpp
                        w.Write((uint)images[i].Length);
                        w.Write((uint)offset);
                        offset += images[i].Length;
                    }
                    for (int i = 0; i < images.Length; i++) w.Write(images[i]);
                }

                Console.WriteLine("已生成 " + path + "（" + string.Join("/", Array.ConvertAll(Sizes, s => s.ToString())) + "）");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("生成图标失败：" + ex.Message);
                return 1;
            }
        }

        private static Bitmap Draw(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                float pad = size * 0.06f;
                var bg = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);
                float radius = size * 0.22f;

                using (GraphicsPath path = Round(bg, radius))
                using (var brush = new SolidBrush(Color.FromArgb(0x23, 0x23, 0x2B)))
                using (var pen = new Pen(Color.FromArgb(0x44, 0xD6, 0x2C), Math.Max(1f, size / 30f)))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(pen, path);
                }

                // 电池主体（电池身 + 正极帽作为一个整体居中）
                float bw = size * 0.44f;
                float bh = size * 0.24f;
                float capW = size * 0.05f;
                float bx = (size - (bw + capW * 0.7f)) / 2f;
                float by = (size - bh) / 2f;
                var body = new RectangleF(bx, by, bw, bh);
                using (GraphicsPath path = Round(body, size * 0.05f))
                using (var brush = new SolidBrush(Color.FromArgb(0x44, 0xD6, 0x2C)))
                {
                    g.FillPath(brush, path);
                }

                // 电池正极帽
                var cap = new RectangleF(bx + bw, by + bh * 0.28f, capW, bh * 0.44f);
                using (var brush = new SolidBrush(Color.FromArgb(0x44, 0xD6, 0x2C)))
                {
                    g.FillRectangle(brush, cap);
                }
            }
            return bmp;
        }

        private static GraphicsPath Round(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
