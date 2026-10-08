using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using MuxcCommandBarFlyout = Microsoft.UI.Xaml.Controls.CommandBarFlyout;

namespace Fort.ind_UWP
{
    internal sealed class SocialMenuEntry
    {
        private readonly MenuFlyoutItem _item;

        private readonly AppBarButton _button;

        public SocialMenuEntry(MenuFlyoutItem item)
        {
            _item = item;
        }

        public SocialMenuEntry(AppBarButton button)
        {
            _button = button;
        }

        public string Text
        {
            set
            {
                if (_item != null) _item.Text = value;
                if (_button == null) return;

                _button.Label = value;
                ToolTipService.SetToolTip(_button, value);
            }
        }

        public string Glyph
        {
            set
            {
                var icon = string.IsNullOrEmpty(value) ? null : new FontIcon { Glyph = value };
                if (_item != null) _item.Icon = icon;
                if (_button != null) _button.Icon = icon;
            }
        }

        public bool IsEnabled
        {
            set
            {
                if (_item != null) _item.IsEnabled = value;
                if (_button != null) _button.IsEnabled = value;
            }
        }

        public Visibility Visibility
        {
            set
            {
                if (_item != null) _item.Visibility = value;
                if (_button != null) _button.Visibility = value;
            }
        }
    }

    internal abstract class SocialMenuSink
    {
        public abstract FlyoutBase Flyout { get; }

        public abstract int SecondaryCount { get; }

        public abstract bool IsEmpty { get; }

        public abstract SocialMenuEntry Add(string text, string glyph, object tag, RoutedEventHandler click, bool primary);

        public abstract void AddSubmenu(string text, string glyph, Action<IList<MenuFlyoutItemBase>> fill);

        public abstract void AddSeparator();

        public abstract void TrimSeparators();

        public static SocialMenuSink ForMenu()
        {
            return new MenuSink();
        }

        public static SocialMenuSink ForCommandBar()
        {
            return new CommandBarSink();
        }

        private sealed class MenuSink : SocialMenuSink
        {
            private readonly MenuFlyout _flyout = new MenuFlyout();

            public override FlyoutBase Flyout
            {
                get { return _flyout; }
            }

            public override int SecondaryCount
            {
                get { return _flyout.Items.Count; }
            }

            public override bool IsEmpty
            {
                get { return _flyout.Items.Count == 0; }
            }

            public override SocialMenuEntry Add(string text, string glyph, object tag, RoutedEventHandler click, bool primary)
            {
                var item = new MenuFlyoutItem { Text = text, Tag = tag };
                if (!string.IsNullOrEmpty(glyph)) item.Icon = new FontIcon { Glyph = glyph };
                item.Click += click;
                _flyout.Items.Add(item);
                return new SocialMenuEntry(item);
            }

            public override void AddSubmenu(string text, string glyph, Action<IList<MenuFlyoutItemBase>> fill)
            {
                var subItem = new MenuFlyoutSubItem { Text = text };
                if (!string.IsNullOrEmpty(glyph)) subItem.Icon = new FontIcon { Glyph = glyph };
                fill(subItem.Items);
                _flyout.Items.Add(subItem);
            }

            public override void AddSeparator()
            {
                _flyout.Items.Add(new MenuFlyoutSeparator());
            }

            public override void TrimSeparators()
            {
                Trim(_flyout.Items, item => item is MenuFlyoutSeparator);
            }
        }

        private sealed class CommandBarSink : SocialMenuSink
        {
            private readonly MuxcCommandBarFlyout _flyout = new MuxcCommandBarFlyout();

            public override FlyoutBase Flyout
            {
                get { return _flyout; }
            }

            public override int SecondaryCount
            {
                get { return _flyout.SecondaryCommands.Count; }
            }

            public override bool IsEmpty
            {
                get { return _flyout.PrimaryCommands.Count == 0 && _flyout.SecondaryCommands.Count == 0; }
            }

            public override SocialMenuEntry Add(string text, string glyph, object tag, RoutedEventHandler click, bool primary)
            {
                var button = new AppBarButton { Label = text, Tag = tag };
                if (!string.IsNullOrEmpty(glyph)) button.Icon = new FontIcon { Glyph = glyph };
                if (primary) ToolTipService.SetToolTip(button, text);
                button.Click += click;

                if (primary)
                {
                    _flyout.PrimaryCommands.Add(button);
                }
                else
                {
                    _flyout.SecondaryCommands.Add(button);
                }

                return new SocialMenuEntry(button);
            }

            public override void AddSubmenu(string text, string glyph, Action<IList<MenuFlyoutItemBase>> fill)
            {
                var menu = new MenuFlyout { Placement = FlyoutPlacementMode.RightEdgeAlignedTop };
                fill(menu.Items);
                CloseOnClick(menu.Items);

                var button = new AppBarButton { Label = text, Flyout = menu };
                if (!string.IsNullOrEmpty(glyph)) button.Icon = new FontIcon { Glyph = glyph };
                _flyout.SecondaryCommands.Add(button);
            }

            public override void AddSeparator()
            {
                _flyout.SecondaryCommands.Add(new AppBarSeparator());
            }

            public override void TrimSeparators()
            {
                Trim(_flyout.SecondaryCommands, item => item is AppBarSeparator);
            }

            private void CloseOnClick(IList<MenuFlyoutItemBase> items)
            {
                foreach (var entry in items)
                {
                    var subItem = entry as MenuFlyoutSubItem;
                    if (subItem != null)
                    {
                        CloseOnClick(subItem.Items);
                        continue;
                    }

                    var item = entry as MenuFlyoutItem;
                    if (item != null) item.Click += SubmenuItem_Click;
                }
            }

            private void SubmenuItem_Click(object sender, RoutedEventArgs e)
            {
                _flyout.Hide();
            }
        }

        private static void Trim<T>(IList<T> items, Func<T, bool> isSeparator)
        {
            while (items.Count > 0 && isSeparator(items[items.Count - 1]))
            {
                items.RemoveAt(items.Count - 1);
            }

            while (items.Count > 0 && isSeparator(items[0]))
            {
                items.RemoveAt(0);
            }

            for (var i = items.Count - 1; i > 0; i--)
            {
                if (isSeparator(items[i]) && isSeparator(items[i - 1])) items.RemoveAt(i);
            }
        }
    }
}
