using System;
using Windows.UI.Xaml;

namespace Fort.ind_UWP
{
    internal interface IImmersiveWindowPage
    {
        bool ExtendsUnderTitleBar { get; }

        ElementTheme TitleBarTheme { get; }

        event EventHandler TitleBarChanged;

        void SetTitleBarInset(double inset);
    }
}
