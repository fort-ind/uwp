using System;
using System.Collections.Generic;
using Windows.Globalization.DateTimeFormatting;
using Windows.UI.Xaml;

namespace Fort.ind_UWP
{
    public enum SocialChatSendState
    {
        Sent,
        Sending,
        Failed
    }

    public sealed class SocialChatMessageItem : BindableBase
    {
        [ThreadStatic]
        private static DateTimeFormatter t_timeFormatter;

        [ThreadStatic]
        private static DateTimeFormatter t_dateFormatter;

        private static DateTimeFormatter TimeFormatter
        {
            get { return t_timeFormatter ?? (t_timeFormatter = new DateTimeFormatter("shorttime")); }
        }

        private static DateTimeFormatter DateFormatter
        {
            get { return t_dateFormatter ?? (t_dateFormatter = new DateTimeFormatter("longdate")); }
        }

        private SocialChatMessage _message;

        private SocialChatSendState _state;

        private string _failureText;

        private bool _startsDay;

        private bool _startsGroup;

        private bool _endsGroup;

        private bool _revealed;

        private readonly DateTimeOffset _queuedAt = DateTimeOffset.Now;

        private SocialChatMessageItem(SocialChatMessage message, bool isMine, SocialUser sender, string pendingText,
                                      SocialDriveFile pendingFile, IReadOnlyDictionary<string, Uri> localEmojis)
        {
            _message = message;
            IsMine = isMine;
            Sender = sender;
            LocalEmojis = localEmojis;
            LocalId = Guid.NewGuid().ToString("N");
            _state = message == null ? SocialChatSendState.Sending : SocialChatSendState.Sent;
            PendingText = pendingText;
            PendingFile = pendingFile;
            Segments = MfmText.Parse(message == null ? pendingText : message.Text, null, localEmojis, null);
        }

        public static SocialChatMessageItem Create(SocialChatMessage message, string account, SocialUser other,
                                                   IReadOnlyDictionary<string, Uri> localEmojis)
        {
            var mine = message.IsFrom(account);
            return new SocialChatMessageItem(message, mine, mine ? null : (message.FromUser ?? other), null, null, localEmojis);
        }

        public static SocialChatMessageItem CreatePending(string text, SocialDriveFile file, IReadOnlyDictionary<string, Uri> localEmojis)
        {
            return new SocialChatMessageItem(null, true, null, text, file, localEmojis);
        }

        public event EventHandler RetryRequested;

        public event EventHandler<FrameworkElement> ReactRequested;

        public event EventHandler<string> UnreactRequested;

        public SocialDriveFile PendingFile { get; private set; }

        public bool CanReact
        {
            get { return !IsMine && _state == SocialChatSendState.Sent && _message != null; }
        }

        public void RequestReact(FrameworkElement anchor)
        {
            if (CanReact) ReactRequested?.Invoke(this, anchor);
        }

        public void RequestUnreact(string key)
        {
            if (CanReact && !string.IsNullOrEmpty(key)) UnreactRequested?.Invoke(this, key);
        }

        public string LocalId { get; private set; }

        public void RequestRetry()
        {
            if (_state != SocialChatSendState.Failed) return;

            RetryRequested?.Invoke(this, EventArgs.Empty);
        }

        public SocialChatMessage Message
        {
            get { return _message; }
        }

        public string Id
        {
            get { return _message == null ? null : _message.Id; }
        }

        public DateTimeOffset CreatedAt
        {
            get { return _message == null ? _queuedAt : _message.CreatedAt; }
        }

        public bool IsMine { get; private set; }

        public SocialUser Sender { get; private set; }

        public IReadOnlyDictionary<string, Uri> LocalEmojis { get; private set; }

        public string PendingText { get; private set; }

        public IReadOnlyList<MfmSegment> Segments { get; private set; }

        public string PlainText
        {
            get { return MfmText.PlainText(Segments); }
        }

        public SocialDriveFile File
        {
            get { return _message == null ? PendingFile : _message.File; }
        }

        public IReadOnlyList<SocialReactionCount> Reactions
        {
            get { return _message == null ? new SocialReactionCount[0] : _message.Reactions; }
        }

        public SocialChatSendState State
        {
            get { return _state; }
        }

        public string FailureText
        {
            get { return _failureText; }
        }

        public bool StartsDay
        {
            get { return _startsDay; }
        }

        public bool StartsGroup
        {
            get { return _startsGroup; }
        }

        public bool EndsGroup
        {
            get { return _endsGroup; }
        }

        public bool IsRevealed
        {
            get { return _revealed; }
            set { SetProperty(ref _revealed, value); }
        }

        public string DayText
        {
            get
            {
                var local = CreatedAt.ToLocalTime().Date;
                var today = DateTimeOffset.Now.Date;
                if (local == today) return LocalizedStrings.Get("SocialChatDayToday");
                if (local == today.AddDays(-1)) return LocalizedStrings.Get("SocialChatDayYesterday");

                return DateFormatter.Format(CreatedAt);
            }
        }

        public string TimeText
        {
            get
            {
                return TimeFormatter.Format(CreatedAt);
            }
        }

        public string AutomationName
        {
            get
            {
                var who = IsMine ? LocalizedStrings.Get("SocialChatYou")
                                 : MfmText.PlainName(Sender == null ? "" : SocialNoteItem.DisplayNameOf(Sender));
                var text = PlainText;
                if (string.IsNullOrWhiteSpace(text) && File != null) text = LocalizedStrings.Get("SocialChatAttachmentPreview");

                switch (_state)
                {
                    case SocialChatSendState.Sending:
                        return LocalizedStrings.Format("SocialChatBubbleSendingAutomationFormat", text);
                    case SocialChatSendState.Failed:
                        return LocalizedStrings.Format("SocialChatBubbleFailedAutomationFormat", text, _failureText ?? "");
                    default:
                        return LocalizedStrings.Format("SocialChatBubbleAutomationFormat", who, TimeText, text);
                }
            }
        }

        public bool IsSameSenderAs(SocialChatMessageItem other)
        {
            if (other == null || other.IsMine != IsMine) return false;
            if (IsMine) return true;

            var mineFrom = _message == null ? null : _message.FromUserId;
            var theirsFrom = other._message == null ? null : other._message.FromUserId;
            return string.Equals(mineFrom, theirsFrom, StringComparison.Ordinal);
        }

        internal bool SetLayout(bool startsDay, bool startsGroup, bool endsGroup)
        {
            if (_startsDay == startsDay && _startsGroup == startsGroup && _endsGroup == endsGroup) return false;

            _startsDay = startsDay;
            _startsGroup = startsGroup;
            _endsGroup = endsGroup;
            OnPropertyChanged("Layout");
            return true;
        }

        internal void SetSender(SocialUser user)
        {
            if (IsMine || user == null || Sender != null) return;

            Sender = user;
            OnPropertyChanged("Layout");
            OnPropertyChanged("AutomationName");
        }

        internal void MarkSent(SocialChatMessage message)
        {
            _message = message;
            _state = SocialChatSendState.Sent;
            _failureText = null;
            Segments = MfmText.Parse(message.Text, null, LocalEmojis, null);
            OnPropertyChanged("Message");
            OnPropertyChanged("State");
            OnPropertyChanged("AutomationName");
        }

        internal void MarkSending()
        {
            _state = SocialChatSendState.Sending;
            _failureText = null;
            OnPropertyChanged("State");
            OnPropertyChanged("AutomationName");
        }

        internal void MarkFailed(string reason)
        {
            _state = SocialChatSendState.Failed;
            _failureText = reason;
            OnPropertyChanged("State");
            OnPropertyChanged("AutomationName");
        }

        internal void Replace(SocialChatMessage message)
        {
            if (message == null) return;

            _message = message;
            OnPropertyChanged("Message");
            OnPropertyChanged("AutomationName");
        }
    }
}
