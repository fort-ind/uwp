using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed class ShellHeaderTemplateSelector : DataTemplateSelector
    {
        public DataTemplate TitleTemplate { get; set; }

        public DataTemplate SettingsBreadcrumbTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item)
        {
            return item is SettingsCategory ? SettingsBreadcrumbTemplate : TitleTemplate;
        }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            return SelectTemplateCore(item);
        }
    }
}
