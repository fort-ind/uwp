using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.UI.Core;
using Windows.UI.ViewManagement;

namespace Fort.ind_UWP
{
    internal sealed class SocialNoteHub
    {
        private const int SweepInterval = 256;

        private static readonly object s_lock = new object();

        private static readonly List<SocialNoteHub> s_hubs = new List<SocialNoteHub>();

        [ThreadStatic]
        private static SocialNoteHub t_hub;

        private readonly Dictionary<string, List<WeakReference<SocialNoteItem>>> _items =
            new Dictionary<string, List<WeakReference<SocialNoteItem>>>(StringComparer.Ordinal);

        private readonly CoreDispatcher _dispatcher;

        private readonly int _viewId;

        private int _addsSinceSweep;

        private volatile bool _forgotten;

        private SocialNoteHub(CoreDispatcher dispatcher, int viewId)
        {
            _dispatcher = dispatcher;
            _viewId = viewId;
        }

        public static void Register(SocialNoteItem item)
        {
            if (item == null || item.Note == null) return;

            try
            {
                var hub = Current();
                if (hub != null) hub.Add(item);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteHub: could not register a note - {ex.Message}");
            }
        }

        public static void ForgetView(int viewId)
        {
            List<SocialNoteHub> forgotten = new List<SocialNoteHub>();
            lock (s_lock)
            {
                for (var i = s_hubs.Count - 1; i >= 0; i--)
                {
                    if (s_hubs[i]._viewId != viewId) continue;

                    forgotten.Add(s_hubs[i]);
                    s_hubs.RemoveAt(i);
                }
            }

            foreach (var hub in forgotten)
            {
                hub._forgotten = true;
                SocialNoteService.Changed -= hub.OnChanged;
                if (ReferenceEquals(t_hub, hub)) t_hub = null;
            }
        }

        private static SocialNoteHub Current()
        {
            var hub = t_hub;
            if (hub != null) return hub;

            var window = CoreWindow.GetForCurrentThread();
            if (window == null) return null;

            hub = new SocialNoteHub(window.Dispatcher, ApplicationView.GetForCurrentView().Id);
            lock (s_lock)
            {
                s_hubs.Add(hub);
            }

            SocialNoteService.Changed += hub.OnChanged;
            t_hub = hub;
            return hub;
        }

        private void Add(SocialNoteItem item)
        {
            List<WeakReference<SocialNoteItem>> list;
            if (!_items.TryGetValue(item.Note.Id, out list))
            {
                list = new List<WeakReference<SocialNoteItem>>(1);
                _items[item.Note.Id] = list;
            }

            list.Add(new WeakReference<SocialNoteItem>(item));

            if (++_addsSinceSweep >= SweepInterval)
            {
                _addsSinceSweep = 0;
                Sweep();
            }
        }

        private void Sweep()
        {
            var empty = new List<string>();
            foreach (var pair in _items)
            {
                pair.Value.RemoveAll(reference =>
                {
                    SocialNoteItem target;
                    return !reference.TryGetTarget(out target);
                });
                if (pair.Value.Count == 0) empty.Add(pair.Key);
            }

            foreach (var key in empty)
            {
                _items.Remove(key);
            }
        }

        private async void OnChanged(object sender, SocialNoteChange change)
        {
            try
            {
                if (_forgotten || change == null || change.NoteId == null) return;

                await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => Apply(change));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteHub: could not forward a note change - {ex.Message}");
            }
        }

        private void Apply(SocialNoteChange change)
        {
            try
            {
                if (_forgotten) return;

                List<WeakReference<SocialNoteItem>> list;
                if (!_items.TryGetValue(change.NoteId, out list)) return;

                for (var i = list.Count - 1; i >= 0; i--)
                {
                    SocialNoteItem item;
                    if (!list[i].TryGetTarget(out item))
                    {
                        list.RemoveAt(i);
                        continue;
                    }

                    item.Apply(change);
                }

                if (list.Count == 0) _items.Remove(change.NoteId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialNoteHub: could not apply a note change - {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
