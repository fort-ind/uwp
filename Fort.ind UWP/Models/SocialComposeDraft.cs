using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Windows.Data.Json;

namespace Fort.ind_UWP
{
    public enum SocialPollExpiry
    {
        Never,
        FiveMinutes,
        OneHour,
        OneDay,
        ThreeDays,
        OneWeek,
        Custom
    }

    public sealed class SocialDraftPoll
    {
        public SocialDraftPoll(IReadOnlyList<string> choices, bool multiple, SocialPollExpiry expiry, DateTimeOffset? expiresAt)
        {
            Choices = choices ?? new string[0];
            Multiple = multiple;
            Expiry = expiry;
            ExpiresAt = expiresAt;
        }

        public IReadOnlyList<string> Choices { get; private set; }

        public bool Multiple { get; private set; }

        public SocialPollExpiry Expiry { get; private set; }

        public DateTimeOffset? ExpiresAt { get; private set; }

        public int FilledChoices
        {
            get { return Choices.Count(choice => !string.IsNullOrWhiteSpace(choice)); }
        }

        public static TimeSpan? DurationOf(SocialPollExpiry expiry)
        {
            switch (expiry)
            {
                case SocialPollExpiry.FiveMinutes: return TimeSpan.FromMinutes(5);
                case SocialPollExpiry.OneHour: return TimeSpan.FromHours(1);
                case SocialPollExpiry.OneDay: return TimeSpan.FromDays(1);
                case SocialPollExpiry.ThreeDays: return TimeSpan.FromDays(3);
                case SocialPollExpiry.OneWeek: return TimeSpan.FromDays(7);
                default: return null;
            }
        }

        public bool SameAs(SocialDraftPoll other)
        {
            if (other == null || other.Multiple != Multiple || other.Expiry != Expiry) return false;
            if (!Nullable.Equals(other.ExpiresAt, ExpiresAt) || other.Choices.Count != Choices.Count) return false;

            for (var i = 0; i < Choices.Count; i++)
            {
                if (!string.Equals(Choices[i], other.Choices[i], StringComparison.Ordinal)) return false;
            }

            return true;
        }

        internal JsonObject ToJson()
        {
            var obj = new JsonObject();
            var choices = new JsonArray();
            foreach (var choice in Choices)
            {
                choices.Add(JsonValue.CreateStringValue(choice ?? ""));
            }

            obj.Add("choices", choices);
            obj.Add("multiple", JsonValue.CreateBooleanValue(Multiple));
            obj.Add("expiry", JsonValue.CreateStringValue(Expiry.ToString()));
            SocialJson.Put(obj, "expiresAt", ExpiresAt.HasValue ? ExpiresAt.Value.ToString("o", CultureInfo.InvariantCulture) : null);
            return obj;
        }

        internal static SocialDraftPoll FromJson(JsonObject obj)
        {
            if (obj == null) return null;

            SocialPollExpiry expiry;
            if (!Enum.TryParse(SocialJson.String(obj, "expiry"), out expiry)) expiry = SocialPollExpiry.OneDay;

            var expiresAt = SocialJson.Date(obj, "expiresAt");
            return new SocialDraftPoll(SocialJson.StringList(obj, "choices"),
                                       SocialJson.Bool(obj, "multiple").GetValueOrDefault(),
                                       expiry,
                                       expiresAt == DateTimeOffset.MinValue ? (DateTimeOffset?)null : expiresAt);
        }

        internal static SocialDraftPoll FromPoll(SocialPoll poll)
        {
            if (poll == null) return null;

            var choices = new List<string>();
            foreach (var choice in poll.Choices)
            {
                choices.Add(choice.Text ?? "");
            }

            return new SocialDraftPoll(choices, poll.Multiple,
                                       poll.ExpiresAt.HasValue ? SocialPollExpiry.Custom : SocialPollExpiry.Never,
                                       poll.ExpiresAt);
        }
    }

    public sealed class SocialComposeDraft
    {
        public static readonly SocialComposeDraft Empty = new SocialComposeDraft();

        private SocialComposeDraft()
        {
            Text = "";
            Visibility = SocialNoteActionService.PublicVisibility;
            Recipients = new SocialUser[0];
            Files = new SocialDriveFile[0];
        }

        public string Text { get; private set; }

        public string ContentWarning { get; private set; }

        public string Visibility { get; private set; }

        public bool LocalOnly { get; private set; }

        public IReadOnlyList<SocialUser> Recipients { get; private set; }

        public string ReactionAcceptance { get; private set; }

        public SocialDraftPoll Poll { get; private set; }

        public IReadOnlyList<SocialDriveFile> Files { get; private set; }

        public string QuoteId { get; private set; }

        public string ReplyId { get; private set; }

        public string EditId { get; private set; }

        public bool HasContentWarning
        {
            get { return ContentWarning != null; }
        }

        public bool IsEmpty
        {
            get
            {
                return string.IsNullOrWhiteSpace(Text)
                       && string.IsNullOrWhiteSpace(ContentWarning)
                       && Files.Count == 0
                       && Poll == null
                       && string.IsNullOrEmpty(QuoteId);
            }
        }

        public static SocialComposeDraft Create(string text, string contentWarning, string visibility, bool localOnly,
                                                IReadOnlyList<SocialUser> recipients, string reactionAcceptance,
                                                SocialDraftPoll poll, IReadOnlyList<SocialDriveFile> files,
                                                string quoteId, string replyId, string editId)
        {
            return new SocialComposeDraft
            {
                Text = text ?? "",
                ContentWarning = contentWarning,
                Visibility = string.IsNullOrEmpty(visibility) ? SocialNoteActionService.PublicVisibility : visibility,
                LocalOnly = localOnly,
                Recipients = recipients ?? new SocialUser[0],
                ReactionAcceptance = string.IsNullOrEmpty(reactionAcceptance) ? null : reactionAcceptance,
                Poll = poll,
                Files = files ?? new SocialDriveFile[0],
                QuoteId = string.IsNullOrEmpty(quoteId) ? null : quoteId,
                ReplyId = string.IsNullOrEmpty(replyId) ? null : replyId,
                EditId = string.IsNullOrEmpty(editId) ? null : editId
            };
        }

        public static SocialComposeDraft FromNote(SocialNote note, IReadOnlyList<SocialUser> recipients, bool keepIdentity)
        {
            if (note == null) return Empty;

            var quote = note.Renote != null && !note.IsPureRenote ? note.Renote.Id : note.RenoteId;
            return Create(note.Text, string.IsNullOrEmpty(note.ContentWarning) ? null : note.ContentWarning,
                          note.Visibility, note.LocalOnly, recipients, note.ReactionAcceptance,
                          SocialDraftPoll.FromPoll(note.Poll), note.Files, quote,
                          note.ReplyId, keepIdentity ? note.Id : null);
        }

        internal JsonObject ToJson()
        {
            var obj = new JsonObject();
            SocialJson.Put(obj, "text", Text);
            SocialJson.Put(obj, "cw", ContentWarning);
            SocialJson.Put(obj, "visibility", Visibility);
            obj.Add("localOnly", JsonValue.CreateBooleanValue(LocalOnly));
            SocialJson.Put(obj, "reactionAcceptance", ReactionAcceptance);
            SocialJson.Put(obj, "quoteId", QuoteId);
            SocialJson.Put(obj, "replyId", ReplyId);
            SocialJson.Put(obj, "editId", EditId);

            var recipients = new JsonArray();
            foreach (var user in Recipients)
            {
                recipients.Add(user.ToJson());
            }
            obj.Add("recipients", recipients);

            var files = new JsonArray();
            foreach (var file in Files)
            {
                files.Add(file.ToJson());
            }
            obj.Add("files", files);

            if (Poll != null) obj.Add("poll", Poll.ToJson());
            return obj;
        }

        internal static SocialComposeDraft FromJson(JsonObject obj)
        {
            if (obj == null) return null;

            var recipients = new List<SocialUser>();
            var users = SocialJson.Array(obj, "recipients");
            if (users != null)
            {
                foreach (var value in users)
                {
                    var user = value.ValueType == JsonValueType.Object ? SocialUser.FromJson(value.GetObject()) : null;
                    if (user != null && !string.IsNullOrEmpty(user.Id)) recipients.Add(user);
                }
            }

            return Create(SocialJson.String(obj, "text"),
                          SocialJson.String(obj, "cw"),
                          SocialJson.String(obj, "visibility"),
                          SocialJson.Bool(obj, "localOnly").GetValueOrDefault(),
                          recipients,
                          SocialJson.String(obj, "reactionAcceptance"),
                          SocialDraftPoll.FromJson(SocialJson.Object(obj, "poll")),
                          SocialDriveFile.ListFromJson(SocialJson.Array(obj, "files")),
                          SocialJson.String(obj, "quoteId"),
                          SocialJson.String(obj, "replyId"),
                          SocialJson.String(obj, "editId"));
        }
    }
}
