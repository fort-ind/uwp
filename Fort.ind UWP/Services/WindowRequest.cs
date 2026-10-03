using System;
using Windows.Foundation;

namespace Fort.ind_UWP
{
    public sealed class WindowRequest
    {
        public WindowRequest(string key, string title, string header, Type pageType, object parameter,
                             Size? preferredSize, bool accountScoped)
        {
            Key = key;
            Title = title;
            Header = header;
            PageType = pageType;
            Parameter = parameter;
            PreferredSize = preferredSize;
            AccountScoped = accountScoped;
        }

        public string Key { get; private set; }

        public string Title { get; private set; }

        public string Header { get; private set; }

        public Type PageType { get; private set; }

        public object Parameter { get; private set; }

        public Size? PreferredSize { get; private set; }

        public bool AccountScoped { get; private set; }
    }
}
