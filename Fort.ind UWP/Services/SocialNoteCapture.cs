using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace Fort.ind_UWP
{
    public sealed class SocialNoteWatch : IDisposable
    {
        private readonly HashSet<string> _ids = new HashSet<string>(StringComparer.Ordinal);

        private bool _disposed;

        internal SocialNoteWatch()
        {
        }

        public void Set(IEnumerable<string> ids)
        {
            if (_disposed) return;

            var next = new HashSet<string>(StringComparer.Ordinal);
            if (ids != null)
            {
                foreach (var id in ids)
                {
                    if (!string.IsNullOrEmpty(id)) next.Add(id);
                }
            }

            var added = new List<string>();
            foreach (var id in next)
            {
                if (!_ids.Contains(id)) added.Add(id);
            }

            var removed = new List<string>();
            foreach (var id in _ids)
            {
                if (!next.Contains(id)) removed.Add(id);
            }

            if (added.Count == 0 && removed.Count == 0) return;

            _ids.Clear();
            _ids.UnionWith(next);
            SocialNoteCapture.Change(added, removed);
        }

        public void Dispose()
        {
            if (_disposed) return;

            Set(null);
            _disposed = true;
        }
    }

    public static class SocialNoteCapture
    {
        private static readonly TimeSpan MinimumReconnectDelay = TimeSpan.FromSeconds(10);

        private static readonly TimeSpan MaximumReconnectDelay = TimeSpan.FromMinutes(5);

        private static readonly object s_lock = new object();

        private static readonly Dictionary<string, int> s_counts = new Dictionary<string, int>(StringComparer.Ordinal);

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

        public static SocialNoteWatch Watch()
        {
            return new SocialNoteWatch();
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
                wanted = s_counts.Count > 0;
            }

            if (wanted) StartStream();
        }

        internal static void Change(IReadOnlyList<string> added, IReadOnlyList<string> removed)
        {
            var subscribe = new List<string>();
            var unsubscribe = new List<string>();
            SocialStream stream;
            bool start, linger;
            int lingerGeneration = 0;

            lock (s_lock)
            {
                foreach (var id in added)
                {
                    int count;
                    s_counts.TryGetValue(id, out count);
                    s_counts[id] = count + 1;
                    if (count == 0) subscribe.Add(id);
                }

                foreach (var id in removed)
                {
                    int count;
                    if (!s_counts.TryGetValue(id, out count)) continue;

                    if (count <= 1)
                    {
                        s_counts.Remove(id);
                        unsubscribe.Add(id);
                    }
                    else
                    {
                        s_counts[id] = count - 1;
                    }
                }

                stream = s_connected ? s_stream : null;
                start = s_counts.Count > 0 && s_stream == null && !s_suspended;
                linger = s_counts.Count == 0 && s_stream != null;

                if (s_counts.Count > 0) s_lingerGeneration++;
                if (linger) lingerGeneration = ++s_lingerGeneration;
            }

            if (stream != null)
            {
                Send(stream, "subNote", subscribe);
                Send(stream, "unsubNote", unsubscribe);
            }

            if (start) StartStream();
            if (linger) CloseAfterLinger(lingerGeneration);
        }

        private static async void Send(SocialStream stream, string type, IReadOnlyList<string> ids)
        {
            try
            {
                foreach (var id in ids)
                {
                    JsonObject body = new JsonObject();
                    body.Add("id", JsonValue.CreateStringValue(id));
                    if (!await stream.SendAsync(type, body)) return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteCapture: could not send {type} - {ex.Message}");
            }
        }

        private static async void CloseAfterLinger(int generation)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(AppConstants.SocialNoteCaptureLingerSeconds));

                lock (s_lock)
                {
                    if (generation != s_lingerGeneration || s_counts.Count > 0) return;
                }

                StopStream();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteCapture: could not close the idle stream - {ex.Message}");
            }
        }

        private static void StartStream()
        {
            int generation;
            lock (s_lock)
            {
                if (s_stream != null || s_suspended || s_counts.Count == 0) return;
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
            }

            if (stream == null) return;

            stream.NoteUpdated -= OnNoteUpdated;
            stream.Closed -= OnStreamClosed;
            stream.Dispose();
        }

        private static async void Connect(int generation)
        {
            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                if (string.IsNullOrEmpty(token)) return;

                var stream = new SocialStream(null);
                lock (s_lock)
                {
                    if (generation != s_generation || s_stream != null || s_suspended || s_counts.Count == 0) return;
                    s_stream = stream;
                }

                stream.NoteUpdated += OnNoteUpdated;
                stream.Closed += OnStreamClosed;

                if (!await stream.ConnectAsync(token, CancellationToken.None)) return;

                List<string> ids;
                lock (s_lock)
                {
                    if (!ReferenceEquals(s_stream, stream)) return;

                    s_connected = true;
                    s_reconnectDelay = MinimumReconnectDelay;
                    ids = new List<string>(s_counts.Keys);
                }

                Send(stream, "subNote", ids);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteCapture: connect failed - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void OnStreamClosed(object sender, EventArgs e)
        {
            var stream = sender as SocialStream;
            if (stream == null) return;

            stream.NoteUpdated -= OnNoteUpdated;
            stream.Closed -= OnStreamClosed;

            int generation;
            TimeSpan delay;
            lock (s_lock)
            {
                if (!ReferenceEquals(s_stream, stream)) return;

                s_stream = null;
                s_connected = false;
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
                Debug.WriteLine($"SocialNoteCapture: reconnect failed - {ex.Message}");
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
                    wanted = s_counts.Count > 0;
                }

                StopStream();
                if (wanted && account != null) StartStream();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteCapture: account change handling failed - {ex.Message}");
            }
        }

        private static void OnNoteUpdated(object sender, SocialNoteStreamEventArgs e)
        {
            try
            {
                if (string.Equals(e.Type, "updated", StringComparison.Ordinal))
                {
                    RefreshNote(e.NoteId);
                    return;
                }

                SocialNoteService.Raise(SocialNoteChange.FromStream(e.NoteId, e.Type, e.Body));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteCapture: could not handle a note update - {ex.Message}");
            }
        }

        private static async void RefreshNote(string noteId)
        {
            try
            {
                var result = await SocialContentService.FetchNoteAsync(noteId, CancellationToken.None);
                if (result.Status == SocialApiStatus.Ok)
                {
                    SocialNoteService.Raise(SocialNoteChange.Snapshot(result.Value));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteCapture: could not refresh an edited note - {ex.Message}");
            }
        }
    }
}
