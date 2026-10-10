using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        private static Dictionary<string, Dictionary<string, string>> s_drafts = NewStore();

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

            string text;
            lock (s_lock)
            {
                Dictionary<string, string> drafts;
                if (!s_drafts.TryGetValue(account, out drafts) || !drafts.TryGetValue(slot, out text)) return null;
            }

            JsonObject parsed;
            return JsonObject.TryParse(text, out parsed) ? SocialComposeDraft.FromJson(parsed) : null;
        }

        public static void Save(string account, string slot, SocialComposeDraft draft)
        {
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(slot)) return;

            if (draft == null || draft.IsEmpty)
            {
                Remove(account, slot);
                return;
            }

            var text = draft.ToJson().Stringify();
            lock (s_lock)
            {
                Dictionary<string, string> drafts;
                if (!s_drafts.TryGetValue(account, out drafts))
                {
                    drafts = new Dictionary<string, string>(StringComparer.Ordinal);
                    s_drafts[account] = drafts;
                }

                drafts[slot] = text;
                s_dirty = true;
            }

            ScheduleWrite();
        }

        public static void Remove(string account, string slot)
        {
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(slot)) return;

            lock (s_lock)
            {
                Dictionary<string, string> drafts;
                if (!s_drafts.TryGetValue(account, out drafts) || !drafts.Remove(slot)) return;

                if (drafts.Count == 0) s_drafts.Remove(account);
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

                if (!s_drafts.Remove(account)) return;
                s_dirty = true;
            }

            await FlushAsync();
        }

        public static void ResetForAppDataWipe()
        {
            lock (s_lock)
            {
                s_saveDebounce.Cancel();
                s_drafts = NewStore();
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
                AppLog.Error("SocialDraftService: scheduled save failed", ex);
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
                    var loaded = ParseStore(JsonObject.Parse(text));
                    lock (s_lock)
                    {
                        foreach (var pair in s_drafts)
                        {
                            loaded[pair.Key] = pair.Value;
                        }
                        s_drafts = loaded;
                    }
                }

                s_loaded = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialDraftService: could not read the drafts", ex);
                if (file != null) s_saveBlocked = !await TryMoveAsideAsync(file);
                s_loaded = true;
            }
            finally
            {
                s_fileGate.Release();
            }
        }

        private static Dictionary<string, Dictionary<string, string>> NewStore()
        {
            return new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        }

        private static Dictionary<string, Dictionary<string, string>> ParseStore(JsonObject root)
        {
            var store = NewStore();
            foreach (var account in root.Where(pair => pair.Value != null && pair.Value.ValueType == JsonValueType.Object))
            {
                var slots = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var slot in account.Value.GetObject().Where(pair => pair.Value != null && pair.Value.ValueType == JsonValueType.Object))
                {
                    slots[slot.Key] = slot.Value.Stringify();
                }

                if (slots.Count > 0) store[account.Key] = slots;
            }

            return store;
        }

        private static string SerializeStore(Dictionary<string, Dictionary<string, string>> store)
        {
            var builder = new StringBuilder("{");
            var firstAccount = true;
            foreach (var account in store)
            {
                if (!firstAccount) builder.Append(',');
                firstAccount = false;

                builder.Append(JsonValue.CreateStringValue(account.Key).Stringify()).Append(":{");
                var firstSlot = true;
                foreach (var slot in account.Value)
                {
                    if (!firstSlot) builder.Append(',');
                    firstSlot = false;
                    builder.Append(JsonValue.CreateStringValue(slot.Key).Stringify()).Append(':').Append(slot.Value);
                }
                builder.Append('}');
            }

            return builder.Append('}').ToString();
        }

        private static async Task<bool> TryMoveAsideAsync(StorageFile file)
        {
            try
            {
                await file.RenameAsync(AppConstants.SocialDraftsFileName + ".bak", NameCollisionOption.ReplaceExisting);
                AppLog.Warning("SocialDraftService: moved the unreadable drafts file aside");
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialDraftService: could not move the unreadable drafts file aside", ex);
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
                    json = SerializeStore(s_drafts);
                }

                if (s_saveBlocked)
                {
                    AppLog.Warning("SocialDraftService: not saving - the existing drafts file could not be read or moved aside");
                    return;
                }

                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(AppConstants.SocialDraftsFileName,
                                                                                    CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, json);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialDraftService: could not save the drafts", ex);
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
