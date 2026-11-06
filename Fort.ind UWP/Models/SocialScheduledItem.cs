using System.Collections.Generic;
using Windows.Globalization.DateTimeFormatting;

namespace Fort.ind_UWP
{
    public sealed class SocialScheduledItem
    {
        private const int ExcerptLength = 200;

        private SocialScheduledItem(SocialScheduledNote scheduled, string whenText, string summary, string details, string glyph)
        {
            Scheduled = scheduled;
            WhenText = whenText;
            Summary = summary;
            Details = details;
            VisibilityGlyph = glyph;
        }

        public SocialScheduledNote Scheduled { get; private set; }

        public string WhenText { get; private set; }

        public string Summary { get; private set; }

        public string Details { get; private set; }

        public string VisibilityGlyph { get; private set; }

        public bool CanEdit
        {
            get { return Scheduled.CanEdit; }
        }

        public string AutomationName
        {
            get { return WhenText + ". " + Summary + ". " + Details; }
        }

        public static SocialScheduledItem Create(SocialScheduledNote scheduled)
        {
            var note = scheduled.Note;
            var formatter = new DateTimeFormatter("shortdate shorttime");
            var when = LocalizedStrings.Format("SocialScheduledWhenFormat", formatter.Format(scheduled.ScheduledAt));

            var warning = (note.ContentWarning ?? "").Trim();
            var text = (note.Text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            string summary;
            if (warning.Length > 0)
            {
                summary = LocalizedStrings.Format("SocialScheduledWarningFormat", Excerpt(warning));
            }
            else if (text.Length > 0)
            {
                summary = Excerpt(text);
            }
            else
            {
                summary = LocalizedStrings.Get("SocialScheduledNoText");
            }

            var parts = new List<string>();
            parts.Add(LocalizedStrings.Get(SocialComposer.NameKeyFor(note.Visibility)));
            if (note.Files.Count > 0) parts.Add(LocalizedStrings.Format("SocialScheduledFilesFormat", note.Files.Count));
            if (note.Poll != null) parts.Add(LocalizedStrings.Get("SocialScheduledPoll"));
            if (!string.IsNullOrEmpty(note.RenoteId)) parts.Add(LocalizedStrings.Get("SocialScheduledQuote"));
            if (!string.IsNullOrEmpty(note.ReplyId)) parts.Add(LocalizedStrings.Get("SocialScheduledReply"));

            return new SocialScheduledItem(scheduled, when, summary, string.Join(" · ", parts),
                                           SocialComposer.GlyphFor(note.Visibility));
        }

        private static string Excerpt(string text)
        {
            return text.Length <= ExcerptLength ? text : text.Substring(0, ExcerptLength).TrimEnd() + "…";
        }
    }
}
