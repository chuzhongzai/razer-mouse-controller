// --hoverzone [x,y]：打印屏幕/任务栏布局，并说明某个点算不算"托盘附近"（决定悬停气泡会不会显示）。
// 纯粹是诊断用：用户报告"气泡自己冒出来"时，先看这个坐标判定是不是对的。
using System;
using System.Drawing;
using System.Windows.Forms;
using RazerMouse.Ui;

namespace RazerMouse.App
{
    internal static class HoverZoneCommand
    {
        public static int Run(string value)
        {
            Theme.InitDpi();
            Console.WriteLine(HoverZone.Describe());

            if (!string.IsNullOrEmpty(value))
            {
                int x, y;
                if (!TryParse(value, out x, out y))
                {
                    Console.WriteLine("坐标格式应为 x,y");
                    return 1;
                }

                Console.WriteLine(Explain(new Point(x, y)));
                return 0;
            }

            Screen screen = Screen.PrimaryScreen;
            Rectangle bounds = screen.Bounds;
            Rectangle bar = HoverZone.TaskbarRect(screen);
            int barTop = bar.IsEmpty ? bounds.Bottom - Theme.Z(40) : bar.Top + bar.Height / 2;

            Console.WriteLine();
            Console.WriteLine("典型位置的判定：");
            Console.WriteLine("  " + Explain(new Point(bounds.Width / 2, bounds.Height / 2)));
            Console.WriteLine("  " + Explain(new Point(bounds.Right - Theme.Z(220), barTop)));
            Console.WriteLine("  " + Explain(new Point(bounds.Right - Theme.Z(240), barTop - Theme.Z(240))));
            Console.WriteLine("  " + Explain(new Point(Theme.Z(40), Theme.Z(40))));
            return 0;
        }

        private static string Explain(Point point)
        {
            bool inside = HoverZone.Contains(point);
            return "(" + point.X + "," + point.Y + ") → " + (inside ? "托盘附近，会显示气泡" : "不在托盘附近，不显示气泡");
        }

        private static bool TryParse(string text, out int x, out int y)
        {
            x = 0;
            y = 0;
            string[] parts = text.Split(',', 'x', 'X', ' ');
            if (parts.Length < 2) return false;
            return int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y);
        }
    }
}
