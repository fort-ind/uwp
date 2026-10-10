using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public static class SocialModerationService
    {
        private const string AlreadyMutingCode = "ALREADY_MUTING";

        private const string NotMutingCode = "NOT_MUTING";

        private const string AlreadyBlockingCode = "ALREADY_BLOCKING";

        private const string NotBlockingCode = "NOT_BLOCKING";

        private static readonly object s_lock = new object();

        private static readonly Dictionary<string, SocialRelation> s_relations = new Dictionary<string, SocialRelation>(StringComparer.Ordinal);

        private static readonly Dictionary<string, DateTimeOffset> s_muteEnds = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        private static readonly HashSet<string> s_busy = new HashSet<string>(StringComparer.Ordinal);

        private static string s_accountId;

        public static bool CanModerate(SocialUser user)
        {
            if (user == null || string.IsNullOrEmpty(user.Id)) return false;

            var account = SocialContentService.CurrentAccountId();
            return account != null && !string.Equals(account, user.Id, StringComparison.Ordinal);
        }

        public static SocialRelation CachedRelation(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return null;

            lock (s_lock)
            {
                return CurrentRelationLocked(userId);
            }
        }

        public static bool IsHidden(string userId)
        {
            var relation = CachedRelation(userId);
            return relation != null && (relation.IsMuted || relation.IsBlocking);
        }

        public static bool AreRenotesHidden(string userId)
        {
            var relation = CachedRelation(userId);
            return relation != null && relation.IsRenoteMuted;
        }

        public static async Task<SocialRelation> GetRelationAsync(string userId)
        {
            if (string.IsNullOrEmpty(userId) || SocialContentService.CurrentAccountId() == null) return null;

            var token = await MisskeyAuthService.TryGetTokenAsync();
            var result = await SocialApiService.GetRelationAsync(token, userId, CancellationToken.None);
            if (result.Status != SocialApiStatus.Ok) return null;

            lock (s_lock)
            {
                ForgetIfAccountChangedLocked();

                if (!result.Value.IsMuted) s_muteEnds.Remove(userId);
                s_relations[userId] = result.Value;
            }

            return result.Value;
        }

        public static async Task MuteAsync(UIElement owner, SocialUser user, TimeSpan? duration)
        {
            if (!CanModerate(user)) return;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteMutes, SocialSignInPrompt.MuteUser)) return;

            DateTimeOffset? ends = duration.HasValue ? DateTimeOffset.UtcNow + duration.Value : (DateTimeOffset?)null;
            await RunAsync(owner, user, SocialRelationKind.Mute, true,
                           token => SocialApiService.MuteUserAsync(token, user.Id, ends, CancellationToken.None),
                           AlreadyMutingCode, SocialPermissions.WriteMutes, SocialSignInPrompt.MuteUser,
                           "SocialMutedUserAnnouncementFormat", "SocialMuteUserFailedTitleFormat", ends);
        }

        public static async Task UnmuteAsync(UIElement owner, SocialUser user)
        {
            if (!CanModerate(user)) return;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteMutes, SocialSignInPrompt.MuteUser)) return;

            await RunAsync(owner, user, SocialRelationKind.Mute, false,
                           token => SocialApiService.UnmuteUserAsync(token, user.Id, CancellationToken.None),
                           NotMutingCode, SocialPermissions.WriteMutes, SocialSignInPrompt.MuteUser,
                           "SocialUnmutedUserAnnouncementFormat", "SocialUnmuteUserFailedTitleFormat", null);
        }

        public static async Task SetRenotesHiddenAsync(UIElement owner, SocialUser user, bool hide)
        {
            if (!CanModerate(user)) return;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteMutes, SocialSignInPrompt.MuteUser)) return;

            await RunAsync(owner, user, SocialRelationKind.RenoteMute, hide,
                           token => SocialApiService.MuteRenotesAsync(token, user.Id, hide, CancellationToken.None),
                           hide ? AlreadyMutingCode : NotMutingCode, SocialPermissions.WriteMutes, SocialSignInPrompt.MuteUser,
                           hide ? "SocialRenotesHiddenAnnouncementFormat" : "SocialRenotesShownAnnouncementFormat",
                           hide ? "SocialHideRenotesFailedTitleFormat" : "SocialShowRenotesFailedTitleFormat", null);
        }

        public static async Task BlockAsync(UIElement owner, SocialUser user)
        {
            if (!CanModerate(user)) return;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteBlocks, SocialSignInPrompt.Block)) return;

            var name = SocialNoteItem.DisplayNameOf(user);
            var confirmed = await DialogService.ShowConfirmAsync(owner,
                                                                 LocalizedStrings.Format("SocialBlockDialogTitleFormat", name),
                                                                 LocalizedStrings.Get("SocialBlockDialogBody"),
                                                                 LocalizedStrings.Get("SocialBlockDialogConfirm"),
                                                                 LocalizedStrings.Get("DialogCancel"),
                                                                 ContentDialogButton.Close);
            if (!confirmed) return;

            var changed = await RunAsync(owner, user, SocialRelationKind.Block, true,
                                         token => SocialApiService.BlockAsync(token, user.Id, true, CancellationToken.None),
                                         AlreadyBlockingCode, SocialPermissions.WriteBlocks, SocialSignInPrompt.Block,
                                         "SocialBlockedAnnouncementFormat", "SocialBlockFailedTitleFormat", null);
            if (changed) await SocialFollowService.RereadAsync(user.Id);
        }

        public static async Task UnblockAsync(UIElement owner, SocialUser user)
        {
            if (!CanModerate(user)) return;
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteBlocks, SocialSignInPrompt.Block)) return;

            var changed = await RunAsync(owner, user, SocialRelationKind.Block, false,
                                         token => SocialApiService.BlockAsync(token, user.Id, false, CancellationToken.None),
                                         NotBlockingCode, SocialPermissions.WriteBlocks, SocialSignInPrompt.Block,
                                         "SocialUnblockedAnnouncementFormat", "SocialUnblockFailedTitleFormat", null);
            if (changed) await SocialFollowService.RereadAsync(user.Id);
        }

        private static async Task<bool> RunAsync(UIElement owner, SocialUser user, SocialRelationKind kind, bool on,
                                                 Func<string, Task<SocialApiResult<bool>>> request, string doneCode,
                                                 string permission, SocialSignInPrompt prompt,
                                                 string announcementKey, string failureTitleKey, DateTimeOffset? muteEnds)
        {
            var busy = (int)kind + ":" + user.Id;
            if (!TryBegin(busy)) return false;

            try
            {
                var name = SocialNoteItem.DisplayNameOf(user);
                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await request(token);
                if (result.Status == SocialApiStatus.Ok || string.Equals(result.ErrorCode, doneCode, StringComparison.Ordinal))
                {
                    Remember(user.Id, kind, on, muteEnds);
                    SocialNoteService.Raise(SocialNoteChange.RelationChanged(user.Id, kind, on));
                    Announce(owner, LocalizedStrings.Format(announcementKey, name));
                    return true;
                }

                if (result.Status == SocialApiStatus.PermissionDenied)
                {
                    await SocialPermissions.HandleDeniedAsync(owner, permission, prompt);
                    return false;
                }

                await DialogService.ShowMessageAsync(owner,
                                                     LocalizedStrings.Format(failureTitleKey, name),
                                                     LocalizedStrings.Get(SocialApiService.ConnectionMessageKey(result.Status)
                                                                          ?? "SocialActionErrorGeneric"),
                                                     LocalizedStrings.Get("DialogOk"));
                return false;
            }
            finally
            {
                End(busy);
            }
        }

        private static void Remember(string userId, SocialRelationKind kind, bool on, DateTimeOffset? muteEnds)
        {
            lock (s_lock)
            {
                ForgetIfAccountChangedLocked();

                SocialRelation relation;
                if (!s_relations.TryGetValue(userId, out relation)) relation = new SocialRelation(userId, false, false, false);

                switch (kind)
                {
                    case SocialRelationKind.Mute:
                        relation = relation.WithMuted(on);
                        if (on && muteEnds.HasValue) s_muteEnds[userId] = muteEnds.Value;
                        else s_muteEnds.Remove(userId);
                        break;
                    case SocialRelationKind.RenoteMute:
                        relation = relation.WithRenoteMuted(on);
                        break;
                    case SocialRelationKind.Block:
                        relation = relation.WithBlocking(on);
                        break;
                }

                s_relations[userId] = relation;
            }
        }

        private static SocialRelation CurrentRelationLocked(string userId)
        {
            ForgetIfAccountChangedLocked();

            SocialRelation relation;
            if (!s_relations.TryGetValue(userId, out relation)) return null;

            DateTimeOffset end;
            if (relation.IsMuted && s_muteEnds.TryGetValue(userId, out end) && end <= DateTimeOffset.UtcNow)
            {
                relation = relation.WithMuted(false);
                s_relations[userId] = relation;
                s_muteEnds.Remove(userId);
            }

            return relation;
        }

        private static void ForgetIfAccountChangedLocked()
        {
            var account = SocialContentService.CurrentAccountId();
            if (string.Equals(account, s_accountId, StringComparison.Ordinal)) return;

            s_accountId = account;
            s_relations.Clear();
            s_muteEnds.Clear();
        }

        private static void Announce(UIElement owner, string text)
        {
            if (owner == null || string.IsNullOrEmpty(text)) return;

            try
            {
                AutomationHelper.AnnounceStatus(owner, text, "SocialModeration");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialModerationService: could not announce", ex);
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
