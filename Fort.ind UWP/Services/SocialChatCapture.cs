using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Fort.ind_UWP
{
    public sealed class SocialChatWatch : IDisposable
    {
        private string _otherUserId;

        private bool _disposed;

        internal SocialChatWatch()
        {
        }

        public void Set(string otherUserId)
        {
            if (_disposed) return;
            if (string.IsNullOrEmpty(otherUserId)) otherUserId = null;
            if (string.Equals(_otherUserId, otherUserId, StringComparison.Ordinal)) return;

            var previous = _otherUserId;
            _otherUserId = otherUserId;
            SocialChatCapture.Change(otherUserId, previous);
        }

        public void MarkRead()
        {
            if (!_disposed && _otherUserId != null) SocialChatCapture.MarkRead(_otherUserId);
        }

        public void Rejoin()
        {
            if (!_disposed && _otherUserId != null) SocialChatCapture.Rejoin(_otherUserId);
        }

        public void Dispose()
        {
            if (_disposed) return;

            Set(null);
            _disposed = true;
            SocialChatCapture.Release();
        }
    }

    public static class SocialChatCapture
    {
        private const string MainChannel = "main";

        private const string ChatUserChannel = "chatUser";

        private static readonly TimeSpan MinimumReconnectDelay = TimeSpan.FromSeconds(10);

        private static readonly TimeSpan MaximumReconnectDelay = TimeSpan.FromMinutes(5);

        private static readonly object s_lock = new object();

        private static readonly Dictionary<string, int> s_counts = new Dictionary<string, int>(StringComparer.Ordinal);

        private static readonly Dictionary<string, string> s_channelIds = new Dictionary<string, string>(StringComparer.Ordinal);

        private static readonly Dictionary<string, string> s_channelOwners = new Dictionary<string, string>(StringComparer.Ordinal);

        private static string s_mainChannelId;

        private static int s_watchers;

        private static SocialStream s_stream;

        private static bool s_connected;

        private static int s_generation;

        private static int s_lingerGeneration;

        private static bool s_suspended;

        private static bool s_initialized;

        private static string s_accountId;

        private static TimeSpan s_reconnectDelay = MinimumReconnectDelay;

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

        public static SocialChatWatch Watch()
        {
            bool start;
            lock (s_lock)
            {
                s_watchers++;
                s_lingerGeneration++;
                start = s_stream == null && !s_suspended;
            }

            if (start) StartStream();
            return new SocialChatWatch();
        }

        public static void OnSuspending()
        {
            lock (s_lock)
            {
                s_suspended = true;
            }

            StopStream();
        }

        public static void OnResuming()
        {
            bool wanted;
            lock (s_lock)
            {
                s_suspended = false;
                wanted = s_watchers > 0;
            }

            if (wanted) StartStream();
        }

        internal static void Release()
        {
            bool linger;
            int generation = 0;
            lock (s_lock)
            {
                if (s_watchers > 0) s_watchers--;
                linger = s_watchers == 0 && s_stream != null;
                if (linger) generation = ++s_lingerGeneration;
            }

            if (linger) CloseAfterLinger(generation);
        }

        internal static void Change(string added, string removed)
        {
            SocialStream stream;
            string join = null;
            string leaveChannel = null;

            lock (s_lock)
            {
                stream = s_connected ? s_stream : null;

                if (added != null)
                {
                    int count;
                    s_counts.TryGetValue(added, out count);
                    s_counts[added] = count + 1;
                    if (count == 0) join = added;
                }

                if (removed != null)
                {
                    int count;
                    if (s_counts.TryGetValue(removed, out count))
                    {
                        if (count <= 1)
                        {
                            s_counts.Remove(removed);
                            string channelId;
                            if (s_channelIds.TryGetValue(removed, out channelId))
                            {
                                s_channelIds.Remove(removed);
                                s_channelOwners.Remove(channelId);
                                leaveChannel = channelId;
                            }
                        }
                        else
                        {
                            s_counts[removed] = count - 1;
                        }
                    }
                }
            }

            if (stream == null) return;

            if (join != null) JoinConversation(stream, join);
            if (leaveChannel != null) Disconnect(stream, leaveChannel);
        }

        internal static void Rejoin(string otherUserId)
        {
            SocialStream stream;
            lock (s_lock)
            {
                stream = s_connected ? s_stream : null;
                if (stream == null || !s_counts.ContainsKey(otherUserId)) return;
            }

            JoinConversation(stream, otherUserId);
        }

        internal static void MarkRead(string otherUserId)
        {
            SocialStream stream;
            string channelId;
            lock (s_lock)
            {
                stream = s_connected ? s_stream : null;
                if (stream == null || !s_channelIds.TryGetValue(otherUserId, out channelId)) return;
            }

            JObject body = new JObject();
            body.Add("id", new JValue(channelId));
            body.Add("type", new JValue("read"));
            body.Add("body", new JObject());
            Send(stream, "ch", body);
        }

        private static void JoinConversation(SocialStream stream, string otherUserId)
        {
            string channelId;
            lock (s_lock)
            {
                if (!s_channelIds.TryGetValue(otherUserId, out channelId))
                {
                    channelId = Guid.NewGuid().ToString("N");
                    s_channelIds[otherUserId] = channelId;
                    s_channelOwners[channelId] = otherUserId;
                }
            }

            JObject parameters = new JObject();
            parameters.Add("otherId", new JValue(otherUserId));

            JObject body = new JObject();
            body.Add("channel", new JValue(ChatUserChannel));
            body.Add("id", new JValue(channelId));
            body.Add("params", parameters);
            Send(stream, "connect", body);
        }

        private static void Disconnect(SocialStream stream, string channelId)
        {
            JObject body = new JObject();
            body.Add("id", new JValue(channelId));
            Send(stream, "disconnect", body);
        }

        private static async void Send(SocialStream stream, string type, JObject body)
        {
            try
            {
                await stream.SendAsync(type, body);
            }
            catch (Exception ex)
            {
                AppLog.Error($"SocialChatCapture: could not send {type}", ex);
            }
        }

        private static async void CloseAfterLinger(int generation)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(AppConstants.SocialChatCaptureLingerSeconds));

                lock (s_lock)
                {
                    if (generation != s_lingerGeneration || s_watchers > 0) return;
                }

                StopStream();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatCapture: could not close the idle stream", ex);
            }
        }

        private static void StartStream()
        {
            int generation;
            lock (s_lock)
            {
                if (s_stream != null || s_suspended || s_watchers == 0) return;
                generation = ++s_generation;
            }

            Connect(generation);
        }

        private static void StopStream()
        {
            SocialStream stream;
            lock (s_lock)
            {
                stream = s_stream;
                s_stream = null;
                s_connected = false;
                s_generation++;
                s_reconnectDelay = MinimumReconnectDelay;
                s_channelIds.Clear();
                s_channelOwners.Clear();
                s_mainChannelId = null;
            }

            if (stream == null) return;

            stream.ChannelMessageReceived -= OnChannelMessage;
            stream.Closed -= OnStreamClosed;
            stream.Dispose();
        }

        private static async void Connect(int generation)
        {
            try
            {
                if (!LabsService.ChatEnabled || !SocialPermissions.Has(SocialPermissions.ReadChat)) return;

                var token = await MisskeyAuthService.TryGetTokenAsync();
                if (string.IsNullOrEmpty(token)) return;

                var stream = new SocialStream(null);
                lock (s_lock)
                {
                    if (generation != s_generation || s_stream != null || s_suspended || s_watchers == 0) return;
                    s_stream = stream;
                }

                stream.ChannelMessageReceived += OnChannelMessage;
                stream.Closed += OnStreamClosed;

                if (!await stream.ConnectAsync(token, CancellationToken.None)) return;

                List<string> others;
                string mainId = Guid.NewGuid().ToString("N");
                lock (s_lock)
                {
                    if (!ReferenceEquals(s_stream, stream)) return;

                    s_connected = true;
                    s_reconnectDelay = MinimumReconnectDelay;
                    s_mainChannelId = mainId;
                    s_channelIds.Clear();
                    s_channelOwners.Clear();
                    others = new List<string>(s_counts.Keys);
                }

                JObject main = new JObject();
                main.Add("channel", new JValue(MainChannel));
                main.Add("id", new JValue(mainId));
                Send(stream, "connect", main);

                foreach (var other in others)
                {
                    JoinConversation(stream, other);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatCapture: connect failed", ex);
            }
        }

        private static void OnStreamClosed(object sender, EventArgs e)
        {
            var stream = sender as SocialStream;
            if (stream == null) return;

            stream.ChannelMessageReceived -= OnChannelMessage;
            stream.Closed -= OnStreamClosed;

            int generation;
            TimeSpan delay;
            lock (s_lock)
            {
                if (!ReferenceEquals(s_stream, stream)) return;

                s_stream = null;
                s_connected = false;
                s_channelIds.Clear();
                s_channelOwners.Clear();
                s_mainChannelId = null;
                generation = s_generation;

                delay = s_reconnectDelay;
                var doubled = TimeSpan.FromTicks(s_reconnectDelay.Ticks * 2);
                s_reconnectDelay = doubled > MaximumReconnectDelay ? MaximumReconnectDelay : doubled;
            }

            if (stream.Unauthorized) return;

            ReconnectAfter(delay, generation);
        }

        private static async void ReconnectAfter(TimeSpan delay, int generation)
        {
            try
            {
                await Task.Delay(delay);

                lock (s_lock)
                {
                    if (generation != s_generation || s_stream != null) return;
                }

                StartStream();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatCapture: reconnect failed", ex);
            }
        }

        private static void OnAuthStateChanged(object sender, bool isSignedIn)
        {
            try
            {
                var account = SocialContentService.CurrentAccountId();
                bool wanted;
                lock (s_lock)
                {
                    if (string.Equals(account, s_accountId, StringComparison.Ordinal)) return;

                    s_accountId = account;
                    wanted = s_watchers > 0;
                }

                StopStream();
                if (wanted && account != null) StartStream();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatCapture: account change handling failed", ex);
            }
        }

        private static void OnChannelMessage(object sender, SocialChannelEventArgs e)
        {
            try
            {
                string other;
                bool isMain;
                lock (s_lock)
                {
                    isMain = string.Equals(e.ChannelId, s_mainChannelId, StringComparison.Ordinal);
                    s_channelOwners.TryGetValue(e.ChannelId, out other);
                }

                if (isMain)
                {
                    if (string.Equals(e.Type, "newChatMessage", StringComparison.Ordinal))
                    {
                        SocialChatService.ApplyFromMainChannel(SocialChatMessage.FromJson(e.Body as JObject));
                    }
                    return;
                }

                if (other == null) return;

                switch (e.Type)
                {
                    case "message":
                        SocialChatService.ApplyIncoming(SocialChatMessage.FromJson(e.Body as JObject));
                        break;
                    case "deleted":
                        if (e.Body != null && e.Body.Type == JTokenType.String) SocialChatService.ApplyDeleted(other, (string)e.Body);
                        break;
                    case "react":
                    case "unreact":
                        var reaction = e.Body as JObject;
                        SocialChatService.ApplyReaction(other,
                                                        SocialJson.String(reaction, "messageId"),
                                                        SocialJson.String(reaction, "reaction"),
                                                        string.Equals(e.Type, "react", StringComparison.Ordinal));
                        break;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialChatCapture: could not handle a chat event", ex);
            }
        }
    }
}
