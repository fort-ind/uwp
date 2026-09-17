using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed class ExpanderHeaderButton : Button
    {
        public static readonly DependencyProperty IsExpandedProperty =
            DependencyProperty.Register(
                "IsExpanded",
                typeof(bool),
                typeof(ExpanderHeaderButton),
                new PropertyMetadata(false, OnIsExpandedChanged));

        public bool IsExpanded
        {
            get { return (bool)GetValue(IsExpandedProperty); }
            set { SetValue(IsExpandedProperty, value); }
        }

        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new ExpanderHeaderButtonAutomationPeer(this);
        }

        private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var button = d as ExpanderHeaderButton;
            if (button == null) return;

            var peer = FrameworkElementAutomationPeer.FromElement(button) as ExpanderHeaderButtonAutomationPeer;
            if (peer == null) return;

            peer.RaiseExpandCollapseStateChanged((bool)e.OldValue, (bool)e.NewValue);
        }
    }

    public sealed class ExpanderHeaderButtonAutomationPeer : ButtonAutomationPeer, IExpandCollapseProvider
    {
        public ExpanderHeaderButtonAutomationPeer(ExpanderHeaderButton owner)
            : base(owner)
        {
        }

        public ExpandCollapseState ExpandCollapseState
        {
            get
            {
                var owner = Owner as ExpanderHeaderButton;
                return owner != null && owner.IsExpanded
                       ? ExpandCollapseState.Expanded
                       : ExpandCollapseState.Collapsed;
            }
        }

        public void Expand()
        {
            var owner = Owner as ExpanderHeaderButton;
            if (owner == null || owner.IsExpanded) return;

            Invoke();
        }

        public void Collapse()
        {
            var owner = Owner as ExpanderHeaderButton;
            if (owner == null || !owner.IsExpanded) return;

            Invoke();
        }

        protected override object GetPatternCore(PatternInterface patternInterface)
        {
            if (patternInterface == PatternInterface.ExpandCollapse) return this;
            return base.GetPatternCore(patternInterface);
        }

        internal void RaiseExpandCollapseStateChanged(bool oldValue, bool newValue)
        {
            RaisePropertyChangedEvent(
                ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                oldValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
                newValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
        }
    }
}
