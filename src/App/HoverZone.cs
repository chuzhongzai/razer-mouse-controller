// 判断一个屏幕坐标是不是"托盘附近"：任务栏那一条，或者「隐藏的图标」浮层（贴在底部托盘上方、右边对齐）。
// 悬停气泡靠它挡掉外壳偶尔乱发的 MouseMove —— 指针明明在屏幕中间，气泡不该冒出来。
using System;
using System.Drawing;
using System.Windows.Forms;
using RazerMouse.Ui;

namespace RazerMouse.App
{
    internal static class HoverZone
    {
        /// <summary>任务栏所在的矩形（屏幕范围与可用工作区之间差出来的那条边）。自动隐藏时返回空。</summary>
        public static Rectangle TaskbarRect(Screen screen)
        {
            Rectangle bounds = screen.Bounds;
            Rectangle work = screen.WorkingArea;

            if (work.Bottom < bounds.Bottom)
            {
                return new Rectangle(bounds.Left, work.Bottom, bounds.Width, bounds.Bottom - work.Bottom);
            }

            if (work.Top > bounds.Top)
            {
                return new Rectangle(bounds.Left, bounds.Top, bounds.Width, work.Top - bounds.Top);
            }

            if (work.Left > bounds.Left)
            {
                return new Rectangle(bounds.Left, bounds.Top, work.Left - bounds.Left, bounds.Height);
            }

            if (work.Right < bounds.Right)
            {
                return new Rectangle(work.Right, bounds.Top, bounds.Right - work.Right, bounds.Height);
            }

            return Rectangle.Empty;
        }

        public static bool Contains(Point point)
        {
            Screen screen = Screen.FromPoint(point);
            Rectangle bounds = screen.Bounds;
            Rectangle bar = TaskbarRect(screen);

            if (bar.IsEmpty)
            {
                // 任务栏设成了自动隐藏：它盖在屏幕最下面，图标也就在最下面那一条里
                Rectangle bottom = new Rectangle(bounds.Left, bounds.Bottom - Theme.Z(160),
                                                 bounds.Width, Theme.Z(160));
                return bottom.Contains(point);
            }

            Rectangle zone = bar;
            zone.Inflate(Theme.Z(10), Theme.Z(10));
            if (zone.Contains(point)) return true;

            if (screen.WorkingArea.Bottom < bounds.Bottom)
            {
                // 「隐藏的图标」浮层：贴在底部任务栏上方、右边对齐
                Rectangle flyout = new Rectangle(bounds.Right - Theme.Z(800), bar.Top - Theme.Z(800),
                                                 Theme.Z(800), Theme.Z(800));
                return flyout.Contains(point);
            }

            return false;
        }

        /// <summary>给诊断命令用的说明文字。</summary>
        public static string Describe()
        {
            Screen screen = Screen.PrimaryScreen;
            Rectangle bar = TaskbarRect(screen);
            string text = "屏幕=" + screen.Bounds.Width + "x" + screen.Bounds.Height
                        + " 工作区=" + screen.WorkingArea.Width + "x" + screen.WorkingArea.Height
                        + " DPI缩放=" + Theme.DpiScale.ToString("0.##");
            text += bar.IsEmpty
                ? " 任务栏=自动隐藏(取屏幕底部 160 像素)"
                : " 任务栏=" + bar.Left + "," + bar.Top + "," + bar.Width + "x" + bar.Height;
            return text;
        }
    }
}
