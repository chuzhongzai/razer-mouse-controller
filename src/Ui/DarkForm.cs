// 无边框深色窗体的公共外壳：圆角裁切 + 描边。面板/悬停卡/输入框都继承它。
using System;
using System.Drawing;
using System.Windows.Forms;

namespace RazerMouse.Ui
{
    internal class DarkForm : Form
    {
        public DarkForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;   // 尺寸全部由 Theme.Z() 自己按 DPI 换算，不要让 WinForms 再自动缩放一次
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            DoubleBuffered = true;
            KeyPreview = true;
            Radius = 12;
        }

        /// <summary>圆角半径，0 表示直角。</summary>
        public int Radius { get; set; }

        /// <summary>是否画一圈 1px 边框。</summary>
        public bool DrawBorder { get; set; }

        public Color BorderColor { get; set; }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRegion();
        }

        private void ApplyRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            if (Radius <= 0) { Region = null; return; }
            using (var path = Theme.Rounded(new Rectangle(0, 0, Width, Height), Radius))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!DrawBorder) return;
            Theme.Setup(e.Graphics);
            Color color = BorderColor.IsEmpty ? Theme.Border : BorderColor;
            Theme.Stroke(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Radius, color, Theme.Zf(1f));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                OnEscape();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected virtual void OnEscape()
        {
            Hide();
        }
    }
}
