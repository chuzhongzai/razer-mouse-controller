// 自绘胶囊按钮：面板里所有的可点元素都用它，保证深色下的一致观感。
using System;
using System.Drawing;
using System.Windows.Forms;

namespace RazerMouse.Ui
{
    internal sealed class Chip : Control
    {
        private bool _hover;
        private bool _down;
        private bool _selected;
        private bool _ghost;

        public Chip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            BackColor = Theme.Background;
            Cursor = Cursors.Hand;
            Font = Theme.Chip;
            Radius = Theme.Z(7);
        }

        public int Radius { get; set; }

        /// <summary>选中态：绿色描边 + 绿字。</summary>
        public bool Selected
        {
            get { return _selected; }
            set { if (_selected == value) return; _selected = value; Invalidate(); }
        }

        /// <summary>幽灵按钮：平时透明，只有悬停才有底。</summary>
        public bool Ghost
        {
            get { return _ghost; }
            set { _ghost = value; Invalidate(); }
        }

        /// <summary>次要文字色（幽灵按钮用）。</summary>
        public Color TextColor { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Setup(g);

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Color back = Color.Empty;
            if (_down) back = Theme.CardPressed;
            else if (_hover) back = Theme.CardHover;
            else if (!_ghost) back = Theme.Card;

            if (back != Color.Empty)
                Theme.Fill(g, bounds, Radius, back);

            if (_selected)
                Theme.Stroke(g, bounds, Radius, Theme.Accent, Theme.Zf(1.4f));
            else if (_ghost && _hover)
                Theme.Stroke(g, bounds, Radius, Theme.Border, Theme.Zf(1f));

            Color color = TextColor;
            if (color.IsEmpty) color = _selected ? Theme.Accent : Theme.Text;
            if (!Enabled) color = Color.FromArgb(0x5C, 0x5C, 0x68);

            Theme.DrawText(g, Text, Font, color, bounds,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _down = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _down = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            Invalidate();
            base.OnTextChanged(e);
        }
    }
}
