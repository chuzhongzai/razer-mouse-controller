// 悬停提示：只有文字的小气泡（没有电量条、没有按钮），出现和消失都是即时的。
using System;
using System.Drawing;
using System.Windows.Forms;

namespace RazerMouse.Ui
{
    internal sealed class TextTip : DarkForm
    {
        private const int PadDip = 10;      // 内边距（96 DPI 设计值）
        private const int LineGapDip = 2;   // 行间距
        private const int OffsetDip = 12;   // 与光标/图标之间的距离

        private string[] _lines = new string[0];

        public TextTip()
        {
            Radius = Theme.Z(8);
            DrawBorder = true;
            BorderColor = Theme.Border;
            BackColor = Theme.Card;
            TopMost = true;
        }

        /// <summary>置顶但不抢焦点：气泡出现时不该把面板或别的窗口踢到后面。</summary>
        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000008;   // WS_EX_TOPMOST
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW（不进 Alt+Tab）
                return cp;
            }
        }

        /// <summary>换文字，并按文字多少重算气泡大小。第一行是标题（粗、亮），其余是细节（灰）。</summary>
        public void Update(params string[] lines)
        {
            _lines = lines ?? new string[0];

            int widest = 0;
            foreach (string line in _lines)
            {
                Size size = TextRenderer.MeasureText(line, Theme.Body);
                if (size.Width > widest) widest = size.Width;
            }

            int pad = Theme.Z(PadDip);
            int gap = Theme.Z(LineGapDip);
            int lineHeight = Theme.Body.Height;
            int count = Math.Max(1, _lines.Length);

            Size = new Size(
                widest + pad * 2,
                pad * 2 + count * lineHeight + (count - 1) * gap);

            Invalidate();
        }

        /// <summary>弹在光标上方（上方放不下就放下方），水平居中并夹在工作区内。</summary>
        public void ShowAt(Point cursor)
        {
            Screen screen = Screen.FromPoint(cursor);
            Rectangle work = screen.WorkingArea;
            int gap = Theme.Z(OffsetDip);

            int x = cursor.X - Width / 2;
            int y = cursor.Y - Height - gap;
            if (y < work.Top) y = cursor.Y + gap;
            x = Math.Max(work.Left + Theme.Z(4), Math.Min(x, work.Right - Width - Theme.Z(4)));

            Location = new Point(x, y);
            if (!Visible) Show();
            else Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Setup(e.Graphics);
            Theme.Fill(e.Graphics, new Rectangle(0, 0, Width, Height), Radius, Theme.Card);
            base.OnPaint(e);   // 描边

            int pad = Theme.Z(PadDip);
            int gap = Theme.Z(LineGapDip);
            int lineHeight = Theme.Body.Height;
            int y = pad;

            for (int i = 0; i < _lines.Length; i++)
            {
                Font font = i == 0 ? Theme.BodyBold : Theme.Body;
                Color color = i == 0 ? Theme.Text : Theme.Muted;
                var bounds = new Rectangle(pad, y, Width - pad * 2, lineHeight);
                Theme.DrawText(e.Graphics, _lines[i], font, color, bounds,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                y += lineHeight + gap;
            }
        }
    }
}
