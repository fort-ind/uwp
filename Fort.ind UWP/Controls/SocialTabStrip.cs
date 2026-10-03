using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Fort.ind_UWP
{
    public sealed class SocialTabStrip : StackPanel
    {
        public SocialTabStrip()
        {
            Orientation = Orientation.Horizontal;
            TabFocusNavigation = KeyboardNavigationMode.Once;
            XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled;
        }

        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new SocialTabStripAutomationPeer(this);
        }
    }

    public sealed class SocialTabButton : RadioButton
    {
        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new SocialTabButtonAutomationPeer(this);
        }
    }

    public sealed class SocialTabStripAutomationPeer : FrameworkElementAutomationPeer
    {
        public SocialTabStripAutomationPeer(SocialTabStrip owner)
            : base(owner)
        {
        }

        protected override AutomationControlType GetAutomationControlTypeCore()
        {
            return AutomationControlType.Tab;
        }

        protected override string GetClassNameCore()
        {
            return "SocialTabStrip";
        }
    }

    public sealed class SocialTabButtonAutomationPeer : RadioButtonAutomationPeer
    {
        public SocialTabButtonAutomationPeer(SocialTabButton owner)
            : base(owner)
        {
        }

        protected override AutomationControlType GetAutomationControlTypeCore()
        {
            return AutomationControlType.TabItem;
        }

        protected override string GetClassNameCore()
        {
            return "SocialTabButton";
        }
    }
}
