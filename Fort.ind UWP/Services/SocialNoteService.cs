using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.Data.Json;

namespace Fort.ind_UWP
{
    public enum SocialNoteChangeKind
    {
        Reset,
        Snapshot,
        Reacted,
        Unreacted,
        Replied,
        PollVoted,
        Deleted,
        Renoted,
        Unrenoted,
        RenoteKnown,
        FavoriteChanged,
        ThreadMuteChanged,
        PinChanged,
        Posted
    }

    public sealed class SocialNoteChange
    {
        private SocialNoteChange(SocialNoteChangeKind kind, string noteId)
        {
            Kind = kind;
            NoteId = noteId;
        }

        public SocialNoteChangeKind Kind { get; private set; }

        public string NoteId { get; private set; }

        public SocialNote Note { get; private set; }

        public string Reaction { get; private set; }

        public string UserId { get; private set; }

        public string EmojiName { get; private set; }

        public string EmojiUrl { get; private set; }

        public int ChoiceIndex { get; private set; }

        public string ChildId { get; private set; }

        public bool Flag { get; private set; }

        public bool IsMine
        {
            get
            {
                return !string.IsNullOrEmpty(UserId)
                       && string.Equals(UserId, SocialContentService.CurrentAccountId(), StringComparison.Ordinal);
            }
        }

        public static SocialNoteChange Reset()
        {
            return new SocialNoteChange(SocialNoteChangeKind.Reset, null);
        }

        public static SocialNoteChange Snapshot(SocialNote note)
        {
            return note == null ? null : new SocialNoteChange(SocialNoteChangeKind.Snapshot, note.Id) { Note = note };
        }

        public static SocialNoteChange Deleted(string noteId)
        {
            return string.IsNullOrEmpty(noteId) ? null : new SocialNoteChange(SocialNoteChangeKind.Deleted, noteId);
        }

        public static SocialNoteChange DeletedBy(string noteId, string userId)
        {
            return string.IsNullOrEmpty(noteId) ? null : new SocialNoteChange(SocialNoteChangeKind.Deleted, noteId) { UserId = userId };
        }

        public static SocialNoteChange Reacted(string noteId, string userId, string reaction)
        {
            var key = SocialReactions.Normalize(reaction);
            if (string.IsNullOrEmpty(noteId) || key == null) return null;

            return new SocialNoteChange(SocialNoteChangeKind.Reacted, noteId) { Reaction = key, UserId = userId };
        }

        public static SocialNoteChange Unreacted(string noteId, string userId, string reaction)
        {
            var key = SocialReactions.Normalize(reaction);
            if (string.IsNullOrEmpty(noteId) || key == null) return null;

            return new SocialNoteChange(SocialNoteChangeKind.Unreacted, noteId) { Reaction = key, UserId = userId };
        }

        public static SocialNoteChange PollVoted(string noteId, string userId, int choice)
        {
            if (string.IsNullOrEmpty(noteId) || choice < 0) return null;

            return new SocialNoteChange(SocialNoteChangeKind.PollVoted, noteId) { ChoiceIndex = choice, UserId = userId };
        }

        public static SocialNoteChange Renoted(string noteId, string userId, string renoteId)
        {
            if (string.IsNullOrEmpty(noteId)) return null;

            return new SocialNoteChange(SocialNoteChangeKind.Renoted, noteId) { UserId = userId, ChildId = renoteId };
        }

        public static SocialNoteChange Unrenoted(string noteId, string userId)
        {
            if (string.IsNullOrEmpty(noteId)) return null;

            return new SocialNoteChange(SocialNoteChangeKind.Unrenoted, noteId) { UserId = userId };
        }

        public static SocialNoteChange RenoteKnown(string noteId, bool renoted)
        {
            return string.IsNullOrEmpty(noteId) ? null : new SocialNoteChange(SocialNoteChangeKind.RenoteKnown, noteId) { Flag = renoted };
        }

        public static SocialNoteChange Favorite(string noteId, bool favorited)
        {
            return string.IsNullOrEmpty(noteId) ? null : new SocialNoteChange(SocialNoteChangeKind.FavoriteChanged, noteId) { Flag = favorited };
        }

        public static SocialNoteChange ThreadMute(string noteId, bool muted)
        {
            return string.IsNullOrEmpty(noteId) ? null : new SocialNoteChange(SocialNoteChangeKind.ThreadMuteChanged, noteId) { Flag = muted };
        }

        public static SocialNoteChange Posted(SocialNote note)
        {
            return note == null ? null : new SocialNoteChange(SocialNoteChangeKind.Posted, note.Id) { Note = note, UserId = note.UserId };
        }

        public static SocialNoteChange RepliedWith(string parentId, SocialNote reply)
        {
            if (string.IsNullOrEmpty(parentId) || reply == null) return null;

            return new SocialNoteChange(SocialNoteChangeKind.Replied, parentId) { ChildId = reply.Id, UserId = reply.UserId, Note = reply };
        }

        public static SocialNoteChange Pin(string noteId, bool pinned)
        {
            return string.IsNullOrEmpty(noteId) ? null : new SocialNoteChange(SocialNoteChangeKind.PinChanged, noteId) { Flag = pinned };
        }

        public static SocialNoteChange FromStream(string noteId, string type, JsonObject body)
        {
            if (string.IsNullOrEmpty(noteId) || string.IsNullOrEmpty(type)) return null;

            switch (type)
            {
                case "reacted":
                    {
                        var change = Reacted(noteId, SocialJson.String(body, "userId"), SocialJson.String(body, "reaction"));
                        if (change == null) return null;

                        var emoji = SocialJson.Object(body, "emoji");
                        change.EmojiName = SocialJson.String(emoji, "name");
                        change.EmojiUrl = SocialJson.String(emoji, "url");
                        return change;
                    }
                case "unreacted":
                    return Unreacted(noteId, SocialJson.String(body, "userId"), SocialJson.String(body, "reaction"));
                case "pollVoted":
                    {
                        var choice = SocialJson.Int(body, "choice");
                        return choice.HasValue ? PollVoted(noteId, SocialJson.String(body, "userId"), choice.Value) : null;
                    }
                case "replied":
                    {
                        var childId = SocialJson.String(body, "id");
                        if (string.IsNullOrEmpty(childId)) return null;

                        return new SocialNoteChange(SocialNoteChangeKind.Replied, noteId)
                        {
                            ChildId = childId,
                            UserId = SocialJson.String(body, "userId")
                        };
                    }
                case "deleted":
                    return Deleted(noteId);
                default:
                    return null;
            }
        }
    }

    public static class SocialNoteService
    {
        private static readonly object s_lock = new object();

        private static readonly Dictionary<string, SocialNoteState> s_states = new Dictionary<string, SocialNoteState>(StringComparer.Ordinal);

        private static readonly Queue<string> s_stateOrder = new Queue<string>();

        private static readonly Dictionary<string, bool> s_renoted = new Dictionary<string, bool>(StringComparer.Ordinal);

        private static readonly Queue<string> s_renotedOrder = new Queue<string>();

        private static HashSet<string> s_pinned;

        private static string s_accountId;

        private static bool s_initialized;

        private static int s_favoritesVersion;

        private static int s_pinsVersion;

        private static int s_postsVersion;

        private static readonly List<KeyValuePair<int, SocialNote>> s_posts = new List<KeyValuePair<int, SocialNote>>();

        public static int PostsVersion
        {
            get { return System.Threading.Volatile.Read(ref s_postsVersion); }
        }

        public static IReadOnlyList<SocialNote> PostsSince(int version)
        {
            var posts = new List<SocialNote>();
            lock (s_lock)
            {
                foreach (var pair in s_posts)
                {
                    if (pair.Key > version) posts.Add(pair.Value);
                }
            }

            return posts;
        }

        public static int FavoritesVersion
        {
            get { return System.Threading.Volatile.Read(ref s_favoritesVersion); }
        }

        public static int PinsVersion
        {
            get { return System.Threading.Volatile.Read(ref s_pinsVersion); }
        }

        public static event EventHandler<SocialNoteChange> Changed;

        public static void Initialize()
        {
            lock (s_lock)
            {
                if (s_initialized) return;
                s_initialized = true;
                s_accountId = SocialContentService.CurrentAccountId();
            }

            ProfileService.AuthStateChanged += OnAuthStateChanged;
        }

        public static void Raise(SocialNoteChange change)
        {
            if (change == null) return;

            Remember(change);

            try
            {
                Changed?.Invoke(null, change);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteService: a note change handler failed - {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static SocialNoteState CachedState(string noteId)
        {
            if (string.IsNullOrEmpty(noteId)) return null;

            lock (s_lock)
            {
                SocialNoteState state;
                return s_states.TryGetValue(noteId, out state) ? state : null;
            }
        }

        public static void RememberState(string noteId, SocialNoteState state)
        {
            if (string.IsNullOrEmpty(noteId) || state == null) return;

            lock (s_lock)
            {
                if (!s_states.ContainsKey(noteId)) s_stateOrder.Enqueue(noteId);
                s_states[noteId] = state;

                while (s_stateOrder.Count > AppConstants.SocialNoteStateCacheLimit)
                {
                    s_states.Remove(s_stateOrder.Dequeue());
                }
            }
        }

        public static bool? KnownRenoted(string noteId)
        {
            if (string.IsNullOrEmpty(noteId)) return null;

            lock (s_lock)
            {
                bool renoted;
                return s_renoted.TryGetValue(noteId, out renoted) ? renoted : (bool?)null;
            }
        }

        public static bool? IsPinned(string noteId)
        {
            if (string.IsNullOrEmpty(noteId)) return null;

            lock (s_lock)
            {
                return s_pinned == null ? (bool?)null : s_pinned.Contains(noteId);
            }
        }

        public static void RememberPinned(IEnumerable<string> noteIds)
        {
            var pinned = new HashSet<string>(StringComparer.Ordinal);
            if (noteIds != null)
            {
                foreach (var id in noteIds)
                {
                    if (!string.IsNullOrEmpty(id)) pinned.Add(id);
                }
            }

            lock (s_lock)
            {
                s_pinned = pinned;
            }
        }

        private static void Remember(SocialNoteChange change)
        {
            lock (s_lock)
            {
                switch (change.Kind)
                {
                    case SocialNoteChangeKind.Renoted:
                        RememberRenoted(change.NoteId, true);
                        break;
                    case SocialNoteChangeKind.Unrenoted:
                        RememberRenoted(change.NoteId, false);
                        break;
                    case SocialNoteChangeKind.RenoteKnown:
                        RememberRenoted(change.NoteId, change.Flag);
                        break;
                    case SocialNoteChangeKind.FavoriteChanged:
                        {
                            s_favoritesVersion++;
                            SocialNoteState state;
                            if (s_states.TryGetValue(change.NoteId, out state)) s_states[change.NoteId] = state.WithFavorited(change.Flag);
                            break;
                        }
                    case SocialNoteChangeKind.ThreadMuteChanged:
                        {
                            SocialNoteState state;
                            if (s_states.TryGetValue(change.NoteId, out state)) s_states[change.NoteId] = state.WithMutedThread(change.Flag);
                            break;
                        }
                    case SocialNoteChangeKind.Posted:
                        s_postsVersion++;
                        s_posts.Add(new KeyValuePair<int, SocialNote>(s_postsVersion, change.Note));
                        while (s_posts.Count > AppConstants.SocialRecentPostLimit) s_posts.RemoveAt(0);
                        break;
                    case SocialNoteChangeKind.PinChanged:
                        s_pinsVersion++;
                        if (s_pinned != null)
                        {
                            if (change.Flag) s_pinned.Add(change.NoteId);
                            else s_pinned.Remove(change.NoteId);
                        }
                        break;
                }
            }
        }

        private static void RememberRenoted(string noteId, bool renoted)
        {
            if (!s_renoted.ContainsKey(noteId)) s_renotedOrder.Enqueue(noteId);
            s_renoted[noteId] = renoted;

            while (s_renotedOrder.Count > AppConstants.SocialNoteStateCacheLimit)
            {
                s_renoted.Remove(s_renotedOrder.Dequeue());
            }
        }

        private static void OnAuthStateChanged(object sender, bool isSignedIn)
        {
            var account = SocialContentService.CurrentAccountId();
            lock (s_lock)
            {
                if (!string.Equals(account, s_accountId, StringComparison.Ordinal))
                {
                    s_accountId = account;
                    s_states.Clear();
                    s_stateOrder.Clear();
                    s_renoted.Clear();
                    s_renotedOrder.Clear();
                    s_pinned = null;
                    s_posts.Clear();
                }
            }

            Raise(SocialNoteChange.Reset());
        }
    }
}
