using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Fort.ind_UWP
{
    public sealed class SocialScheduledNote
    {
        private SocialScheduledNote(string id, DateTimeOffset scheduledAt, SocialNote note, IReadOnlyList<SocialUser> recipients)
        {
            Id = id;
            ScheduledAt = scheduledAt;
            Note = note;
            Recipients = recipients;
        }

        public string Id { get; private set; }

        public DateTimeOffset ScheduledAt { get; private set; }

        public SocialNote Note { get; private set; }

        public IReadOnlyList<SocialUser> Recipients { get; private set; }

        public bool CanEdit
        {
            get { return string.IsNullOrEmpty(Note.ReplyId); }
        }

        public SocialComposeDraft ToDraft()
        {
            return SocialComposeDraft.FromNote(Note, Recipients, false);
        }

        internal static SocialScheduledNote FromJson(JObject obj)
        {
            if (obj == null) return null;

            var id = SocialJson.String(obj, "id");
            var scheduledAt = SocialJson.Date(obj, "scheduledAt");
            var noteObject = SocialJson.Object(obj, "note");
            if (string.IsNullOrWhiteSpace(id) || scheduledAt == DateTimeOffset.MinValue || noteObject == null) return null;

            var note = SocialNote.FromJson(noteObject);
            if (note == null) return null;

            var recipients = new List<SocialUser>();
            var users = SocialJson.Array(noteObject, "visibleUsers");
            if (users != null)
            {
                recipients.AddRange(users.OfType<JObject>()
                                         .Select(value => SocialUser.FromJson(value))
                                         .Where(user => user != null && !string.IsNullOrEmpty(user.Id)));
            }

            return new SocialScheduledNote(id, scheduledAt, note, recipients);
        }

        internal static IReadOnlyList<SocialScheduledNote> ListFromJson(JArray array)
        {
            var notes = new List<SocialScheduledNote>();
            if (array == null) return notes;

            notes.AddRange(array.OfType<JObject>()
                                .Select(value => FromJson(value))
                                .Where(note => note != null)
                                .OrderBy(note => note.ScheduledAt));
            return notes;
        }
    }
}
