using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;

namespace Fort.ind_UWP
{
    public sealed class SocialReplyDefaults
    {
        public SocialReplyDefaults(string visibility, bool localOnly, IReadOnlyList<string> recipientIds, string prefix)
        {
            Visibility = visibility;
            LocalOnly = localOnly;
            RecipientIds = recipientIds;
            Prefix = prefix;
        }

        public string Visibility { get; private set; }

        public bool LocalOnly { get; private set; }

        public IReadOnlyList<string> RecipientIds { get; private set; }

        public string Prefix { get; private set; }
    }

    public sealed class SocialPostResult
    {
        public SocialPostResult(SocialNote note, SocialApiStatus status, string errorCode)
        {
            Note = note;
            Status = status;
            ErrorCode = errorCode;
        }

        public SocialNote Note { get; private set; }

        public SocialApiStatus Status { get; private set; }

        public string ErrorCode { get; private set; }

        public bool Succeeded
        {
            get { return Note != null; }
        }
    }

    public static class SocialPostService
    {
        public const string DirectVisibility = "specified";

        private static readonly Dictionary<string, string> s_errorMessages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "MAX_LENGTH", "SocialPostErrorTooLong" },
            { "MAX_CW_LENGTH", "SocialPostErrorWarningTooLong" },
            { "CONTAINS_PROHIBITED_WORDS", "SocialPostErrorProhibitedWords" },
            { "CONTAINS_TOO_MANY_MENTIONS", "SocialPostErrorTooManyMentions" },
            { "YOU_HAVE_BEEN_BLOCKED", "SocialPostErrorBlocked" },
            { "NO_SUCH_REPLY_TARGET", "SocialPostErrorReplyGone" },
            { "CANNOT_REPLY_TO_AN_INVISIBLE_NOTE", "SocialPostErrorReplyGone" },
            { "NO_SUCH_RENOTE_TARGET", "SocialPostErrorQuoteGone" },
            { "CANNOT_RENOTE_DUE_TO_VISIBILITY", "SocialActionErrorRenoteVisibility" },
            { "QUOTE_DISABLED_FOR_USER", "SocialPostErrorQuoteDisabled" },
            { "CANNOT_CREATE_ALREADY_EXPIRED_POLL", "SocialPostErrorPollEnded" },
            { "NO_SUCH_FILE", "SocialPostErrorFileGone" },
            { "NO_SUCH_NOTE", "SocialActionErrorNoSuchNote" },
            { "YOU_ARE_NOT_THE_AUTHOR", "SocialPostErrorNotAuthor" },
            { "CANNOT_REPLY_TO_SPECIFIED_VISIBILITY_NOTE_WITH_EXTENDED_VISIBILITY", "SocialPostErrorReplyVisibility" }
        };

        public static int Length(string text)
        {
            return NormalizeText(text).Length;
        }

        public static string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        public static SocialReplyDefaults ReplyDefaultsFor(SocialNote parent)
        {
            return ReplyDefaultsFor(parent, SocialContentService.CurrentAccountId());
        }

        public static SocialReplyDefaults ReplyDefaultsFor(SocialNote parent, string me)
        {
            if (parent == null) return new SocialReplyDefaults(SocialNoteActionService.PublicVisibility, false, new string[0], "");

            var visibility = string.IsNullOrEmpty(parent.Visibility) ? SocialNoteActionService.PublicVisibility : parent.Visibility;

            var recipients = new List<string>();
            if (visibility == DirectVisibility)
            {
                recipients.AddRange(parent.VisibleUserIds.Where(id => !string.IsNullOrEmpty(id) && id != me).Distinct());

                if (parent.User != null && parent.User.Id != me && !recipients.Contains(parent.User.Id)) recipients.Add(parent.User.Id);
            }

            var prefix = new System.Text.StringBuilder();
            if (parent.User != null && parent.User.Id != me)
            {
                prefix.Append(parent.User.Handle).Append(' ');
            }

            var handles = parent.MentionHandles.Where(pair => pair.Key != me && !string.IsNullOrEmpty(pair.Value))
                                               .Select(pair => pair.Value);
            foreach (var handle in handles)
            {
                var present = prefix.ToString().IndexOf(handle + " ", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!present) prefix.Append(handle).Append(' ');
            }

            return new SocialReplyDefaults(visibility, parent.LocalOnly, recipients, prefix.ToString());
        }

        public static string WithReplyPrefix(string prefix, string text)
        {
            var typed = (text ?? "").Trim();
            var lead = (prefix ?? "").Trim();
            if (lead.Length == 0) return typed;

            var space = lead.IndexOf(' ');
            var author = space < 0 ? lead : lead.Substring(0, space);
            if (typed.StartsWith(author, StringComparison.OrdinalIgnoreCase)
                && (typed.Length == author.Length || char.IsWhiteSpace(typed[author.Length])))
            {
                return typed;
            }

            return prefix + typed;
        }

        public static JsonObject BuildReplyBody(SocialNote parent, SocialReplyDefaults defaults, string text)
        {
            var draft = SocialComposeDraft.Create(text, null, defaults.Visibility, defaults.LocalOnly, null, null, null, null,
                                                  null, parent.Id, null);
            var body = BuildBody(draft);
            if (draft.Visibility == DirectVisibility)
            {
                var ids = new JsonArray();
                foreach (var id in defaults.RecipientIds)
                {
                    ids.Add(JsonValue.CreateStringValue(id));
                }
                body.SetNamedValue("visibleUserIds", ids);
            }

            return body;
        }

        public static JsonObject BuildBody(SocialComposeDraft draft)
        {
            var body = new JsonObject();
            var text = NormalizeText(draft.Text);
            body.Add("text", text.Trim().Length == 0 ? JsonValue.CreateNullValue() : JsonValue.CreateStringValue(text));
            body.Add("cw", draft.HasContentWarning ? JsonValue.CreateStringValue(NormalizeText(draft.ContentWarning)) : JsonValue.CreateNullValue());
            body.Add("visibility", JsonValue.CreateStringValue(draft.Visibility));

            var direct = draft.Visibility == DirectVisibility;
            body.Add("localOnly", JsonValue.CreateBooleanValue(draft.LocalOnly && !direct));
            body.Add("reactionAcceptance", draft.ReactionAcceptance == null
                                           ? JsonValue.CreateNullValue()
                                           : JsonValue.CreateStringValue(draft.ReactionAcceptance));

            if (direct)
            {
                var ids = new JsonArray();
                foreach (var user in draft.Recipients)
                {
                    ids.Add(JsonValue.CreateStringValue(user.Id));
                }
                body.Add("visibleUserIds", ids);
            }

            if (draft.ReplyId != null) body.Add("replyId", JsonValue.CreateStringValue(draft.ReplyId));
            if (draft.QuoteId != null) body.Add("renoteId", JsonValue.CreateStringValue(draft.QuoteId));
            if (draft.EditId != null) body.Add("editId", JsonValue.CreateStringValue(draft.EditId));

            if (draft.Files.Count > 0)
            {
                var files = new JsonArray();
                foreach (var file in draft.Files)
                {
                    files.Add(JsonValue.CreateStringValue(file.Id));
                }
                body.Add("fileIds", files);
            }

            var poll = PollBody(draft.Poll);
            if (poll != null) body.Add("poll", poll);

            return body;
        }

        private static JsonObject PollBody(SocialDraftPoll poll)
        {
            if (poll == null || poll.FilledChoices < 2) return null;

            var choices = new JsonArray();
            foreach (var choice in poll.Choices.Where(choice => !string.IsNullOrWhiteSpace(choice)))
            {
                choices.Add(JsonValue.CreateStringValue(choice.Trim()));
            }

            var body = new JsonObject();
            body.Add("choices", choices);
            body.Add("multiple", JsonValue.CreateBooleanValue(poll.Multiple));

            var duration = SocialDraftPoll.DurationOf(poll.Expiry);
            if (poll.Expiry == SocialPollExpiry.Custom && poll.ExpiresAt.HasValue)
            {
                body.Add("expiresAt", JsonValue.CreateNumberValue(poll.ExpiresAt.Value.ToUnixTimeMilliseconds()));
                body.Add("expiredAfter", JsonValue.CreateNullValue());
            }
            else if (duration.HasValue)
            {
                body.Add("expiresAt", JsonValue.CreateNullValue());
                body.Add("expiredAfter", JsonValue.CreateNumberValue(duration.Value.TotalMilliseconds));
            }
            else
            {
                body.Add("expiresAt", JsonValue.CreateNullValue());
                body.Add("expiredAfter", JsonValue.CreateNullValue());
            }

            return body;
        }

        public static async Task<SocialPostResult> PostAsync(UIElement owner, SocialComposeDraft draft)
        {
            if (draft == null) return new SocialPostResult(null, SocialApiStatus.Failed, null);
            if (!await SocialPermissions.EnsureAsync(owner, SocialPermissions.WriteNotes, SocialSignInPrompt.Post))
            {
                return new SocialPostResult(null, SocialApiStatus.PermissionDenied, null);
            }

            SocialApiResult<SocialNote> result;
            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                var body = BuildBody(draft);
                result = draft.EditId != null
                         ? await SocialApiService.EditNoteAsync(token, body, CancellationToken.None)
                         : await SocialApiService.CreateNoteAsync(token, body, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialPostService: posting failed - {ex.GetType().Name}: {ex.Message}");
                result = SocialApiResult<SocialNote>.Failed(SocialApiStatus.Failed);
            }

            if (result.Status == SocialApiStatus.Ok)
            {
                Announce(draft, result.Value);
                return new SocialPostResult(result.Value, result.Status, null);
            }

            if (result.Status == SocialApiStatus.PermissionDenied)
            {
                await SocialPermissions.HandleDeniedAsync(owner, SocialPermissions.WriteNotes, SocialSignInPrompt.Post);
            }
            else
            {
                await DialogService.ShowMessageAsync(owner,
                                                     LocalizedStrings.Get(draft.EditId != null ? "SocialPostEditFailedTitle"
                                                                          : draft.ReplyId != null ? "SocialPostReplyFailedTitle"
                                                                          : "SocialPostFailedTitle"),
                                                     MessageFor(result.ErrorCode),
                                                     LocalizedStrings.Get("DialogOk"));
            }

            return new SocialPostResult(null, result.Status, result.ErrorCode);
        }

        public static string MessageFor(string errorCode)
        {
            string key;
            if (errorCode == null || !s_errorMessages.TryGetValue(errorCode, out key)) key = "SocialPostErrorGeneric";

            return LocalizedStrings.Get(key);
        }

        private static void Announce(SocialComposeDraft draft, SocialNote note)
        {
            if (draft.EditId != null)
            {
                SocialNoteService.Raise(SocialNoteChange.Snapshot(note));
                return;
            }

            if (draft.ReplyId != null) SocialNoteService.Raise(SocialNoteChange.RepliedWith(draft.ReplyId, note));
            SocialNoteService.Raise(SocialNoteChange.Posted(note));
        }

        public static async Task<IReadOnlyList<SocialUser>> ResolveMentionsAsync(string text, IReadOnlyList<SocialUser> known)
        {
            var found = new List<SocialUser>();
            var segments = MfmText.Parse(NormalizeText(text), null, null, null);
            var me = SocialContentService.CurrentAccountId();

            foreach (var segment in segments.Where(segment => segment.Kind == MfmSegmentKind.Mention && !string.IsNullOrEmpty(segment.Username)))
            {
                var host = string.IsNullOrEmpty(segment.Host)
                           || string.Equals(segment.Host, MisskeyAuthService.InstanceHost, StringComparison.OrdinalIgnoreCase)
                           ? null
                           : segment.Host;

                if (Contains(known, segment.Username, host) || Contains(found, segment.Username, host)) continue;

                try
                {
                    var result = await SocialContentService.FetchUserByHandleAsync(segment.Username, host, CancellationToken.None);
                    if (result.Status == SocialApiStatus.Ok && result.Value.User != null && result.Value.User.Id != me)
                    {
                        found.Add(result.Value.User);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SocialPostService: could not resolve a mention - {ex.Message}");
                }
            }

            return found;
        }

        private static bool Contains(IReadOnlyList<SocialUser> users, string username, string host)
        {
            if (users == null) return false;

            foreach (var user in users)
            {
                var userHost = string.IsNullOrEmpty(user.Host) ? null : user.Host;
                if (string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(userHost, host, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
