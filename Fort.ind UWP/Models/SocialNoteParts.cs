using System;
using System.Collections.Generic;
using Windows.Data.Json;

namespace Fort.ind_UWP
{
    public sealed class SocialReactionCount
    {
        public SocialReactionCount(string key, int count)
        {
            Key = key;
            Count = count;
        }

        public string Key { get; private set; }

        public int Count { get; private set; }

        internal static IReadOnlyList<SocialReactionCount> ListFromJson(JsonObject reactions)
        {
            var list = new List<SocialReactionCount>();
            if (reactions == null) return list;

            foreach (var pair in reactions)
            {
                if (pair.Value.ValueType != JsonValueType.Number) continue;

                var number = pair.Value.GetNumber();
                if (double.IsNaN(number) || number <= 0) continue;

                var key = SocialReactions.Normalize(pair.Key);
                if (string.IsNullOrEmpty(key)) continue;

                var count = number >= int.MaxValue ? int.MaxValue : (int)number;
                var merged = false;
                for (var i = 0; i < list.Count; i++)
                {
                    if (!string.Equals(list[i].Key, key, StringComparison.Ordinal)) continue;

                    list[i] = new SocialReactionCount(key, list[i].Count + count);
                    merged = true;
                    break;
                }

                if (!merged) list.Add(new SocialReactionCount(key, count));
            }

            Sort(list);
            return list;
        }

        internal static void Sort(List<SocialReactionCount> list)
        {
            list.Sort((a, b) =>
            {
                var byCount = b.Count.CompareTo(a.Count);
                return byCount != 0 ? byCount : string.CompareOrdinal(a.Key, b.Key);
            });
        }

        internal static int Total(IReadOnlyList<SocialReactionCount> list)
        {
            long total = 0;
            foreach (var reaction in list)
            {
                total += reaction.Count;
            }

            return total > int.MaxValue ? int.MaxValue : (int)total;
        }
    }

    public sealed class SocialReactionEntry
    {
        private SocialReactionEntry()
        {
        }

        public string Id { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public SocialUser User { get; private set; }

        public string Type { get; private set; }

        internal static IReadOnlyList<SocialReactionEntry> ListFromJson(JsonArray array)
        {
            var entries = new List<SocialReactionEntry>();
            if (array == null) return entries;

            foreach (var value in array)
            {
                if (value.ValueType != JsonValueType.Object) continue;

                var obj = value.GetObject();
                var id = SocialJson.String(obj, "id");
                var user = SocialUser.FromJson(SocialJson.Object(obj, "user"));
                var type = SocialReactions.Normalize(SocialJson.String(obj, "type"));
                if (string.IsNullOrEmpty(id) || user == null || type == null) continue;

                entries.Add(new SocialReactionEntry
                {
                    Id = id,
                    CreatedAt = SocialJson.Date(obj, "createdAt"),
                    User = user,
                    Type = type
                });
            }

            return entries;
        }
    }

    public sealed class SocialPollChoice
    {
        public SocialPollChoice(string text, int votes, bool isVoted)
        {
            Text = text;
            Votes = votes;
            IsVoted = isVoted;
        }

        public string Text { get; private set; }

        public int Votes { get; private set; }

        public bool IsVoted { get; private set; }
    }

    public sealed class SocialPoll
    {
        private SocialPoll(bool multiple, DateTimeOffset? expiresAt, IReadOnlyList<SocialPollChoice> choices)
        {
            Multiple = multiple;
            ExpiresAt = expiresAt;
            Choices = choices;
        }

        public bool Multiple { get; private set; }

        public DateTimeOffset? ExpiresAt { get; private set; }

        public IReadOnlyList<SocialPollChoice> Choices { get; private set; }

        public int TotalVotes
        {
            get
            {
                long total = 0;
                foreach (var choice in Choices)
                {
                    total += Math.Max(0, choice.Votes);
                }

                return total > int.MaxValue ? int.MaxValue : (int)total;
            }
        }

        public bool HasVoted
        {
            get
            {
                foreach (var choice in Choices)
                {
                    if (choice.IsVoted) return true;
                }

                return false;
            }
        }

        public bool IsExpired(DateTimeOffset now)
        {
            return ExpiresAt.HasValue && ExpiresAt.Value <= now;
        }

        public SocialPoll WithVote(int index, bool mine)
        {
            if (index < 0 || index >= Choices.Count) return this;

            var choices = new List<SocialPollChoice>(Choices.Count);
            for (var i = 0; i < Choices.Count; i++)
            {
                var choice = Choices[i];
                choices.Add(i == index
                            ? new SocialPollChoice(choice.Text, choice.Votes + 1, choice.IsVoted || mine)
                            : choice);
            }

            return new SocialPoll(Multiple, ExpiresAt, choices);
        }

        internal static SocialPoll FromJson(JsonObject obj)
        {
            if (obj == null) return null;

            var array = SocialJson.Array(obj, "choices");
            var choices = new List<SocialPollChoice>();
            if (array != null)
            {
                foreach (var value in array)
                {
                    if (value.ValueType != JsonValueType.Object) continue;

                    var choice = value.GetObject();
                    choices.Add(new SocialPollChoice(SocialJson.String(choice, "text") ?? "",
                                                     Math.Max(0, SocialJson.Int(choice, "votes") ?? 0),
                                                     SocialJson.Bool(choice, "isVoted").GetValueOrDefault()));
                }
            }

            var expires = SocialJson.Date(obj, "expiresAt");

            return new SocialPoll(SocialJson.Bool(obj, "multiple").GetValueOrDefault(),
                                  expires == DateTimeOffset.MinValue ? (DateTimeOffset?)null : expires,
                                  choices);
        }
    }
}
