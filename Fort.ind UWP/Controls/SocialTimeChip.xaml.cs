using System;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed partial class SocialTimeChip : UserControl
    {
        public SocialTimeChip()
        {
            this.InitializeComponent();
        }

        public void Show(DateTimeOffset time)
        {
            var label = UnixTimeText.Label(time, DateTimeOffset.Now);
            TimeText.Text = label;
            AutomationProperties.SetName(this, label);
        }
    }
}
