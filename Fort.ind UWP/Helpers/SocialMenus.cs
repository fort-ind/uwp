using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public static class SocialMenus
    {
        private const string OpenGlyph = "\uE774";
        private const string CopyGlyph = "\uE8C8";
        private const string ShareGlyph = "\uE72D";
        private const string PersonGlyph = "\uE77B";
        private const string FavoriteGlyph = "\uE734";
        private const string UnfavoriteGlyph = "\uE735";
        private const string MuteGlyph = "\uE7ED";
        private const string PinGlyph = "\uE718";
        private const string UnpinGlyph = "\uE77A";
        private const string DeleteGlyph = "\uE74D";
        private const string ReportGlyph = "\uE7BA";
        private const string RenoteGlyph = "\uE8EE";
        private const string UndoGlyph = "\uE7A7";
        private const string PublicGlyph = "\uE774";
        private const string HomeGlyph = "\uE80F";
        private const string FollowersGlyph = "\uE72E";
        private const string EditGlyph = "\uE70F";
        private const string RedraftGlyph = "\uE777";
        private const string QuoteGlyph = "\uE9B2";

        private static readonly TimeSpan ShareRequestWindow = TimeSpan.FromSeconds(10);

        public static MenuFlyout Build(object item, UIElement announcer)
        {
            string url = null;
            string shareTitle = null;
            SocialUser author = null;
            SocialUser renoter = null;

            var flyout = new MenuFlyout();

            var note = item as SocialNoteItem;
            if (note != null)
            {
                url = note.NoteUrl;
                shareTitle = LocalizedStrings.Format("SocialShareNoteTitleFormat", note.AuthorName);
                author = note.Author;
                renoter = note.Renoter;
                AddNoteStateItems(flyout, note, announcer);
            }

            var feedItem = item as SocialFeedItem;
            if (feedItem != null)
            {
                url = feedItem.TargetUrl;
                shareTitle = feedItem.Title;
                author = feedItem.HasActorProfile ? feedItem.Actor : null;
            }

            var userItem = item as SocialUserItem;
            if (userItem != null)
            {
                url = SocialLinks.UserUrl(userItem.User);
                shareTitle = userItem.DisplayName;
            }

            AddLinkItems(flyout, url, shareTitle, announcer);

            if (userItem != null) author = userItem.User;

            if (renoter != null && author != null && string.Equals(renoter.Id, author.Id, StringComparison.Ordinal))
            {
                renoter = null;
            }

            var linkItems = flyout.Items.Count;
            foreach (var person in new[] { author, renoter })
            {
                if (person == null || string.IsNullOrEmpty(person.Id)) continue;

                if (linkItems > 0 && flyout.Items.Count == linkItems) flyout.Items.Add(new MenuFlyoutSeparator());
                flyout.Items.Add(ViewUserItem(person));
            }

            if (note != null) AddOwnerItems(flyout, note, announcer);

            RemoveTrailingSeparators(flyout);
            return flyout.Items.Count > 0 ? flyout : null;
        }

        public static MenuFlyout BuildRenote(SocialNoteItem note, UIElement owner)
        {
            if (!SocialNoteActionService.CanRenote(note)) return null;

            var flyout = new MenuFlyout();

            var renote = new MenuFlyoutSubItem
            {
                Text = LocalizedStrings.Get("SocialRenoteMenuRenote"),
                Icon = new FontIcon { Glyph = RenoteGlyph }
            };
            renote.Items.Add(RenoteVisibilityItem(note, owner, SocialNoteActionService.PublicVisibility, "SocialRenoteMenuPublic", PublicGlyph));
            renote.Items.Add(RenoteVisibilityItem(note, owner, SocialNoteActionService.HomeVisibility, "SocialRenoteMenuHome", HomeGlyph));
            renote.Items.Add(RenoteVisibilityItem(note, owner, SocialNoteActionService.FollowersVisibility, "SocialRenoteMenuFollowers", FollowersGlyph));
            flyout.Items.Add(renote);
            flyout.Items.Add(MenuItem("SocialRenoteMenuQuote", QuoteGlyph, () => SocialWindows.ShowComposeAsync(owner, SocialComposeMode.Quote, note.Note)));

            var separator = new MenuFlyoutSeparator();
            var undo = MenuItem("SocialRenoteMenuUndo", UndoGlyph, () => SocialNoteActionService.UndoRenoteAsync(owner, note));
            var known = note.IsRenotedByMe ? true : SocialNoteService.KnownRenoted(note.Note.Id);
            separator.Visibility = known == true ? Visibility.Visible : Visibility.Collapsed;
            undo.Visibility = separator.Visibility;
            flyout.Items.Add(separator);
            flyout.Items.Add(undo);

            if (!known.HasValue) RevealUndo(note, separator, undo);

            return flyout;
        }

        private static MenuFlyoutItem RenoteVisibilityItem(SocialNoteItem note, UIElement owner, string visibility, string textKey, string glyph)
        {
            var item = MenuItem(textKey, glyph, () => SocialNoteActionService.RenoteAsync(owner, note, visibility));
            item.IsEnabled = SocialNoteActionService.CanRenoteAs(note, visibility);
            return item;
        }

        private static async void RevealUndo(SocialNoteItem note, MenuFlyoutSeparator separator, MenuFlyoutItem undo)
        {
            try
            {
                var renoted = await SocialNoteActionService.CheckRenotedAsync(note);
                var visibility = renoted == true ? Visibility.Visible : Visibility.Collapsed;
                separator.Visibility = visibility;
                undo.Visibility = visibility;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialMenus: could not check the renote - {ex.Message}");
            }
        }

        private static void AddNoteStateItems(MenuFlyout flyout, SocialNoteItem note, UIElement owner)
        {
            if (!SocialNoteActionService.CanAct(note)) return;

            var noteId = note.Note.Id;
            var state = SocialNoteService.CachedState(noteId);

            var favorite = MenuItem("SocialMenuFavorite", FavoriteGlyph, () =>
            {
                var current = SocialNoteService.CachedState(noteId);
                return SocialNoteActionService.SetFavoriteAsync(owner, note, current == null || !current.IsFavorited);
            });

            var mute = MenuItem("SocialMenuMuteThread", MuteGlyph, () =>
            {
                var current = SocialNoteService.CachedState(noteId);
                return SocialNoteActionService.SetThreadMutedAsync(owner, note, current == null || !current.IsMutedThread);
            });

            flyout.Items.Add(favorite);
            flyout.Items.Add(mute);
            flyout.Items.Add(new MenuFlyoutSeparator());

            if (state != null)
            {
                LabelStateItems(favorite, mute, state);
            }
            else
            {
                favorite.IsEnabled = false;
                mute.IsEnabled = false;
                LoadStateItems(noteId, favorite, mute);
            }
        }

        private static async void LoadStateItems(string noteId, MenuFlyoutItem favorite, MenuFlyoutItem mute)
        {
            try
            {
                var state = await SocialNoteActionService.GetStateAsync(noteId);
                if (state != null) LabelStateItems(favorite, mute, state);

                favorite.IsEnabled = true;
                mute.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialMenus: could not read the note's state - {ex.Message}");
            }
        }

        private static void LabelStateItems(MenuFlyoutItem favorite, MenuFlyoutItem mute, SocialNoteState state)
        {
            favorite.Text = LocalizedStrings.Get(state.IsFavorited ? "SocialMenuUnfavorite" : "SocialMenuFavorite");
            favorite.Icon = new FontIcon { Glyph = state.IsFavorited ? UnfavoriteGlyph : FavoriteGlyph };
            mute.Text = LocalizedStrings.Get(state.IsMutedThread ? "SocialMenuUnmuteThread" : "SocialMenuMuteThread");
        }

        private static void AddOwnerItems(MenuFlyout flyout, SocialNoteItem note, UIElement owner)
        {
            if (!SocialNoteActionService.CanAct(note)) return;

            flyout.Items.Add(new MenuFlyoutSeparator());

            if (!note.IsMine)
            {
                flyout.Items.Add(MenuItem("SocialMenuReport", ReportGlyph, () => SocialNoteActionService.ReportAsync(owner, note)));
                return;
            }

            var noteId = note.Note.Id;
            if (string.IsNullOrEmpty(note.Note.User == null ? null : note.Note.User.Host))
            {
                flyout.Items.Add(MenuItem("SocialMenuEdit", EditGlyph, () => SocialWindows.ShowComposeAsync(owner, SocialComposeMode.Edit, note.Note)));
            }

            var pinned = SocialNoteService.IsPinned(noteId);
            var pin = MenuItem("SocialMenuPin", PinGlyph, () =>
            {
                var current = SocialNoteService.IsPinned(noteId);
                return SocialNoteActionService.SetPinnedAsync(owner, note, current != true);
            });
            flyout.Items.Add(pin);

            if (pinned.HasValue)
            {
                LabelPinItem(pin, pinned.Value);
            }
            else
            {
                pin.IsEnabled = false;
                LoadPinItem(noteId, pin);
            }

            flyout.Items.Add(MenuItem("SocialMenuDelete", DeleteGlyph, () => SocialNoteActionService.DeleteAsync(owner, note)));
            flyout.Items.Add(MenuItem("SocialMenuRedraft", RedraftGlyph, () => SocialNoteActionService.RedraftAsync(owner, note)));
        }

        private static async void LoadPinItem(string noteId, MenuFlyoutItem pin)
        {
            try
            {
                var pinned = await SocialNoteActionService.GetPinnedAsync(noteId);
                if (pinned.HasValue) LabelPinItem(pin, pinned.Value);
                pin.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialMenus: could not read the pinned notes - {ex.Message}");
            }
        }

        private static void LabelPinItem(MenuFlyoutItem pin, bool pinned)
        {
            pin.Text = LocalizedStrings.Get(pinned ? "SocialMenuUnpin" : "SocialMenuPin");
            pin.Icon = new FontIcon { Glyph = pinned ? UnpinGlyph : PinGlyph };
        }

        private static void RemoveTrailingSeparators(MenuFlyout flyout)
        {
            while (flyout.Items.Count > 0 && flyout.Items[flyout.Items.Count - 1] is MenuFlyoutSeparator)
            {
                flyout.Items.RemoveAt(flyout.Items.Count - 1);
            }

            while (flyout.Items.Count > 0 && flyout.Items[0] is MenuFlyoutSeparator)
            {
                flyout.Items.RemoveAt(0);
            }
        }

        public static MenuFlyout BuildForUser(SocialUser user, string fallbackUrl, UIElement announcer)
        {
            var url = SocialLinks.UserUrl(user) ?? fallbackUrl;
            var title = user == null ? null : SocialNoteItem.DisplayNameOf(user);

            var flyout = new MenuFlyout();
            AddLinkItems(flyout, url, title, announcer);

            return flyout.Items.Count > 0 ? flyout : null;
        }

        private static void AddLinkItems(MenuFlyout flyout, string url, string shareTitle, UIElement announcer)
        {
            var link = WebLauncher.TryCreateWebUri(url);
            if (link == null) return;

            flyout.Items.Add(MenuItem("SocialMenuOpenOnFortSocial", OpenGlyph, async () => await WebLauncher.LaunchAsync(link.AbsoluteUri)));
            flyout.Items.Add(MenuItem("SocialMenuCopyLink", CopyGlyph, () =>
            {
                CopyLink(link, announcer);
                return Task.CompletedTask;
            }));

            if (DataTransferManager.IsSupported())
            {
                var title = string.IsNullOrWhiteSpace(shareTitle) ? link.AbsoluteUri : shareTitle;
                flyout.Items.Add(MenuItem("SocialMenuShare", ShareGlyph, () =>
                {
                    Share(title, link);
                    return Task.CompletedTask;
                }));
            }
        }

        public static void ShowAt(MenuFlyout flyout, FrameworkElement target, Point? point)
        {
            if (flyout == null || target == null) return;

            if (point.HasValue)
            {
                flyout.ShowAt(target, point.Value);
            }
            else
            {
                flyout.ShowAt(target);
            }
        }

        private static MenuFlyoutItem ViewUserItem(SocialUser user)
        {
            var menuItem = new MenuFlyoutItem
            {
                Text = LocalizedStrings.Format("SocialMenuViewUserFormat", user.Handle),
                Icon = new FontIcon { Glyph = PersonGlyph },
                Tag = user
            };
            menuItem.Click += ViewUserItem_Click;
            return menuItem;
        }

        private static async void ViewUserItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menuItem = sender as FrameworkElement;
                var user = menuItem == null ? null : menuItem.Tag as SocialUser;
                if (user != null) await SocialWindows.ShowUserAsync(user);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialMenus: could not open the profile - {ex.Message}");
            }
        }

        private static MenuFlyoutItem MenuItem(string textKey, string glyph, Func<Task> action)
        {
            var menuItem = new MenuFlyoutItem
            {
                Text = LocalizedStrings.Get(textKey),
                Icon = new FontIcon { Glyph = glyph },
                Tag = action
            };
            menuItem.Click += MenuItem_Click;
            return menuItem;
        }

        private static async void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menuItem = sender as FrameworkElement;
                var action = menuItem == null ? null : menuItem.Tag as Func<Task>;
                if (action != null) await action();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialMenus: a menu command failed - {ex.Message}");
            }
        }

        private static void CopyLink(Uri link, UIElement announcer)
        {
            var package = new DataPackage();
            package.SetText(link.AbsoluteUri);
            package.SetWebLink(link);
            Clipboard.SetContent(package);

            AutomationHelper.AnnounceStatus(announcer, LocalizedStrings.Get("SocialLinkCopiedAnnouncement"), "SocialLinkCopied");
        }

        private static void Share(string title, Uri link)
        {
            var manager = DataTransferManager.GetForCurrentView();
            var requestedAt = DateTimeOffset.UtcNow;

            TypedEventHandler<DataTransferManager, DataRequestedEventArgs> handler = null;
            handler = (sender, args) =>
            {
                try
                {
                    sender.DataRequested -= handler;
                    if (DateTimeOffset.UtcNow - requestedAt > ShareRequestWindow) return;

                    var data = args.Request.Data;
                    data.Properties.Title = title;
                    data.SetWebLink(link);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SocialMenus: filling the share request failed - {ex.Message}");
                }
            };

            manager.DataRequested += handler;
            try
            {
                DataTransferManager.ShowShareUI();
            }
            catch (Exception ex)
            {
                manager.DataRequested -= handler;
                Debug.WriteLine($"SocialMenus: could not show the share UI - {ex.Message}");
            }
        }
    }
}
