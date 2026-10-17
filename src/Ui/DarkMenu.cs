// 右键菜单的深色配色，让托盘菜单和面板观感一致。
using System.Drawing;
using System.Windows.Forms;

namespace RazerMouse.Ui
{
    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Accent; } }
        public override Color MenuItemSelected { get { return Theme.CardHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.CardHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.CardHover; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.CardPressed; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.CardPressed; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
        public override Color CheckBackground { get { return Theme.CardHover; } }
        public override Color CheckSelectedBackground { get { return Theme.CardPressed; } }
        public override Color CheckPressedBackground { get { return Theme.CardPressed; } }
    }
}
