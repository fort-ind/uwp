using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;

namespace Fort.ind_UWP
{
    public static class SocialDraftService
    {
        public const string NewSlot = "new";

        private const string EditSlotPrefix = "edit:";

        private static readonly object s_lock = new object();

        private static readonly SemaphoreSlim s_fileGate = new SemaphoreSlim(1, 1);

        private static readonly Debouncer s_saveDebounce = new Debouncer();

        private static readonly Dictionary<string, SocialComposeDraft> s_replies = new Dictionary<string, SocialComposeDraft>(StringComparer.Ordinal);

        private static JsonObject s_root = new JsonObject();

        private static volatile bool s_loaded;

        private static bool s_dirty;

        private static bool s_saveBlocked;

        public static string EditSlot(string noteId)
        {
            return EditSlotPrefix + noteId;
        }

        public static async Task<SocialComposeDraft> LoadAsync(string account, string slot)
        {
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(slot)) return null;

            await EnsureLoadedAsync();

            lock (s_lock)
            {
                var drafts = SocialJson.Object(s_root, account);
                return SocialComposeDraft.FromJson(SocialJson.Object(drafts, slot));
            }
        }

        public static void Save(string account, string slot, SocialComposeDraft draft)
        {
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(slot)) return;

            if (draft == null || draft.IsEmpty)
            {
                Remove(account, slot);
                return;
            }

            lock (s_lock)
            {
                var drafts = SocialJson.Object(s_root, account);
                if (drafts == null)
                {
                    drafts = new JsonObject();
                    s_root.SetNamedValue(account, drafts);
                }

                drafts.SetNamedValue(slot, draft.ToJson());
                s_dirty = true;
            }

            ScheduleWrite();
        }

        public static void Remove(string account, string slot)
        {
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(slot)) return;

            lock (s_lock)
            {
                var drafts = SocialJson.Object(s_root, account);
                if (drafts == null || !drafts.ContainsKey(slot)) return;

                drafts.Remove(slot);
                if (drafts.Count == 0) s_root.Remove(account);
                s_dirty = true;
            }

            ScheduleWrite();
        }

        public static async Task ForgetAccountAsync(string account)
        {
            if (string.IsNullOrEmpty(account)) return;

            await EnsureLoadedAsync();

            lock (s_lock)
            {
                var prefix = account + "|";
                var replies = s_replies.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList();
                foreach (var key in replies)
                {
                    s_replies.Remove(key);
                }

                if (!s_root.ContainsKey(account)) return;

                s_root.Remove(account);
                s_dirty = true;
            }

            await FlushAsync();
        }

        public static void ResetForAppDataWipe()
        {
            lock (s_lock)
            {
                s_saveDebounce.Cancel();
                s_root = new JsonObject();
                s_replies.Clear();
                s_dirty = false;
                s_saveBlocked = false;
                s_loaded = true;
            }
        }

        public static SocialComposeDraft ReplyDraft(string account, string noteId)
        {
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(noteId)) return null;

            lock (s_lock)
            {
                SocialComposeDraft draft;
                return s_replies.TryGetValue(account + "|" + noteId, out draft) ? draft : null;
            }
        }

        public static void SaveReplyDraft(string account, string noteId, SocialComposeDraft draft)
        {
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(noteId)) return;

            lock (s_lock)
            {
                var key = account + "|" + noteId;
                if (draft == null || draft.IsEmpty) s_replies.Remove(key);
                else s_replies[key] = draft;
            }
        }

        public static async Task FlushAsync()
        {
            lock (s_lock)
            {
                s_saveDebounce.Cancel();
            }

            await WriteAsync();
        }

        private static async void ScheduleWrite()
        {
            try
            {
                CancellationToken token;
                lock (s_lock)
                {
                    token = s_saveDebounce.Restart();
                }

                await Task.Delay(AppConstants.SocialDraftSaveDelayMilliseconds);
                if (token.IsCancellationRequested) return;

                await WriteAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialDraftService: scheduled save failed - {ex.Message}");
            }
        }

        private static async Task EnsureLoadedAsync()
        {
            if (s_loaded) return;

            await s_fileGate.WaitAsync();
            StorageFile file = null;
            try
            {
                if (s_loaded) return;

                file = await ApplicationData.Current.LocalFolder.TryGetItemAsync(AppConstants.SocialDraftsFileName) as StorageFile;
                if (file != null)
                {
                    var text = await FileIO.ReadTextAsync(file);
                    var parsed = JsonObject.Parse(text);
                    lock (s_lock)
                    {
                        foreach (var pair in s_root)
                        {
                            parsed.SetNamedValue(pair.Key, pair.Value);
                        }
                        s_root = parsed;
                    }
                }

                s_loaded = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialDraftService: could not read the drafts - {ex.GetType().Name}: {ex.Message}");
                if (file != null) s_saveBlocked = !await TryMoveAsideAsync(file);
                s_loaded = true;
            }
            finally
            {
                s_fileGate.Release();
            }
        }

        private static async Task<bool> TryMoveAsideAsync(StorageFile file)
        {
            try
            {
                await file.RenameAsync(AppConstants.SocialDraftsFileName + ".bak", NameCollisionOption.ReplaceExisting);
                Debug.WriteLine("SocialDraftService: moved the unreadable drafts file aside");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialDraftService: could not move the unreadable drafts file aside - {ex.Message}");
                return false;
            }
        }

        private static async Task WriteAsync()
        {
            await EnsureLoadedAsync();

            await s_fileGate.WaitAsync();
            try
            {
                string json;
                lock (s_lock)
                {
                    if (!s_dirty) return;
                    s_dirty = false;
                    json = s_root.Stringify();
                }

                if (s_saveBlocked)
                {
                    Debug.WriteLine("SocialDraftService: not saving - the existing drafts file could not be read or moved aside");
                    return;
                }

                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(AppConstants.SocialDraftsFileName,
                                                                                    CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialDraftService: could not save the drafts - {ex.GetType().Name}: {ex.Message}");
                lock (s_lock)
                {
                    s_dirty = true;
                }
            }
            finally
            {
                s_fileGate.Release();
            }
        }
    }
}
