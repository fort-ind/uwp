using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Fort.ind_UWP
{
    public static class SocialWordMuteService
    {
        private sealed class Snapshot
        {
            public string Source;

            public SocialMuteWordSet Soft = SocialMuteWordSet.Empty;

            public SocialMuteWordSet Hard = SocialMuteWordSet.Empty;
        }

        private static Snapshot s_snapshot = new Snapshot();

        public static IReadOnlyList<string> SoftMatches(SocialNote note)
        {
            if (note == null) return null;

            var set = Current().Soft;
            return set.Count == 0 ? null : MatchAll(note, set);
        }

        public static IReadOnlyList<SocialNote> WithoutHardMuted(IReadOnlyList<SocialNote> notes)
        {
            if (notes == null || notes.Count == 0) return notes;

            var set = Current().Hard;
            if (set.Count == 0) return notes;

            var kept = notes.Where(entry => !IsHardMuted(entry, set)).ToList();
            return kept.Count == notes.Count ? notes : kept;
        }

        private static bool IsHardMuted(SocialNote entry, SocialMuteWordSet set)
        {
            if (entry == null) return false;

            var shown = entry.IsPureRenote ? entry.Renote : entry;
            return shown != null && MatchAll(shown, set) != null;
        }

        private static IReadOnlyList<string> MatchAll(SocialNote note, SocialMuteWordSet set)
        {
            var me = SocialContentService.CurrentAccountId();
            List<string> matched = null;
            foreach (var candidate in new[] { note, note.Reply, note.Renote })
            {
                if (candidate == null || (me != null && string.Equals(candidate.UserId, me, StringComparison.Ordinal))) continue;

                var found = set.Match(TextOf(candidate));
                if (found == null) continue;

                if (matched == null) matched = new List<string>();
                matched.AddRange(found.Where(word => !matched.Contains(word)));
            }

            return matched;
        }

        public static string TextOf(SocialNote note)
        {
            var parts = new StringBuilder();
            Append(parts, note.ContentWarning);
            Append(parts, note.Text);

            if (note.Files != null)
            {
                foreach (var file in note.Files)
                {
                    Append(parts, file.Comment);
                }
            }

            if (note.Poll != null)
            {
                foreach (var choice in note.Poll.Choices)
                {
                    Append(parts, choice.Text);
                }
            }

            return parts.ToString().Trim();
        }

        private static void Append(StringBuilder parts, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (parts.Length > 0) parts.Append('\n');
            parts.Append(text);
        }

        private static Snapshot Current()
        {
            var user = ProfileService.CurrentUser;
            var source = user == null ? null : user.DetailJson;
            var snapshot = s_snapshot;
            if (ReferenceEquals(snapshot.Source, source) || string.Equals(snapshot.Source, source, StringComparison.Ordinal)) return snapshot;

            var built = new Snapshot { Source = source };
            JObject obj;
            if (!string.IsNullOrEmpty(source) && JsonText.TryParseObject(source, out obj))
            {
                built.Soft = SocialMuteWordSet.FromJson(SocialJson.Array(obj, SocialAccountSettings.MutedWordsField));
                built.Hard = SocialMuteWordSet.FromJson(SocialJson.Array(obj, SocialAccountSettings.HardMutedWordsField));
            }

            s_snapshot = built;
            return built;
        }
    }
}
