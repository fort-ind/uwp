using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Fort.ind_UWP
{
    public sealed class SocialNoteItem : INotifyPropertyChanged, ISocialFeedEntry
    {
        private const string HomeVisibility = "home";

        private const string FollowersVisibility = "followers";

        private const string DirectVisibility = "specified";

        private const int AvatarDecodeSize = 40;

        private const int ExcerptTextElements = 200;

        private const int SeenReplyLimit = 16;

        private readonly Queue<string> _seenReplies = new Queue<string>();

        private IReadOnlyDictionary<string, Uri> _localEmojis;

        private string _timeText;

        private bool _isContentRevealed;

        private bool _isMediaRevealed;

        private SocialLinkPreview _linkPreview;

        private bool _linkPreviewRequested;

        private ImageSource _avatar;

        private bool _avatarCreated;

        private IReadOnlyList<SocialReactionCount> _reactions;

        private Dictionary<string, string> _extraReactionEmojis;

        private string _myReaction;

        private int _reactionCount;

        private int _repliesCount;

        private int _renoteCount;

        private SocialPoll _poll;

        private bool _isDeleted;

        private bool _isPollRevealed;

        private bool _isRenotedByMe;

        private SocialNoteItem()
        {
        }

        public string PagingId { get; private set; }

        public SocialNote Note { get; private set; }

        public SocialUser Author
        {
            get { return Note.User; }
        }

        public SocialUser Renoter { get; private set; }

        public bool IsPinned { get; private set; }

        public int Depth { get; private set; }

        public int ContinueThreadCount { get; private set; }

        public bool ContinuesDeeper { get; private set; }

        public IReadOnlyList<MfmSegment> NameSegments { get; private set; }

        public string AuthorName { get; private set; }

        public string AuthorHandle { get; private set; }

        public string RenoterName { get; private set; }

        public IReadOnlyList<MfmSegment> BodySegments { get; private set; }

        public string ContentWarning { get; private set; }

        public bool HasContentWarning
        {
            get { return !string.IsNullOrEmpty(ContentWarning); }
        }

        public SocialUser ReplyToUser { get; private set; }

        public bool IsReply { get; private set; }

        public IReadOnlyList<SocialDriveFile> Media
        {
            get { return Note.Files; }
        }

        public bool HasSensitiveMedia { get; private set; }

        public string LinkPreviewUrl { get; private set; }

        public SocialNote Quote { get; private set; }

        public IReadOnlyList<MfmSegment> QuoteNameSegments { get; private set; }

        public IReadOnlyList<MfmSegment> QuoteBodySegments { get; private set; }

        public string QuoteTimeText { get; private set; }

        public bool IsHidden
        {
            get { return Note.IsHidden; }
        }

        public bool HasPoll
        {
            get { return _poll != null; }
        }

        public SocialPoll Poll
        {
            get { return _poll; }
        }

        public bool IsPollRevealed
        {
            get { return _isPollRevealed; }
            set
            {
                if (_isPollRevealed == value) return;
                _isPollRevealed = value;
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<SocialReactionCount> Reactions
        {
            get { return _reactions; }
        }

        public string MyReaction
        {
            get { return _myReaction; }
        }

        public int ReactionCount
        {
            get { return _reactionCount; }
        }

        public int RepliesCount
        {
            get { return _repliesCount; }
        }

        public int RenoteCount
        {
            get { return _renoteCount; }
        }

        public bool IsDeleted
        {
            get { return _isDeleted; }
        }

        public bool IsRenotedByMe
        {
            get { return _isRenotedByMe; }
        }

        public bool IsMine
        {
            get { return Author != null && IsMe(Author.Id); }
        }

        public bool IsGone
        {
            get { return _isDeleted || (Renoter != null && IsMe(Renoter.Id) && !_isRenotedByMe); }
        }

        public static bool IsRemovedBy(SocialNoteItem item, SocialNoteChange change)
        {
            if (item == null || change == null || !string.Equals(item.Note.Id, change.NoteId, StringComparison.Ordinal)) return false;

            switch (change.Kind)
            {
                case SocialNoteChangeKind.Deleted:
                    return true;
                case SocialNoteChangeKind.Unrenoted:
                    return change.IsMine && item.Renoter != null && IsMe(item.Renoter.Id);
                default:
                    return false;
            }
        }

        public bool IsEdited
        {
            get { return Note.UpdatedAt.HasValue; }
        }

        public string EditedTimeFull
        {
            get { return Note.UpdatedAt.HasValue ? RelativeTime.Full(Note.UpdatedAt.Value) : null; }
        }

        public IReadOnlyDictionary<string, Uri> LocalEmojis
        {
            get { return _localEmojis != null && _localEmojis.Count > 0 ? _localEmojis : SocialContentService.CachedEmojiMap; }
        }

        public string VisibilityGlyph { get; private set; }

        public string VisibilityName { get; private set; }

        public string TimeFull { get; private set; }

        public string NoteUrl
        {
            get { return SocialLinks.NoteUrl(Note.Id); }
        }

        public string TimeText
        {
            get { return _timeText; }
        }

        public bool IsContentRevealed
        {
            get { return _isContentRevealed; }
            set
            {
                if (_isContentRevealed == value) return;
                _isContentRevealed = value;
                OnPropertyChanged();
                OnPropertyChanged("AutomationName");
            }
        }

        public bool IsMediaRevealed
        {
            get { return _isMediaRevealed; }
            set
            {
                if (_isMediaRevealed == value) return;
                _isMediaRevealed = value;
                OnPropertyChanged();
            }
        }

        public SocialLinkPreview LinkPreview
        {
            get { return _linkPreview; }
            private set
            {
                if (ReferenceEquals(_linkPreview, value)) return;
                _linkPreview = value;
                OnPropertyChanged();
            }
        }

        public ImageSource Avatar
        {
            get
            {
                if (_avatarCreated) return _avatar;
                _avatarCreated = true;

                var uri = Author == null ? null : WebLauncher.TryCreateFetchUri(Author.AvatarUrl);
                if (uri == null) return null;

                BitmapImage bitmap = new BitmapImage();
                bitmap.DecodePixelType = DecodePixelType.Logical;
                bitmap.DecodePixelWidth = AvatarDecodeSize;
                bitmap.DecodePixelHeight = AvatarDecodeSize;
                bitmap.UriSource = uri;
                _avatar = bitmap;
                return _avatar;
            }
        }

        public string AutomationName
        {
            get
            {
                var parts = new StringBuilder();

                if (Renoter != null)
                {
                    parts.Append(LocalizedStrings.Format("SocialRenotedByFormat", RenoterName));
                    parts.Append(". ");
                }

                string content;
                if (_isDeleted)
                {
                    content = LocalizedStrings.Get("SocialNoteDeleted");
                }
                else if (IsHidden)
                {
                    content = LocalizedStrings.Get("SocialHiddenNote");
                }
                else if (HasContentWarning && !IsContentRevealed)
                {
                    content = LocalizedStrings.Format("SocialNoteContentWarningAutomationFormat", ContentWarning);
                }
                else
                {
                    content = Excerpt(MfmText.PlainText(BodySegments));
                }

                parts.Append(LocalizedStrings.Format("SocialNoteAutomationFormat", AuthorName + " " + AuthorHandle, TimeFull, content));

                if (IsEdited)
                {
                    parts.Append(". ");
                    parts.Append(LocalizedStrings.Get("SocialNoteEditedAutomation"));
                }

                if (Media.Count > 0)
                {
                    parts.Append(". ");
                    parts.Append(LocalizedStrings.Format(Media.Count == 1 ? "SocialNoteAttachmentsOneAutomationFormat" : "SocialNoteAttachmentsAutomationFormat", Media.Count));
                }

                if (Quote != null)
                {
                    parts.Append(". ");
                    parts.Append(LocalizedStrings.Format("SocialQuoteAutomationFormat",
                                                         Quote.User == null ? LocalizedStrings.Get("SocialUnknownUser") : Quote.User.Handle,
                                                         Excerpt(MfmText.PlainText(QuoteBodySegments))));
                }

                if (_repliesCount > 0 || _renoteCount > 0 || _reactionCount > 0)
                {
                    parts.Append(". ");
                    parts.Append(LocalizedStrings.Format("SocialNoteCountsAutomationFormat",
                                                         _repliesCount, _renoteCount, _reactionCount));
                }

                return parts.ToString();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void RefreshTime(DateTimeOffset now)
        {
            var text = RelativeTime.Short(Note.CreatedAt, now);
            if (string.Equals(text, _timeText, StringComparison.Ordinal)) return;

            _timeText = text;
            OnPropertyChanged("TimeText");
        }

        public async void RequestLinkPreview()
        {
            try
            {
                if (_linkPreviewRequested || string.IsNullOrEmpty(LinkPreviewUrl)) return;
                _linkPreviewRequested = true;

                var url = LinkPreviewUrl;
                var preview = await SocialContentService.GetLinkPreviewAsync(url);
                if (preview != null && string.Equals(url, LinkPreviewUrl, StringComparison.Ordinal)) LinkPreview = preview;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SocialNoteItem: link preview failed - {ex.Message}");
            }
        }

        public string ReactionEmojiUrl(string key)
        {
            var name = SocialReactions.CustomName(key);
            if (name == null || !SocialReactions.IsRemoteCustom(key)) return null;

            var host = key.Substring(name.Length + 2, key.Length - name.Length - 3);
            string url;
            if (_extraReactionEmojis != null && _extraReactionEmojis.TryGetValue(name + "@" + host, out url)) return url;
            return Note.ReactionEmojis.TryGetValue(name + "@" + host, out url) ? url : null;
        }

        public Uri ReactionImageUri(string key)
        {
            if (!SocialReactions.IsCustom(key)) return null;

            if (SocialReactions.IsRemoteCustom(key))
            {
                return SocialLinks.StaticEmojiUri(ReactionEmojiUrl(key));
            }

            return SocialReactions.ImageUri(key, null, LocalEmojis);
        }

        internal void SetContinuation(int moreReplies, bool deeper)
        {
            ContinueThreadCount = Math.Max(0, moreReplies);
            ContinuesDeeper = deeper;
        }

        public void Apply(SocialNoteChange change)
        {
            if (change == null || !string.Equals(change.NoteId, Note.Id, StringComparison.Ordinal)) return;

            switch (change.Kind)
            {
                case SocialNoteChangeKind.Snapshot:
                    ApplySnapshot(change.Note);
                    break;
                case SocialNoteChangeKind.Reacted:
                    ApplyReacted(change);
                    break;
                case SocialNoteChangeKind.Unreacted:
                    ApplyUnreacted(change);
                    break;
                case SocialNoteChangeKind.Replied:
                    ApplyReplied(change.ChildId);
                    break;
                case SocialNoteChangeKind.PollVoted:
                    ApplyPollVoted(change);
                    break;
                case SocialNoteChangeKind.Deleted:
                    if (_isDeleted) return;
                    _isDeleted = true;
                    OnPropertyChanged("IsDeleted");
                    OnPropertyChanged("AutomationName");
                    break;
                case SocialNoteChangeKind.Renoted:
                    _renoteCount++;
                    if (change.IsMine) _isRenotedByMe = true;
                    RaiseRenotesChanged();
                    break;
                case SocialNoteChangeKind.Unrenoted:
                    _renoteCount = Math.Max(0, _renoteCount - 1);
                    if (change.IsMine) _isRenotedByMe = false;
                    RaiseRenotesChanged();
                    break;
                case SocialNoteChangeKind.RenoteKnown:
                    if (_isRenotedByMe == change.Flag) return;
                    _isRenotedByMe = change.Flag;
                    OnPropertyChanged("IsRenotedByMe");
                    break;
            }
        }

        private void RaiseRenotesChanged()
        {
            OnPropertyChanged("RenoteCount");
            OnPropertyChanged("IsRenotedByMe");
            OnPropertyChanged("AutomationName");
        }

        private bool HasSameContent(SocialNote note)
        {
            var current = Note;
            return Nullable.Equals(current.UpdatedAt, note.UpdatedAt)
                   && current.IsHidden == note.IsHidden
                   && string.Equals(current.Text, note.Text, StringComparison.Ordinal)
                   && string.Equals(current.ContentWarning, note.ContentWarning, StringComparison.Ordinal)
                   && string.Equals(current.Visibility, note.Visibility, StringComparison.Ordinal)
                   && current.Files.Count == note.Files.Count
                   && (current.Poll == null) == (note.Poll == null)
                   && (current.Poll == null || current.Poll.Choices.Count == note.Poll.Choices.Count);
        }

        private void ApplyState(SocialNote note)
        {
            Note = note;
            _extraReactionEmojis = null;
            _reactions = note.Reactions;
            _myReaction = note.MyReaction;
            _reactionCount = Math.Max(note.ReactionCount, SocialReactionCount.Total(note.Reactions));
            _repliesCount = note.RepliesCount;
            _renoteCount = note.RenoteCount;
            _poll = note.IsHidden ? null : note.Poll;

            OnPropertyChanged("Reactions");
            OnPropertyChanged("RepliesCount");
            OnPropertyChanged("RenoteCount");
            OnPropertyChanged("Poll");
            OnPropertyChanged("AutomationName");
        }

        private void ApplySnapshot(SocialNote note)
        {
            if (note == null || note.User == null) return;

            if (HasSameContent(note))
            {
                ApplyState(note);
                return;
            }

            var hadSensitive = HasSensitiveMedia;
            var previewUrl = LinkPreviewUrl;

            Derive(note);

            if (!HasSensitiveMedia) _isMediaRevealed = true;
            else if (!hadSensitive) _isMediaRevealed = false;

            if (!string.Equals(previewUrl, LinkPreviewUrl, StringComparison.Ordinal))
            {
                _linkPreview = null;
                _linkPreviewRequested = false;
            }

            _extraReactionEmojis = null;
            OnPropertyChanged("Note");
            OnPropertyChanged("AutomationName");
        }

        private void ApplyReacted(SocialNoteChange change)
        {
            var mine = IsMe(change.UserId);
            if (mine && SocialReactions.AreSame(_myReaction, change.Reaction)) return;

            if (mine && _myReaction != null) AdjustReaction(_myReaction, -1);
            AdjustReaction(change.Reaction, 1);
            if (mine) _myReaction = change.Reaction;

            if (!string.IsNullOrEmpty(change.EmojiName) && !string.IsNullOrEmpty(change.EmojiUrl)
                && change.EmojiName.IndexOf('@') > 0 && !change.EmojiName.EndsWith("@.", StringComparison.Ordinal))
            {
                if (_extraReactionEmojis == null) _extraReactionEmojis = new Dictionary<string, string>(StringComparer.Ordinal);
                _extraReactionEmojis[change.EmojiName] = change.EmojiUrl;
            }

            RaiseReactionsChanged();
        }

        private void ApplyUnreacted(SocialNoteChange change)
        {
            var mine = IsMe(change.UserId);
            if (mine && (_myReaction == null || !SocialReactions.AreSame(_myReaction, change.Reaction))) return;

            AdjustReaction(mine ? _myReaction : change.Reaction, -1);
            if (mine) _myReaction = null;

            RaiseReactionsChanged();
        }

        private void ApplyReplied(string childId)
        {
            if (!string.IsNullOrEmpty(childId))
            {
                if (_seenReplies.Contains(childId)) return;

                _seenReplies.Enqueue(childId);
                while (_seenReplies.Count > SeenReplyLimit) _seenReplies.Dequeue();
            }

            _repliesCount++;
            OnPropertyChanged("RepliesCount");
            OnPropertyChanged("AutomationName");
        }

        private void ApplyPollVoted(SocialNoteChange change)
        {
            if (_poll == null || change.ChoiceIndex >= _poll.Choices.Count) return;

            var mine = IsMe(change.UserId);
            if (mine && _poll.Choices[change.ChoiceIndex].IsVoted) return;

            _poll = _poll.WithVote(change.ChoiceIndex, mine);
            OnPropertyChanged("Poll");
        }

        private void AdjustReaction(string key, int delta)
        {
            if (string.IsNullOrEmpty(key)) return;

            var list = new List<SocialReactionCount>(_reactions);
            var found = false;
            for (var i = 0; i < list.Count; i++)
            {
                if (!SocialReactions.AreSame(list[i].Key, key)) continue;

                found = true;
                var count = list[i].Count + delta;
                if (count <= 0) list.RemoveAt(i);
                else list[i] = new SocialReactionCount(list[i].Key, count);
                break;
            }

            if (!found && delta > 0) list.Add(new SocialReactionCount(SocialReactions.Normalize(key), delta));

            SocialReactionCount.Sort(list);
            _reactions = list;
            _reactionCount = SocialReactionCount.Total(list);
        }

        private void RaiseReactionsChanged()
        {
            OnPropertyChanged("Reactions");
            OnPropertyChanged("AutomationName");
        }

        private static bool IsMe(string userId)
        {
            return !string.IsNullOrEmpty(userId)
                   && string.Equals(userId, SocialContentService.CurrentAccountId(), StringComparison.Ordinal);
        }

        public static List<SocialNoteItem> CreateAll(IReadOnlyList<SocialNote> notes, IReadOnlyDictionary<string, Uri> localEmojis, bool isPinned)
        {
            var items = new List<SocialNoteItem>();
            if (notes == null) return items;

            foreach (var note in notes)
            {
                var item = Create(note, localEmojis, isPinned);
                if (item != null) items.Add(item);
            }
            return items;
        }

        public static SocialNoteItem Create(SocialNote outer, IReadOnlyDictionary<string, Uri> localEmojis, bool isPinned)
        {
            return Create(outer, localEmojis, isPinned, 0, null);
        }

        public static SocialNoteItem Create(SocialNote outer, IReadOnlyDictionary<string, Uri> localEmojis, bool isPinned,
                                            int depth, string pagingId)
        {
            if (outer == null) return null;

            var note = outer.IsPureRenote ? outer.Renote : outer;
            if (note == null || note.User == null) return null;

            var item = new SocialNoteItem
            {
                PagingId = pagingId ?? outer.Id,
                Renoter = outer.IsPureRenote ? outer.User : null,
                IsPinned = isPinned,
                Depth = Math.Max(0, depth),
                _localEmojis = localEmojis
            };

            item.RenoterName = item.Renoter == null ? null : DisplayNameOf(item.Renoter);
            item.Derive(note);
            item._isMediaRevealed = !item.HasSensitiveMedia;
            item._isRenotedByMe = SocialNoteService.KnownRenoted(note.Id) == true
                                  || (item.Renoter != null && IsMe(item.Renoter.Id));

            SocialNoteHub.Register(item);
            return item;
        }

        private void Derive(SocialNote note)
        {
            var isLocal = string.IsNullOrWhiteSpace(note.User.Host);
            var body = note.IsHidden
                       ? (IReadOnlyList<MfmSegment>)new MfmSegment[0]
                       : MfmText.Parse(note.Text, note.Emojis, isLocal ? LocalEmojis : null, note.MentionHandles);

            Note = note;
            NameSegments = MfmText.ParseName(note.User.DisplayName, note.User.Emojis);
            AuthorName = DisplayNameOf(note.User);
            AuthorHandle = note.User.Handle;
            BodySegments = body;
            ContentWarning = note.IsHidden || string.IsNullOrWhiteSpace(note.ContentWarning) ? null : note.ContentWarning.Trim();
            IsReply = !string.IsNullOrEmpty(note.ReplyId);
            ReplyToUser = note.Reply == null ? null : note.Reply.User;
            TimeFull = RelativeTime.Full(note.CreatedAt);

            HasSensitiveMedia = false;
            foreach (var file in note.Files)
            {
                if (file.IsSensitive)
                {
                    HasSensitiveMedia = true;
                    break;
                }
            }

            var previewLink = MfmText.FirstPreviewLink(body);
            LinkPreviewUrl = previewLink == null ? null : previewLink.Url;

            Quote = null;
            QuoteNameSegments = null;
            QuoteBodySegments = null;
            QuoteTimeText = null;

            var quote = note.Renote;
            if (quote != null && quote.User != null && !note.IsHidden)
            {
                var quoteIsLocal = string.IsNullOrWhiteSpace(quote.User.Host);
                Quote = quote;
                QuoteNameSegments = MfmText.ParseName(quote.User.DisplayName, quote.User.Emojis);
                QuoteBodySegments = !string.IsNullOrWhiteSpace(quote.ContentWarning)
                                    ? new[] { new MfmSegment { Kind = MfmSegmentKind.Text, Text = quote.ContentWarning.Trim() } }
                                    : MfmText.Parse(quote.Text, quote.Emojis, quoteIsLocal ? LocalEmojis : null, quote.MentionHandles);
                QuoteTimeText = RelativeTime.Short(quote.CreatedAt, DateTimeOffset.Now);
            }

            VisibilityGlyph = null;
            VisibilityName = null;
            ApplyVisibility(note.Visibility);

            _reactions = note.Reactions;
            _myReaction = note.MyReaction;
            _reactionCount = Math.Max(note.ReactionCount, SocialReactionCount.Total(note.Reactions));
            _repliesCount = note.RepliesCount;
            _renoteCount = note.RenoteCount;
            _poll = note.IsHidden ? null : note.Poll;
            _timeText = RelativeTime.Short(note.CreatedAt, DateTimeOffset.Now);
        }

        private void ApplyVisibility(string visibility)
        {
            switch (visibility)
            {
                case HomeVisibility:
                    VisibilityGlyph = "";
                    VisibilityName = LocalizedStrings.Get("SocialVisibilityHome");
                    break;
                case FollowersVisibility:
                    VisibilityGlyph = "";
                    VisibilityName = LocalizedStrings.Get("SocialVisibilityFollowers");
                    break;
                case DirectVisibility:
                    VisibilityGlyph = "";
                    VisibilityName = LocalizedStrings.Get("SocialVisibilityDirect");
                    break;
            }
        }

        public static string DisplayNameOf(SocialUser user)
        {
            if (user == null) return LocalizedStrings.Get("SocialUnknownUser");

            var plain = MfmText.PlainName(user.Name);
            return string.IsNullOrWhiteSpace(plain) ? user.Username : plain;
        }

        private static string Excerpt(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var head = TextHelper.FirstTextElements(text, ExcerptTextElements);
            return head.Length < text.Length ? head + "…" : head;
        }
    }
}
