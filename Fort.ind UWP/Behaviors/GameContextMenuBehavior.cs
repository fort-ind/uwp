using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Xaml.Interactivity;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Fort.ind_UWP
{
    public sealed class GameContextMenuBehavior : Behavior<FrameworkElement>
    {
        private const string PinGlyph = "";
        private const string UnpinGlyph = "";
        private const string ShareGlyph = "";

        private DataTransferManager _shareManager;

        private SharedGame _pendingShare;

        protected override void OnAttached()
        {
            base.OnAttached();

            try
            {
                AssociatedObject.ContextRequested += OnContextRequested;

                if (DataTransferManager.IsSupported())
                {
                    _shareManager = DataTransferManager.GetForCurrentView();
                    _shareManager.DataRequested += OnDataRequested;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GameContextMenuBehavior: Failed to attach - {ex.Message}");
            }
        }

        protected override void OnDetaching()
        {
            try
            {
                AssociatedObject.ContextRequested -= OnContextRequested;

                if (_shareManager != null)
                {
                    _shareManager.DataRequested -= OnDataRequested;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GameContextMenuBehavior: Failed to detach - {ex.Message}");
            }

            _shareManager = null;
            _pendingShare = null;
            base.OnDetaching();
        }

        private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            try
            {
                if (args.Handled) return;

                var target = args.OriginalSource as FrameworkElement;
                var game = GameFor(target);
                if (game == null) return;

                var flyout = BuildMenu(game);
                if (flyout == null) return;

                Point point;
                if (args.TryGetPosition(target, out point))
                {
                    flyout.ShowAt(target, point);
                }
                else
                {
                    flyout.ShowAt(target);
                }

                args.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GameContextMenuBehavior: Failed to open the game menu - {ex.Message}");
            }
        }

        private static SearchItem GameFor(FrameworkElement element)
        {
            if (element == null) return null;

            var game = element.DataContext as SearchItem;
            if (game == null)
            {
                var container = element as ContentControl;
                game = container == null ? null : container.Content as SearchItem;
            }

            if (game == null || string.IsNullOrEmpty(game.Url)) return null;
            return game;
        }

        private MenuFlyout BuildMenu(SearchItem game)
        {
            var flyout = new MenuFlyout();

            if (LabsService.PinGamesEnabled && GameTileService.CanPin(game))
            {
                var pinned = GameTileService.IsPinned(game);
                var pinItem = new MenuFlyoutItem()
                {
                    Text = LocalizedStrings.Get(pinned ? "GameUnpinFromStartMenuItem" : "GamePinToStartMenuItem"),
                    Icon = new FontIcon() { Glyph = pinned ? UnpinGlyph : PinGlyph },
                    Tag = game
                };
                pinItem.Click += pinned ? (RoutedEventHandler)UnpinMenuItem_Click : PinMenuItem_Click;
                flyout.Items.Add(pinItem);
            }

            if (LabsService.ShareGamesEnabled && _shareManager != null && WebLauncher.TryCreateWebUri(game.Url) != null)
            {
                var shareItem = new MenuFlyoutItem()
                {
                    Text = LocalizedStrings.Get("GameShareMenuItem"),
                    Icon = new FontIcon() { Glyph = ShareGlyph },
                    Tag = game
                };
                shareItem.Click += ShareMenuItem_Click;
                flyout.Items.Add(shareItem);
            }

            return flyout.Items.Count > 0 ? flyout : null;
        }

        private async void PinMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var game = GameFromMenuItem(sender);
                if (game == null) return;

                if (await GameTileService.PinAsync(game))
                {
                    Announce(LocalizedStrings.Format("GamePinnedFormat", game.Title));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GameContextMenuBehavior: Pin to Start failed - {ex.Message}");
            }
        }

        private async void UnpinMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var game = GameFromMenuItem(sender);
                if (game == null) return;

                if (await GameTileService.UnpinAsync(game))
                {
                    Announce(LocalizedStrings.Format("GameUnpinnedFormat", game.Title));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GameContextMenuBehavior: Unpin from Start failed - {ex.Message}");
            }
        }

        private void ShareMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var game = GameFromMenuItem(sender);
                if (game == null || _shareManager == null) return;

                var link = WebLauncher.TryCreateWebUri(game.Url);
                if (link == null) return;

                Interlocked.Exchange(ref _pendingShare, new SharedGame(game.Title, game.Category, link));
                DataTransferManager.ShowShareUI();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GameContextMenuBehavior: Share failed - {ex.Message}");
            }
        }

        private void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            try
            {
                var shared = Interlocked.Exchange(ref _pendingShare, null);
                if (shared == null) return;

                var data = args.Request.Data;
                data.Properties.Title = shared.Title;
                if (!string.IsNullOrEmpty(shared.Description))
                {
                    data.Properties.Description = shared.Description;
                }
                data.SetWebLink(shared.Link);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GameContextMenuBehavior: Filling the share request failed - {ex.Message}");
            }
        }

        private static SearchItem GameFromMenuItem(object sender)
        {
            var menuItem = sender as FrameworkElement;
            return menuItem == null ? null : menuItem.Tag as SearchItem;
        }

        private void Announce(string message)
        {
            AutomationHelper.AnnounceStatus(AssociatedObject, message, "GameContextMenu");
        }

        private sealed class SharedGame
        {
            public SharedGame(string title, string description, Uri link)
            {
                Title = title;
                Description = description;
                Link = link;
            }

            public string Title { get; private set; }

            public string Description { get; private set; }

            public Uri Link { get; private set; }
        }
    }
}
