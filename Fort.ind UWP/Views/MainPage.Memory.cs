using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class MainPage
    {
        private readonly Dictionary<Page, DispatcherTimer> _unloadTimers = new Dictionary<Page, DispatcherTimer>();

        private Page _contentPage;

        private bool _memoryHandlerAttached;

        private void AttachMemoryHandler()
        {
            if (_memoryHandlerAttached) return;

            MemoryService.TrimRequested += MemoryService_TrimRequested;
            _memoryHandlerAttached = true;
        }

        private void DetachMemoryHandler()
        {
            if (!_memoryHandlerAttached) return;

            MemoryService.TrimRequested -= MemoryService_TrimRequested;
            _memoryHandlerAttached = false;
        }

        private void TrackContentPage(Page current)
        {
            if (current != null) CancelUnload(current);
            if (IsSubPage(current)) return;

            var previous = _contentPage;
            _contentPage = current;

            if (previous != null && !ReferenceEquals(previous, current) && previous.NavigationCacheMode != NavigationCacheMode.Disabled)
            {
                ScheduleUnload(previous);
            }
        }

        private static bool IsSubPage(Page page)
        {
            return page is SocialNotePage || page is SocialUserListPage || page is LoginPage;
        }

        private void ScheduleUnload(Page page)
        {
            if (_unloadTimers.ContainsKey(page)) return;

            var timer = new DispatcherTimer { Interval = MemoryService.PageUnloadDelay };
            timer.Tick += (sender, args) => UnloadPage(page);
            _unloadTimers[page] = timer;
            timer.Start();
        }

        private void CancelUnload(Page page)
        {
            DispatcherTimer timer;
            if (!_unloadTimers.TryGetValue(page, out timer)) return;

            timer.Stop();
            _unloadTimers.Remove(page);
        }

        private void UnloadPage(Page page)
        {
            try
            {
                CancelUnload(page);
                if (ReferenceEquals(ContentFrame.Content, page)) return;

                page.NavigationCacheMode = NavigationCacheMode.Disabled;

                var releasable = page as IReleasablePage;
                if (releasable != null) releasable.Release();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: could not unload {page.GetType().Name} - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void UnloadAllInactivePages()
        {
            foreach (var page in new List<Page>(_unloadTimers.Keys))
            {
                UnloadPage(page);
            }
        }

        private async void MemoryService_TrimRequested(object sender, MemoryTrimEventArgs e)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        UnloadAllInactivePages();
                        SocialEmojiPicker.TrimCurrentView();

                        if (e.IncludeCurrentPages)
                        {
                            var trimmable = ContentFrame.Content as ITrimmablePage;
                            if (trimmable != null) trimmable.Trim();
                        }

                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"MainPage: memory trim failed - {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPage: memory trim handler failed - {ex.Message}");
            }
        }
    }
}
