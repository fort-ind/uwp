using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

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
        private const string ModerateGlyph = "\uE8F8";
        private const string MuteUserGlyph = "\uE74F";
        private const string UnmuteUserGlyph = "\uE767";
        private const string BlockGlyph = "\uECE4";

        private static readonly TimeSpan ShareRequestWindow = TimeSpan.FromSeconds(10);

        public static MenuFlyout Build(object item, UIElement announcer)
        {
            return Build(SocialMenuSink.ForMenu(), item, announcer) as MenuFlyout;
        }

        public static FlyoutBase BuildContext(object item, UIElement announcer)
        {
            return Build(SocialMenuSink.ForCommandBar(), item, announcer);
        }

        private static FlyoutBase Build(SocialMenuSink flyout, object item, UIElement announcer)
        {
            string url = null;
            string shareTitle = null;
            SocialUser author = null;
            SocialUser renoter = null;

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

            var linkItems = flyout.SecondaryCount;
            foreach (var person in new[] { author, renoter }.Where(person => person != null && !string.IsNullOrEmpty(person.Id)))
            {
                if (linkItems > 0 && flyout.SecondaryCount == linkItems) flyout.AddSeparator();
                flyout.Add(LocalizedStrings.Format("SocialMenuViewUserFormat", person.Handle), PersonGlyph, person, ViewUserItem_Click, false);
                if (SocialModerationService.CanModerate(person)) AddModerationSubmenu(flyout, person, announcer);
            }

            if (note != null) AddOwnerItems(flyout, note, announcer);

            flyout.TrimSeparators();
            return flyout.IsEmpty ? null : flyout.Flyout;
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

        private static void AddNoteStateItems(SocialMenuSink flyout, SocialNoteItem note, UIElement owner)
        {
            if (!SocialNoteActionService.CanAct(note)) return;

            var noteId = note.Note.Id;
            var state = SocialNoteService.CachedState(noteId);

            var favorite = Add(flyout, "SocialMenuFavorite", FavoriteGlyph, () =>
            {
                var current = SocialNoteService.CachedState(noteId);
                return SocialNoteActionService.SetFavoriteAsync(owner, note, current == null || !current.IsFavorited);
            }, true);

            var mute = Add(flyout, "SocialMenuMuteThread", MuteGlyph, () =>
            {
                var current = SocialNoteService.CachedState(noteId);
                return SocialNoteActionService.SetThreadMutedAsync(owner, note, current == null || !current.IsMutedThread);
            }, false);

            flyout.AddSeparator();

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

        private static async void LoadStateItems(string noteId, SocialMenuEntry favorite, SocialMenuEntry mute)
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

        private static void LabelStateItems(SocialMenuEntry favorite, SocialMenuEntry mute, SocialNoteState state)
        {
            favorite.Text = LocalizedStrings.Get(state.IsFavorited ? "SocialMenuUnfavorite" : "SocialMenuFavorite");
            favorite.Glyph = state.IsFavorited ? UnfavoriteGlyph : FavoriteGlyph;
            mute.Text = LocalizedStrings.Get(state.IsMutedThread ? "SocialMenuUnmuteThread" : "SocialMenuMuteThread");
        }

        private static void AddOwnerItems(SocialMenuSink flyout, SocialNoteItem note, UIElement owner)
        {
            if (!SocialNoteActionService.CanAct(note)) return;

            flyout.AddSeparator();

            if (!note.IsMine)
            {
                Add(flyout, "SocialMenuReport", ReportGlyph, () => SocialNoteActionService.ReportAsync(owner, note), false);
                return;
            }

            var noteId = note.Note.Id;
            if (string.IsNullOrEmpty(note.Note.User == null ? null : note.Note.User.Host))
            {
                Add(flyout, "SocialMenuEdit", EditGlyph, () => SocialWindows.ShowComposeAsync(owner, SocialComposeMode.Edit, note.Note), false);
            }

            var pinned = SocialNoteService.IsPinned(noteId);
            var pin = Add(flyout, "SocialMenuPin", PinGlyph, () =>
            {
                var current = SocialNoteService.IsPinned(noteId);
                return SocialNoteActionService.SetPinnedAsync(owner, note, current != true);
            }, false);

            if (pinned.HasValue)
            {
                LabelPinItem(pin, pinned.Value);
            }
            else
            {
                pin.IsEnabled = false;
                LoadPinItem(noteId, pin);
            }

            Add(flyout, "SocialMenuDelete", DeleteGlyph, () => SocialNoteActionService.DeleteAsync(owner, note), false);
            Add(flyout, "SocialMenuRedraft", RedraftGlyph, () => SocialNoteActionService.RedraftAsync(owner, note), false);
        }

        private static async void LoadPinItem(string noteId, SocialMenuEntry pin)
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

        private static void LabelPinItem(SocialMenuEntry pin, bool pinned)
        {
            pin.Text = LocalizedStrings.Get(pinned ? "SocialMenuUnpin" : "SocialMenuPin");
            pin.Glyph = pinned ? UnpinGlyph : PinGlyph;
        }

        public static MenuFlyout BuildForUser(SocialUser user, string fallbackUrl, UIElement announcer)
        {
            var url = SocialLinks.UserUrl(user) ?? fallbackUrl;
            var title = user == null ? null : SocialNoteItem.DisplayNameOf(user);

            var sink = SocialMenuSink.ForMenu();
            AddLinkItems(sink, url, title, announcer);

            var flyout = (MenuFlyout)sink.Flyout;
            if (SocialModerationService.CanModerate(user))
            {
                if (flyout.Items.Count > 0) flyout.Items.Add(new MenuFlyoutSeparator());
                AddModerationItems(flyout.Items, user, announcer);
            }

            return flyout.Items.Count > 0 ? flyout : null;
        }

        private static void AddModerationSubmenu(SocialMenuSink flyout, SocialUser user, UIElement owner)
        {
            flyout.AddSubmenu(LocalizedStrings.Format("SocialMenuModerateUserFormat", user.Handle), ModerateGlyph,
                              items => AddModerationItems(items, user, owner));
        }

        private static void AddModerationItems(IList<MenuFlyoutItemBase> items, SocialUser user, UIElement owner)
        {
            var mute = new MenuFlyoutSubItem
            {
                Text = LocalizedStrings.Get("SocialMenuMuteUser"),
                Icon = new FontIcon { Glyph = MuteUserGlyph }
            };
            mute.Items.Add(DurationItem("SocialMenuMuteForHour", () => SocialModerationService.MuteAsync(owner, user, TimeSpan.FromHours(1))));
            mute.Items.Add(DurationItem("SocialMenuMuteForDay", () => SocialModerationService.MuteAsync(owner, user, TimeSpan.FromDays(1))));
            mute.Items.Add(DurationItem("SocialMenuMuteForWeek", () => SocialModerationService.MuteAsync(owner, user, TimeSpan.FromDays(7))));
            mute.Items.Add(DurationItem("SocialMenuMuteIndefinitely", () => SocialModerationService.MuteAsync(owner, user, null)));

            var unmute = MenuItem("SocialMenuUnmuteUser", UnmuteUserGlyph, () => SocialModerationService.UnmuteAsync(owner, user));

            var renotes = new ToggleMenuFlyoutItem
            {
                Text = LocalizedStrings.Get("SocialMenuHideRenotes"),
                Icon = new FontIcon { Glyph = RenoteGlyph }
            };
            renotes.Tag = new Func<Task>(() => SocialModerationService.SetRenotesHiddenAsync(owner, user, renotes.IsChecked));
            renotes.Click += MenuItem_Click;

            var block = MenuItem("SocialMenuBlock", BlockGlyph, () => SocialModerationService.BlockAsync(owner, user));
            var unblock = MenuItem("SocialMenuUnblock", BlockGlyph, () => SocialModerationService.UnblockAsync(owner, user));

            items.Add(mute);
            items.Add(unmute);
            items.Add(renotes);
            items.Add(new MenuFlyoutSeparator());
            items.Add(block);
            items.Add(unblock);

            var moderation = new ModerationItems(mute, unmute, renotes, block, unblock);
            var relation = SocialModerationService.CachedRelation(user.Id);
            moderation.Label(relation);
            moderation.SetEnabled(relation != null);
            LoadModerationItems(user.Id, moderation);
        }

        private static async void LoadModerationItems(string userId, ModerationItems moderation)
        {
            try
            {
                var relation = await SocialModerationService.GetRelationAsync(userId);
                if (relation != null) moderation.Label(relation);
                moderation.SetEnabled(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialMenus: could not read the relation - {ex.Message}");
            }
        }

        private static MenuFlyoutItem DurationItem(string textKey, Func<Task> action)
        {
            var menuItem = new MenuFlyoutItem
            {
                Text = LocalizedStrings.Get(textKey),
                Tag = action
            };
            menuItem.Click += MenuItem_Click;
            return menuItem;
        }

        private sealed class ModerationItems
        {
            private readonly MenuFlyoutSubItem _mute;

            private readonly MenuFlyoutItem _unmute;

            private readonly ToggleMenuFlyoutItem _renotes;

            private readonly MenuFlyoutItem _block;

            private readonly MenuFlyoutItem _unblock;

            public ModerationItems(MenuFlyoutSubItem mute, MenuFlyoutItem unmute, ToggleMenuFlyoutItem renotes,
                                   MenuFlyoutItem block, MenuFlyoutItem unblock)
            {
                _mute = mute;
                _unmute = unmute;
                _renotes = renotes;
                _block = block;
                _unblock = unblock;
            }

            public void Label(SocialRelation relation)
            {
                var muted = relation != null && relation.IsMuted;
                var blocking = relation != null && relation.IsBlocking;

                _mute.Visibility = muted ? Visibility.Collapsed : Visibility.Visible;
                _unmute.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
                _renotes.IsChecked = relation != null && relation.IsRenoteMuted;
                _block.Visibility = blocking ? Visibility.Collapsed : Visibility.Visible;
                _unblock.Visibility = blocking ? Visibility.Visible : Visibility.Collapsed;
            }

            public void SetEnabled(bool enabled)
            {
                _mute.IsEnabled = enabled;
                _unmute.IsEnabled = enabled;
                _renotes.IsEnabled = enabled;
                _block.IsEnabled = enabled;
                _unblock.IsEnabled = enabled;
            }
        }

        private static void AddLinkItems(SocialMenuSink flyout, string url, string shareTitle, UIElement announcer)
        {
            var link = WebLauncher.TryCreateWebUri(url);
            if (link == null) return;

            Add(flyout, "SocialMenuOpenOnFortSocial", OpenGlyph, async () => await WebLauncher.LaunchAsync(link.AbsoluteUri), false);
            Add(flyout, "SocialMenuCopyLink", CopyGlyph, () =>
            {
                CopyLink(link, announcer);
                return Task.CompletedTask;
            }, true);

            if (DataTransferManager.IsSupported())
            {
                var title = string.IsNullOrWhiteSpace(shareTitle) ? link.AbsoluteUri : shareTitle;
                Add(flyout, "SocialMenuShare", ShareGlyph, () =>
                {
                    Share(title, link);
                    return Task.CompletedTask;
                }, true);
            }
        }

        public static void ShowContextAt(FlyoutBase flyout, FrameworkElement target, Point? point)
        {
            if (flyout == null || target == null) return;

            var options = new FlyoutShowOptions { ShowMode = FlyoutShowMode.Standard };
            if (point.HasValue) options.Position = point.Value;
            flyout.ShowAt(target, options);
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

        private static SocialMenuEntry Add(SocialMenuSink flyout, string textKey, string glyph, Func<Task> action, bool primary)
        {
            return flyout.Add(LocalizedStrings.Get(textKey), glyph, action, MenuItem_Click, primary);
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
