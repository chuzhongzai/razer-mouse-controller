// 一个极简的深色数值输入框（改档位 DPI 用）。回车确认，Esc 取消。
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace RazerMouse.Ui
{
    internal sealed class ValueDialog : DarkForm
    {
        private readonly TextBox _input;
        private readonly Label _hint;
        private readonly int _min;
        private readonly int _max;
        private int _result;

        private ValueDialog(string title, string label, int initial, int min, int max)
        {
            _min = min;
            _max = max;
            DrawBorder = true;
            Radius = Theme.Z(10);
            ClientSize = new Size(Theme.Z(268), Theme.Z(148));
            Padding = new Padding(Theme.Z(16));

            var caption = new Label();
            caption.Text = title;
            caption.Font = Theme.Title;
            caption.ForeColor = Theme.Text;
            caption.AutoSize = false;
            caption.Bounds = new Rectangle(Theme.Z(16), Theme.Z(12), Theme.Z(236), Theme.Z(24));
            Controls.Add(caption);

            var field = new Label();
            field.Text = label;
            field.Font = Theme.Small;
            field.ForeColor = Theme.Muted;
            field.AutoSize = false;
            field.Bounds = new Rectangle(Theme.Z(16), Theme.Z(42), Theme.Z(236), Theme.Z(18));
            Controls.Add(field);

            _input = new TextBox();
            _input.Text = initial.ToString(CultureInfo.InvariantCulture);
            _input.Font = new Font(Theme.Body.FontFamily, 11f, FontStyle.Bold);
            _input.ForeColor = Theme.Text;
            _input.BackColor = Theme.Card;
            _input.BorderStyle = BorderStyle.FixedSingle;
            _input.Bounds = new Rectangle(Theme.Z(16), Theme.Z(64), Theme.Z(236), Theme.Z(28));
            _input.SelectAll();
            Controls.Add(_input);

            _hint = new Label();
            _hint.Font = Theme.Small;
            _hint.ForeColor = Theme.Muted;
            _hint.AutoSize = false;
            _hint.Bounds = new Rectangle(Theme.Z(16), Theme.Z(96), Theme.Z(236), Theme.Z(18));
            _hint.Text = string.Format("{0} – {1}", min, max);
            Controls.Add(_hint);

            var ok = new Chip();
            ok.Text = "确定";
            ok.Selected = true;
            ok.Bounds = new Rectangle(Theme.Z(148), Theme.Z(116), Theme.Z(100), Theme.Z(28));
            ok.Click += delegate { Commit(); };
            Controls.Add(ok);

            var cancel = new Chip();
            cancel.Text = "取消";
            cancel.Ghost = true;
            cancel.TextColor = Theme.Muted;
            cancel.Bounds = new Rectangle(Theme.Z(40), Theme.Z(116), Theme.Z(100), Theme.Z(28));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            AcceptButton = null;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter || keyData == Keys.Return)
            {
                Commit();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public static bool Ask(IWin32Window owner, string title, string label, int initial,
            int min, int max, out int result)
        {
            using (var dialog = new ValueDialog(title, label, initial, min, max))
            {
                bool ok = dialog.ShowDialog(owner) == DialogResult.OK;
                result = dialog._result;
                return ok;
            }
        }

        private void Commit()
        {
            int value;
            if (!int.TryParse(_input.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                || value < _min || value > _max)
            {
                _hint.ForeColor = Theme.Danger;
                _hint.Text = string.Format("请输入 {0} – {1} 之间的整数", _min, _max);
                _input.Focus();
                _input.SelectAll();
                return;
            }
            _result = value;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnEscape()
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
