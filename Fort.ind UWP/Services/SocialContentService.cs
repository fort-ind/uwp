using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Globalization;

namespace Fort.ind_UWP
{
    public static class SocialContentService
    {
        private static readonly object s_lock = new object();

        private static readonly IReadOnlyDictionary<string, Uri> s_noEmojis = new Dictionary<string, Uri>(StringComparer.Ordinal);

        private static readonly Dictionary<string, Task<SocialLinkPreview>> s_previews =
            new Dictionary<string, Task<SocialLinkPreview>>(StringComparer.Ordinal);

        private static readonly Queue<string> s_previewOrder = new Queue<string>();

        private static readonly SemaphoreSlim s_previewGate = new SemaphoreSlim(AppConstants.SocialLinkPreviewConcurrency);

        private static bool s_initialized;

        private static string s_accountId;

        private static int s_generation;

        private static Task<IReadOnlyDictionary<string, Uri>> s_emojiTask;

        private static volatile IReadOnlyDictionary<string, Uri> s_emojiMap;

        private static volatile IReadOnlyList<SocialCustomEmoji> s_customEmojis;

        public static void Initialize()
        {
            lock (s_lock)
            {
                if (s_initialized) return;
                s_initialized = true;
                s_accountId = CurrentAccountId();
            }

            ProfileService.AuthStateChanged += OnAuthStateChanged;
        }

        public static string CurrentAccountId()
        {
            var user = ProfileService.CurrentUser;
            return user == null ? null : user.UserId;
        }

        public static bool IsCurrentAccount(string accountId)
        {
            return !string.IsNullOrEmpty(accountId) && string.Equals(accountId, CurrentAccountId(), StringComparison.Ordinal);
        }

        private static void OnAuthStateChanged(object sender, bool isSignedIn)
        {
            try
            {
                var account = CurrentAccountId();
                bool purge;
                lock (s_lock)
                {
                    purge = s_accountId != null && !string.Equals(s_accountId, account, StringComparison.Ordinal);
                    s_accountId = account;

                    if (purge)
                    {
                        s_generation++;
                        s_emojiTask = null;
                        s_emojiMap = null;
                        s_customEmojis = null;
                        s_previews.Clear();
                        s_previewOrder.Clear();
                    }
                }

                if (purge) CloseAccountWindows();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialContentService: account change handling failed", ex);
            }
        }

        private static async void CloseAccountWindows()
        {
            try
            {
                await WindowManagerService.CloseAccountScopedWindowsAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialContentService: could not close the account's windows", ex);
            }
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNote>>> FetchTimelineAsync(
            SocialTimeline timeline, string untilId, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            return await SocialApiService.GetTimelineAsync(token, timeline, untilId, AppConstants.SocialFeedPageSize, cancellationToken);
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNote>>> FetchTagNotesAsync(
            string tag, string untilId, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            return await SocialApiService.GetTagNotesAsync(token, tag, untilId, AppConstants.SocialFeedPageSize, cancellationToken);
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNote>>> FetchUserNotesAsync(
            string userId, SocialUserNotesTab tab, string untilId, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            return await SocialApiService.GetUserNotesAsync(token, userId, tab, untilId, AppConstants.SocialFeedPageSize, cancellationToken);
        }

        public static async Task<SocialApiResult<SocialUserDetail>> FetchUserAsync(string userId, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            return await SocialApiService.GetUserAsync(token, userId, cancellationToken);
        }

        public static async Task<SocialApiResult<SocialUserDetail>> FetchUserByHandleAsync(
            string username, string host, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            return await SocialApiService.GetUserByHandleAsync(token, username, host, cancellationToken);
        }

        public static async Task<SocialApiResult<SocialNote>> FetchNoteAsync(string noteId, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            return await SocialApiService.GetNoteAsync(token, noteId, cancellationToken);
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNote>>> FetchChildrenAsync(
            string noteId, string sinceId, int limit, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            return await SocialApiService.GetChildrenAsync(token, noteId, sinceId, limit, cancellationToken);
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNote>>> FetchConversationAsync(
            string noteId, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            return await SocialApiService.GetConversationAsync(token, noteId, AppConstants.SocialConversationLimit, cancellationToken);
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNote>>> FetchRenotesAsync(
            string noteId, string untilId, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            return await SocialApiService.GetRenotesAsync(token, noteId, untilId, AppConstants.SocialReactionsTabPageSize, cancellationToken);
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialReactionEntry>>> FetchReactionsAsync(
            string noteId, string type, string untilId, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            return await SocialApiService.GetReactionsAsync(token, noteId, type, untilId, AppConstants.SocialReactionsTabPageSize, cancellationToken);
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialFavoriteEntry>>> FetchFavoritesAsync(
            string untilId, CancellationToken cancellationToken)
        {
            var token = await MisskeyAuthService.TryGetTokenAsync();
            return await SocialApiService.GetFavoritesAsync(token, untilId, AppConstants.SocialFeedPageSize, cancellationToken);
        }

        public static IReadOnlyDictionary<string, Uri> CachedEmojiMap
        {
            get { return s_emojiMap ?? s_noEmojis; }
        }

        public static async Task<IReadOnlyList<SocialCustomEmoji>> GetCustomEmojisAsync()
        {
            await GetEmojiMapAsync();
            return s_customEmojis ?? (IReadOnlyList<SocialCustomEmoji>)new SocialCustomEmoji[0];
        }

        public static Task<IReadOnlyDictionary<string, Uri>> GetEmojiMapAsync()
        {
            lock (s_lock)
            {
                var map = s_emojiMap;
                if (map != null) return Task.FromResult(map);

                if (s_emojiTask == null)
                {
                    s_emojiTask = LoadEmojiMapAsync(s_generation);
                }
                return s_emojiTask;
            }
        }

        private static async Task<IReadOnlyDictionary<string, Uri>> LoadEmojiMapAsync(int generation)
        {
            try
            {
                var token = await MisskeyAuthService.TryGetTokenAsync();
                var result = await SocialApiService.GetEmojisAsync(token, CancellationToken.None);
                if (result.Status != SocialApiStatus.Ok)
                {
                    ForgetEmojiTask(generation);
                    return s_noEmojis;
                }

                var map = new Dictionary<string, Uri>(StringComparer.Ordinal);
                var custom = new List<SocialCustomEmoji>(result.Value.Count);
                foreach (var emoji in result.Value)
                {
                    var uri = SocialLinks.StaticEmojiUri(emoji.Url);
                    if (uri == null) continue;

                    map[emoji.Name] = uri;
                    custom.Add(emoji);
                }

                lock (s_lock)
                {
                    if (generation == s_generation)
                    {
                        s_customEmojis = custom;
                        s_emojiMap = map;
                    }
                }
                return map;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialContentService: could not load the emoji list", ex);
                ForgetEmojiTask(generation);
                return s_noEmojis;
            }
        }

        private static void ForgetEmojiTask(int generation)
        {
            lock (s_lock)
            {
                if (generation == s_generation) s_emojiTask = null;
            }
        }

        public static void TrimCaches()
        {
            lock (s_lock)
            {
                s_previews.Clear();
                s_previewOrder.Clear();
            }
        }

        public static Task<SocialLinkPreview> GetLinkPreviewAsync(string url)
        {
            if (WebLauncher.TryCreateWebUri(url) == null) return Task.FromResult<SocialLinkPreview>(null);

            TaskCompletionSource<SocialLinkPreview> pending;
            lock (s_lock)
            {
                Task<SocialLinkPreview> existing;
                if (s_previews.TryGetValue(url, out existing)) return existing;

                pending = new TaskCompletionSource<SocialLinkPreview>();
                s_previews[url] = pending.Task;
                s_previewOrder.Enqueue(url);
                while (s_previewOrder.Count > AppConstants.SocialLinkPreviewCacheLimit)
                {
                    s_previews.Remove(s_previewOrder.Dequeue());
                }
            }

            LoadPreview(url, pending);
            return pending.Task;
        }

        private static async void LoadPreview(string url, TaskCompletionSource<SocialLinkPreview> pending)
        {
            SocialLinkPreview preview = null;
            try
            {
                await s_previewGate.WaitAsync();
                try
                {
                    var result = await SocialApiService.GetUrlPreviewAsync(url, PreviewLanguage(), CancellationToken.None);
                    if (result.Status == SocialApiStatus.Ok)
                    {
                        preview = result.Value;
                    }
                    else if (result.Status != SocialApiStatus.Refused)
                    {
                        ForgetPreview(url, pending.Task);
                    }
                }
                finally
                {
                    s_previewGate.Release();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialContentService: link preview failed", ex);
                ForgetPreview(url, pending.Task);
            }

            pending.TrySetResult(preview);
        }

        private static void ForgetPreview(string url, Task<SocialLinkPreview> task)
        {
            lock (s_lock)
            {
                Task<SocialLinkPreview> current;
                if (s_previews.TryGetValue(url, out current) && ReferenceEquals(current, task))
                {
                    s_previews.Remove(url);
                }
            }
        }

        private static string PreviewLanguage()
        {
            try
            {
                var languages = ApplicationLanguages.Languages;
                return languages.Count > 0 ? languages[0] : null;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialContentService: could not read the app language", ex);
                return null;
            }
        }
    }
}
