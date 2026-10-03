using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public static class SocialNoteActionService
    {
        public const string PublicVisibility = "public";

        public const string HomeVisibility = "home";

        public const string FollowersVisibility = "followers";

        private const string NoSuchNoteCode = "NO_SUCH_NOTE";

        private const int ReportCommentLimit = 2048;

        private static readonly object s_lock = new object();

        private static readonly HashSet<string> s_busy = new HashSet<string>(StringComparer.Ordinal);

        private static readonly Dictionary<string, string> s_errorMessages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { NoSuchNoteCode, "SocialActionErrorNoSuchNote" },
            { "YOU_HAVE_BEEN_BLOCKED", "SocialActionErrorBlocked" },
            { "CANNOT_RENOTE_DUE_TO_VISIBILITY", "SocialActionErrorRenoteVisibility" },
            { "PIN_LIMIT_EXCEEDED", "SocialActionErrorPinLimit" },
            { "ALREADY_EXPIRED", "SocialActionErrorPollEnded" },
            { "CANNOT_REPORT_YOURSELF", "SocialActionErrorCannotReport" },
            { "CANNOT_REPORT_THE_ADMIN", "SocialActionErrorCannotReport" }
        };

        public static bool CanAct(SocialNoteItem item)
        {
            return item != null && item.Note != null && !item.IsDeleted && !item.IsHidden
                   && SocialContentService.CurrentAccountId() != null;
        }

        public static bool CanRenote(SocialNoteItem item)
        {
            if (!CanAct(item)) return false;

            switch (item.Note.Visibility)
            {
                case FollowersVisibility:
                    return item.IsMine;
                case "specified":
                    return false;
                default:
                    return true;
            }
        }

        public static bool CanRenoteAs(SocialNoteItem item, string visibility)
        {
            if (!CanRenote(item)) return false;

            switch (item.Note.Visibility)
            {
                case FollowersVisibility:
                    return visibility == FollowersVisibility;
                case HomeVisibility:
                    return visibility != PublicVisibility;
                default:
                    return true;
            }
        }

        public static Task ToggleLikeAsync(UIElement owner, SocialNoteItem item)
        {
            if (item == null) return Task.CompletedTask;

            return item.MyReaction != null ? UnreactAsync(owner, item) : ReactAsync(owner, item, SocialReactions.Like);
        }

        public static async Task ReactAsync(UIElement owner, SocialNoteItem item, string reaction)
        {
            var key = SocialReactions.Normalize(reaction);
            if (!CanAct(item) || key == null) return;

            if (SocialReactions.AreSame(key, item.MyReaction))
            {
                await UnreactAsync(owner, item);
                return;
            }

            if (!SocialReactions.CanReactWith(key)) return;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteReactions, SocialSignInPrompt.React)) return;

            var noteId = item.Note.Id;
            var me = SocialContentService.CurrentAccountId();
            var busy = "react:" + noteId;
            if (me == null || !TryBegin(busy)) return;

            var previous = item.MyReaction;
            try
            {
                SocialNoteService.Raise(SocialNoteChange.Reacted(noteId, me, key));

                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.ReactAsync(token, noteId, SocialReactions.ToRequestKey(key), CancellationToken.None);
                if (result.Status == SocialApiStatus.Ok)
                {
                    Announce(owner, SocialReactions.IsLike(key)
                                    ? LocalizedStrings.Get("SocialActionLikedAnnouncement")
                                    : LocalizedStrings.Format("SocialActionReactedAnnouncementFormat", SocialReactions.SpokenName(key)));
                    return;
                }

                SocialNoteService.Raise(previous == null
                                        ? SocialNoteChange.Unreacted(noteId, me, key)
                                        : SocialNoteChange.Reacted(noteId, me, previous));
                await ReportFailureAsync(owner, item, result.Status, result.ErrorCode,
                                         SocialPermissions.WriteReactions, SocialSignInPrompt.React, "SocialActionReactFailedTitle");
            }
            finally
            {
                End(busy);
            }
        }

        public static async Task UnreactAsync(UIElement owner, SocialNoteItem item)
        {
            if (!CanAct(item) || item.MyReaction == null) return;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteReactions, SocialSignInPrompt.React)) return;

            var noteId = item.Note.Id;
            var me = SocialContentService.CurrentAccountId();
            var busy = "react:" + noteId;
            if (me == null || !TryBegin(busy)) return;

            var previous = item.MyReaction;
            try
            {
                SocialNoteService.Raise(SocialNoteChange.Unreacted(noteId, me, previous));

                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.UnreactAsync(token, noteId, CancellationToken.None);
                if (result.Status == SocialApiStatus.Ok || IsCode(result.ErrorCode, "NOT_REACTED"))
                {
                    Announce(owner, LocalizedStrings.Get("SocialActionUnreactedAnnouncement"));
                    return;
                }

                SocialNoteService.Raise(SocialNoteChange.Reacted(noteId, me, previous));
                await ReportFailureAsync(owner, item, result.Status, result.ErrorCode,
                                         SocialPermissions.WriteReactions, SocialSignInPrompt.React, "SocialActionUnreactFailedTitle");
            }
            finally
            {
                End(busy);
            }
        }

        public static async Task RenoteAsync(UIElement owner, SocialNoteItem item, string visibility)
        {
            if (!CanRenoteAs(item, visibility)) return;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteNotes, SocialSignInPrompt.Renote)) return;

            var noteId = item.Note.Id;
            var me = SocialContentService.CurrentAccountId();
            var busy = "renote:" + noteId;
            if (me == null || !TryBegin(busy)) return;

            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.RenoteAsync(token, noteId, visibility, item.Note.LocalOnly, CancellationToken.None);
                if (result.Status == SocialApiStatus.Ok)
                {
                    SocialNoteService.Raise(SocialNoteChange.Renoted(noteId, me, result.Value.Id));
                    Announce(owner, LocalizedStrings.Get("SocialActionRenotedAnnouncement"));
                    return;
                }

                await ReportFailureAsync(owner, item, result.Status, result.ErrorCode,
                                         SocialPermissions.WriteNotes, SocialSignInPrompt.Renote, "SocialActionRenoteFailedTitle");
            }
            finally
            {
                End(busy);
            }
        }

        public static async Task UndoRenoteAsync(UIElement owner, SocialNoteItem item)
        {
            if (!CanAct(item)) return;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteNotes, SocialSignInPrompt.Renote)) return;

            var noteId = item.Note.Id;
            var me = SocialContentService.CurrentAccountId();
            var busy = "renote:" + noteId;
            if (me == null || !TryBegin(busy)) return;

            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.UnrenoteAsync(token, noteId, CancellationToken.None);
                if (result.Status == SocialApiStatus.Ok)
                {
                    SocialNoteService.Raise(SocialNoteChange.Unrenoted(noteId, me));
                    Announce(owner, LocalizedStrings.Get("SocialActionUnrenotedAnnouncement"));
                    return;
                }

                await ReportFailureAsync(owner, item, result.Status, result.ErrorCode,
                                         SocialPermissions.WriteNotes, SocialSignInPrompt.Renote, "SocialActionUnrenoteFailedTitle");
            }
            finally
            {
                End(busy);
            }
        }

        public static async Task<bool?> CheckRenotedAsync(SocialNoteItem item)
        {
            if (!CanAct(item)) return null;

            var noteId = item.Note.Id;
            var known = SocialNoteService.KnownRenoted(noteId);
            if (known.HasValue) return known;

            var me = SocialContentService.CurrentAccountId();
            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.GetRenotesAsync(token, noteId, me, null, 1, CancellationToken.None);
                if (result.Status != SocialApiStatus.Ok) return null;

                var renoted = result.Value.Count > 0;
                SocialNoteService.Raise(SocialNoteChange.RenoteKnown(noteId, renoted));
                return renoted;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteActionService: could not check the renote - {ex.Message}");
                return null;
            }
        }

        public static async Task<SocialNoteState> GetStateAsync(string noteId)
        {
            var cached = SocialNoteService.CachedState(noteId);
            if (cached != null) return cached;

            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.GetNoteStateAsync(token, noteId, CancellationToken.None);
                if (result.Status != SocialApiStatus.Ok) return null;

                SocialNoteService.RememberState(noteId, result.Value);
                return result.Value;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteActionService: could not read the note's state - {ex.Message}");
                return null;
            }
        }

        public static async Task<bool?> GetPinnedAsync(string noteId)
        {
            var known = SocialNoteService.IsPinned(noteId);
            if (known.HasValue) return known;

            var me = SocialContentService.CurrentAccountId();
            if (me == null) return null;

            try
            {
                var result = await SocialContentService.FetchUserAsync(me, CancellationToken.None);
                if (result.Status != SocialApiStatus.Ok) return null;

                var ids = new List<string>();
                foreach (var note in result.Value.PinnedNotes)
                {
                    ids.Add(note.Id);
                }

                SocialNoteService.RememberPinned(ids);
                return SocialNoteService.IsPinned(noteId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteActionService: could not read the pinned notes - {ex.Message}");
                return null;
            }
        }

        public static async Task SetFavoriteAsync(UIElement owner, SocialNoteItem item, bool favorite)
        {
            if (!CanAct(item)) return;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteFavorites, SocialSignInPrompt.Favorite)) return;

            await RunToggleAsync(owner, item, "favorite:", favorite,
                                 (token, noteId) => SocialApiService.FavoriteAsync(token, noteId, favorite, CancellationToken.None),
                                 favorite ? "ALREADY_FAVORITED" : "NOT_FAVORITED",
                                 SocialNoteChange.Favorite(item.Note.Id, favorite),
                                 favorite ? "SocialActionFavoritedAnnouncement" : "SocialActionUnfavoritedAnnouncement",
                                 SocialPermissions.WriteFavorites, SocialSignInPrompt.Favorite,
                                 favorite ? "SocialActionFavoriteFailedTitle" : "SocialActionUnfavoriteFailedTitle");
        }

        public static async Task SetThreadMutedAsync(UIElement owner, SocialNoteItem item, bool mute)
        {
            if (!CanAct(item)) return;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteAccount, SocialSignInPrompt.Mute)) return;

            await RunToggleAsync(owner, item, "mute:", mute,
                                 (token, noteId) => SocialApiService.MuteThreadAsync(token, noteId, mute, CancellationToken.None),
                                 null,
                                 SocialNoteChange.ThreadMute(item.Note.Id, mute),
                                 mute ? "SocialActionMutedAnnouncement" : "SocialActionUnmutedAnnouncement",
                                 SocialPermissions.WriteAccount, SocialSignInPrompt.Mute,
                                 mute ? "SocialActionMuteFailedTitle" : "SocialActionUnmuteFailedTitle");
        }

        public static async Task SetPinnedAsync(UIElement owner, SocialNoteItem item, bool pin)
        {
            if (!CanAct(item) || !item.IsMine) return;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteAccount, SocialSignInPrompt.Pin)) return;

            await RunToggleAsync(owner, item, "pin:", pin,
                                 (token, noteId) => SocialApiService.PinAsync(token, noteId, pin, CancellationToken.None),
                                 pin ? "ALREADY_PINNED" : null,
                                 SocialNoteChange.Pin(item.Note.Id, pin),
                                 pin ? "SocialActionPinnedAnnouncement" : "SocialActionUnpinnedAnnouncement",
                                 SocialPermissions.WriteAccount, SocialSignInPrompt.Pin,
                                 pin ? "SocialActionPinFailedTitle" : "SocialActionUnpinFailedTitle");
        }

        private static async Task RunToggleAsync(UIElement owner, SocialNoteItem item, string busyPrefix, bool value,
                                                 Func<string, string, Task<SocialApiResult<bool>>> request, string doneCode,
                                                 SocialNoteChange change, string announcementKey,
                                                 string permission, SocialSignInPrompt prompt, string failureTitleKey)
        {
            var noteId = item.Note.Id;
            var busy = busyPrefix + noteId;
            if (!TryBegin(busy)) return;

            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await request(token, noteId);
                if (result.Status == SocialApiStatus.Ok || IsCode(result.ErrorCode, doneCode))
                {
                    SocialNoteService.Raise(change);
                    Announce(owner, LocalizedStrings.Get(announcementKey));
                    return;
                }

                await ReportFailureAsync(owner, item, result.Status, result.ErrorCode, permission, prompt, failureTitleKey);
            }
            finally
            {
                End(busy);
            }
        }

        public static Task<bool> DeleteAsync(UIElement owner, SocialNoteItem item)
        {
            return DeleteAsync(owner, item, false);
        }

        public static async Task RedraftAsync(UIElement owner, SocialNoteItem item)
        {
            var note = item == null ? null : item.Note;
            if (await DeleteAsync(owner, item, true)) await SocialWindows.ShowComposeAsync(owner, SocialComposeMode.Redraft, note);
        }

        private static async Task<bool> DeleteAsync(UIElement owner, SocialNoteItem item, bool redraft)
        {
            if (!CanAct(item) || !item.IsMine) return false;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteNotes, SocialSignInPrompt.Delete)) return false;

            var confirmed = await DialogService.ShowConfirmAsync(owner,
                                                                 LocalizedStrings.Get(redraft ? "SocialRedraftDialogTitle" : "SocialDeleteDialogTitle"),
                                                                 LocalizedStrings.Get(redraft ? "SocialRedraftDialogBody" : "SocialDeleteDialogBody"),
                                                                 LocalizedStrings.Get(redraft ? "SocialRedraftDialogConfirm" : "SocialDeleteDialogConfirm"),
                                                                 LocalizedStrings.Get("DialogCancel"),
                                                                 ContentDialogButton.Close);
            if (!confirmed) return false;

            var noteId = item.Note.Id;
            var me = SocialContentService.CurrentAccountId();
            var busy = "delete:" + noteId;
            if (!TryBegin(busy)) return false;

            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.DeleteNoteAsync(token, noteId, CancellationToken.None);
                if (result.Status == SocialApiStatus.Ok || IsCode(result.ErrorCode, NoSuchNoteCode))
                {
                    SocialNoteService.Raise(SocialNoteChange.DeletedBy(noteId, me));
                    Announce(owner, LocalizedStrings.Get("SocialActionDeletedAnnouncement"));
                    return true;
                }

                await ReportFailureAsync(owner, item, result.Status, result.ErrorCode,
                                         SocialPermissions.WriteNotes, SocialSignInPrompt.Delete, "SocialActionDeleteFailedTitle");
                return false;
            }
            finally
            {
                End(busy);
            }
        }

        public static async Task VoteAsync(UIElement owner, SocialNoteItem item, IReadOnlyList<int> choices)
        {
            var poll = item == null ? null : item.Poll;
            if (!CanAct(item) || poll == null || choices == null || choices.Count == 0) return;
            if (poll.IsExpired(DateTimeOffset.Now)) return;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteVotes, SocialSignInPrompt.Vote)) return;

            var valid = choices.Where(choice => choice >= 0 && choice < poll.Choices.Count && !poll.Choices[choice].IsVoted)
                               .Distinct()
                               .ToList();
            if (valid.Count == 0) return;

            var confirmed = await DialogService.ShowConfirmAsync(owner,
                                                                 LocalizedStrings.Get("SocialVoteDialogTitle"),
                                                                 VoteConfirmation(poll, valid),
                                                                 LocalizedStrings.Get("SocialVoteDialogConfirm"),
                                                                 LocalizedStrings.Get("DialogCancel"),
                                                                 ContentDialogButton.Primary);
            if (!confirmed) return;

            var noteId = item.Note.Id;
            var me = SocialContentService.CurrentAccountId();
            var busy = "vote:" + noteId;
            if (me == null || !TryBegin(busy)) return;

            try
            {
                foreach (var choice in valid)
                {
                    SocialNoteService.Raise(SocialNoteChange.PollVoted(noteId, me, choice));
                }

                var token = await MisskeyAuthService.TryGetTokenAsync();
                SocialApiResult<bool> failure = null;
                foreach (var choice in valid)
                {
                    var result = await SocialApiService.VoteAsync(token, noteId, choice, CancellationToken.None);
                    if (result.Status != SocialApiStatus.Ok && !IsCode(result.ErrorCode, "ALREADY_VOTED"))
                    {
                        failure = result;
                        break;
                    }
                }

                await RefreshAsync(noteId);

                if (failure == null)
                {
                    Announce(owner, LocalizedStrings.Get("SocialActionVotedAnnouncement"));
                    return;
                }

                await ReportFailureAsync(owner, item, failure.Status, failure.ErrorCode,
                                         SocialPermissions.WriteVotes, SocialSignInPrompt.Vote, "SocialActionVoteFailedTitle");
            }
            finally
            {
                End(busy);
            }
        }

        private static string VoteConfirmation(SocialPoll poll, IReadOnlyList<int> choices)
        {
            if (choices.Count == 1)
            {
                return LocalizedStrings.Format("SocialVoteDialogBodyFormat", poll.Choices[choices[0]].Text ?? "");
            }

            var list = new StringBuilder();
            foreach (var choice in choices)
            {
                list.Append("\n• ");
                list.Append(poll.Choices[choice].Text ?? "");
            }

            return LocalizedStrings.Format("SocialVoteDialogBodyMultipleFormat", list);
        }

        public static async Task RefreshAsync(string noteId)
        {
            try
            {
                var result = await SocialContentService.FetchNoteAsync(noteId, CancellationToken.None);
                if (result.Status == SocialApiStatus.Ok) SocialNoteService.Raise(SocialNoteChange.Snapshot(result.Value));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteActionService: could not refresh the note - {ex.Message}");
            }
        }

        public static async Task ReportAsync(UIElement owner, SocialNoteItem item)
        {
            if (!CanAct(item) || item.IsMine || item.Author == null) return;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteReportAbuse, SocialSignInPrompt.Report)) return;

            var prefix = ReportPrefix(item);
            var box = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 120,
                MaxHeight = 240,
                MaxLength = Math.Max(1, ReportCommentLimit - prefix.Length),
                PlaceholderText = LocalizedStrings.Get("SocialReportDialogPlaceholder"),
                IsSpellCheckEnabled = true
            };
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(box, LocalizedStrings.Get("SocialReportDialogBoxName"));

            var body = new StackPanel { Spacing = 12 };
            body.Children.Add(new TextBlock
            {
                Text = LocalizedStrings.Get("SocialReportDialogBody"),
                TextWrapping = TextWrapping.Wrap
            });
            body.Children.Add(box);

            var dialog = new ContentDialog
            {
                Title = LocalizedStrings.Format("SocialReportDialogTitleFormat", item.Author.Handle),
                Content = body,
                PrimaryButtonText = LocalizedStrings.Get("SocialReportDialogConfirm"),
                CloseButtonText = LocalizedStrings.Get("DialogCancel"),
                DefaultButton = ContentDialogButton.Close,
                IsPrimaryButtonEnabled = false
            };
            box.TextChanged += (sender, args) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(box.Text);

            if (await DialogService.ShowAsync(owner, dialog) != ContentDialogResult.Primary) return;

            var comment = prefix + box.Text.Trim();
            var busy = "report:" + item.Note.Id;
            if (!TryBegin(busy)) return;

            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.ReportAsync(token, item.Author.Id, comment, CancellationToken.None);
                if (result.Status == SocialApiStatus.Ok)
                {
                    await DialogService.ShowMessageAsync(owner,
                                                         LocalizedStrings.Get("SocialReportSentTitle"),
                                                         LocalizedStrings.Get("SocialReportSentBody"),
                                                         LocalizedStrings.Get("DialogOk"));
                    return;
                }

                await ReportFailureAsync(owner, item, result.Status, result.ErrorCode,
                                         SocialPermissions.WriteReportAbuse, SocialSignInPrompt.Report, "SocialActionReportFailedTitle");
            }
            finally
            {
                End(busy);
            }
        }

        private static string ReportPrefix(SocialNoteItem item)
        {
            var text = new StringBuilder();
            var remote = string.IsNullOrEmpty(item.Note.RemoteUrl) ? item.Note.RemoteUri : item.Note.RemoteUrl;
            if (!string.IsNullOrEmpty(remote))
            {
                text.Append("Note: ").Append(remote).Append('\n');
            }

            text.Append("Local Note: ").Append(item.NoteUrl).Append('\n');
            text.Append("-----\n");
            return text.ToString();
        }

        private static async Task ReportFailureAsync(UIElement owner, SocialNoteItem item, SocialApiStatus status, string errorCode,
                                                     string permission, SocialSignInPrompt prompt, string titleKey)
        {
            if (status == SocialApiStatus.PermissionDenied)
            {
                await SocialPermissions.HandleDeniedAsync(owner, permission, prompt);
                return;
            }

            if (IsCode(errorCode, NoSuchNoteCode) && item != null)
            {
                SocialNoteService.Raise(SocialNoteChange.Deleted(item.Note.Id));
            }

            string messageKey;
            if (errorCode == null || !s_errorMessages.TryGetValue(errorCode, out messageKey)) messageKey = "SocialActionErrorGeneric";

            var message = messageKey == "SocialActionErrorBlocked"
                          ? LocalizedStrings.Format("SocialActionErrorBlockedFormat", item == null ? "" : item.AuthorName)
                          : LocalizedStrings.Get(messageKey);

            await DialogService.ShowMessageAsync(owner, LocalizedStrings.Get(titleKey), message, LocalizedStrings.Get("DialogOk"));
        }

        private static bool IsCode(string errorCode, string expected)
        {
            return expected != null && string.Equals(errorCode, expected, StringComparison.Ordinal);
        }

        private static void Announce(UIElement owner, string text)
        {
            if (owner == null || string.IsNullOrEmpty(text)) return;

            try
            {
                AutomationHelper.AnnounceStatus(owner, text, "SocialNoteAction");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteActionService: could not announce - {ex.Message}");
            }
        }

        private static bool TryBegin(string key)
        {
            lock (s_lock)
            {
                return s_busy.Add(key);
            }
        }

        private static void End(string key)
        {
            lock (s_lock)
            {
                s_busy.Remove(key);
            }
        }
    }
}
