// 诊断用：--shot <目录> 把面板、悬停卡和托盘图标离线渲染成 PNG。
// 走 DrawToBitmap，不依赖桌面焦点，方便在没有人工操作时验证界面。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using RazerMouse.Ui;

namespace RazerMouse.App
{
    internal static class Shot
    {
        public static int Run(string folder)
        {
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) { Console.WriteLine("无法创建目录：" + ex.Message); return 1; }

            Ui.Theme.InitDpi();   // 截出来的图要和屏幕上一样清晰

            var settings = Settings.Load();
            var service = new MouseService(settings);
            service.Start();

            MouseStatus status = null;
            for (int i = 0; i < 40; i++)
            {
                MouseStatus current = service.Status;
                if (current.Connected && current.HasBattery) { status = current; break; }
                Thread.Sleep(150);
            }
            if (status == null)
            {
                status = service.Status;
                Console.WriteLine("警告：没连上设备，用空状态渲染。");
            }

            Application.EnableVisualStyles();

            using (var panel = new PanelForm(service, settings))
            {
                panel.ApplyStatus(status);
                panel.StartPosition = FormStartPosition.Manual;
                panel.Location = new Point(0, 0);
                panel.Show();
                Application.DoEvents();
                Save(panel, Path.Combine(folder, "panel.png"));
                panel.Hide();
            }

            using (var tip = new TextTip())
            {
                tip.Update(TrayApp.BuildTipLines(status));
                tip.Location = new Point(0, 0);
                tip.Show();
                Application.DoEvents();
                Save(tip, Path.Combine(folder, "tip.png"));
                tip.Hide();
            }

            SaveTrayIcons(status, folder);
            service.Dispose();
            Console.WriteLine("已输出到 " + folder);
            return 0;
        }

        private static void Save(Form form, string path)
        {
            using (var bmp = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb))
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(path, ImageFormat.Png);
            }
            Console.WriteLine("  " + Path.GetFileName(path) + "  " + form.Width + "x" + form.Height);
        }

        /// <summary>把几种电量状态并排画出来（32px 与放大 4 倍的 16px 效果）。</summary>
        private static void SaveTrayIcons(MouseStatus status, string folder)
        {
            var states = new MouseStatus[]
            {
                Make(87, false, true, false),
                Make(51, false, true, false),
                Make(15, false, true, false),
                Make(4, false, true, true),
                Make(64, true, true, false),
                Make(0, false, false, false)
            };

            using (var renderer = new TrayIconRenderer())
            using (var sheet = new Bitmap(6 * 140, 190))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(0x20, 0x20, 0x26));
                g.SmoothingMode = SmoothingMode.None;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;

                for (int i = 0; i < states.Length; i++)
                {
                    Icon icon = renderer.Get(states[i], states[i].BatteryPercent <= 5);
                    using (Bitmap big = icon.ToBitmap())
                    {
                        g.DrawImage(big, new Rectangle(i * 140 + 10, 10, 120, 120));
                        g.DrawImage(big, new Rectangle(i * 140 + 40, 140, 16, 16));
                    }
                }
                sheet.Save(Path.Combine(folder, "tray-states.png"), ImageFormat.Png);
            }
            Console.WriteLine("  tray-states.png");

            // 当前真实状态单独一张
            using (var renderer = new TrayIconRenderer())
            {
                Icon icon = renderer.Get(status, false);
                using (Bitmap big = icon.ToBitmap())
                using (var scaled = new Bitmap(128, 128))
                using (Graphics g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(big, new Rectangle(0, 0, 128, 128));
                    scaled.Save(Path.Combine(folder, "tray-current.png"), ImageFormat.Png);
                }
                Console.WriteLine("  tray-current.png");
            }
        }

        private static MouseStatus Make(int percent, bool charging, bool connected, bool alert)
        {
            var status = new MouseStatus();
            status.Connected = connected;
            status.HasBattery = true;
            status.BatteryPercent = percent;
            status.BatteryRaw = percent;
            status.Charging = charging;
            status.DpiX = 3200;
            status.PollingHz = 1000;
            status.ActiveStage = 4;
            status.Stages.Add(new Protocol.DpiStage(400, 400));
            status.Stages.Add(new Protocol.DpiStage(800, 800));
            status.Stages.Add(new Protocol.DpiStage(1600, 1600));
            status.Stages.Add(new Protocol.DpiStage(3200, 3200));
            status.Stages.Add(new Protocol.DpiStage(6400, 6400));
            status.UpdatedUtc = DateTime.UtcNow;
            return status;
        }
    }
}
