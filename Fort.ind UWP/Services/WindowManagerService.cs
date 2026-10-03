using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public static class WindowManagerService
    {
        private static readonly List<ViewLifetimeControl> s_secondaryViews = new List<ViewLifetimeControl>();

        private static readonly HashSet<string> s_opening = new HashSet<string>(StringComparer.Ordinal);

        private static volatile CoreDispatcher s_mainDispatcher;

        private static volatile int s_mainViewId = -1;

        public static bool IsSecondaryView
        {
            get
            {
                try
                {
                    return !CoreApplication.GetCurrentView().IsMain;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WindowManagerService: could not tell which view this is - {ex.Message}");
                    return false;
                }
            }
        }

        public static async Task<bool> ShowAsync(string navTag, string title, string header, Type pageType)
        {
            if (string.IsNullOrEmpty(navTag) || pageType == null) return false;

            if (IsSecondaryView)
            {
                Debug.WriteLine("WindowManagerService: destination windows are opened from the main window only");
                return false;
            }

            CaptureMainView();

            var request = new WindowRequest(navTag, title, header, pageType, navTag, null, false);
            var current = ApplicationView.GetForCurrentView().Id;

            var existing = Find(navTag);
            if (existing != null && await TryShowAsync(existing, current)) return true;

            var slot = await FindOrCreateAsync(request);
            return slot != null && await TryShowAsync(slot.View, current);
        }

        public static async Task<bool> ShowKeyedAsync(WindowRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Key) || request.PageType == null) return false;

            if (!IsSecondaryView) CaptureMainView();

            var mainDispatcher = s_mainDispatcher;
            if (mainDispatcher == null) return false;

            var callerViewId = ApplicationView.GetForCurrentView().Id;

            for (var attempt = 0; attempt < 2; attempt++)
            {
                var slot = await RunOnMainAsync(mainDispatcher, () => FindOrCreateAsync(request));
                if (slot == null) return false;

                if (!await TryShowAsync(slot.View, callerViewId)) continue;

                if (slot.Created)
                {
                    await ResizeShownViewAsync(slot.View, request);
                }
                else
                {
                    await ReopenAsync(slot.View, request.Parameter);
                }
                return true;
            }

            return false;
        }

        public static async Task ShowInMainWindowAsync(string navTag)
        {
            var mainDispatcher = s_mainDispatcher;
            if (mainDispatcher == null) return;

            await mainDispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                var shell = MainPage.Current;
                if (shell != null) shell.NavigateToTag(navTag);
            });

            await ApplicationViewSwitcher.SwitchAsync(s_mainViewId);
        }

        public static async Task ShowSignInInMainWindowAsync()
        {
            var mainDispatcher = s_mainDispatcher;
            if (mainDispatcher == null) return;

            await mainDispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    var shell = MainPage.Current;
                    if (shell != null) shell.ShowSignIn();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WindowManagerService: could not open sign-in in the main window - {ex.GetType().Name}: {ex.Message}");
                }
            });

            await ApplicationViewSwitcher.SwitchAsync(s_mainViewId);
        }

        public static async Task CloseAccountScopedWindowsAsync()
        {
            var mainDispatcher = s_mainDispatcher;
            if (mainDispatcher == null) return;

            var views = await RunOnMainAsync(mainDispatcher, () =>
            {
                var scoped = new List<ViewLifetimeControl>();
                foreach (var view in s_secondaryViews)
                {
                    if (view.AccountScoped) scoped.Add(view);
                }
                return Task.FromResult(scoped);
            });

            foreach (var view in views)
            {
                try
                {
                    await view.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ConsolidateCurrentView(view));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WindowManagerService: could not close view {view.Id} - {ex.Message}");
                }
            }
        }

        public static void CloseCurrentWindow()
        {
            if (!IsSecondaryView) return;

            var view = FindById(ApplicationView.GetForCurrentView().Id);
            ConsolidateCurrentView(view);
        }

        public static void SetCurrentWindowTitle(string title)
        {
            try
            {
                var rootFrame = Window.Current.Content as Frame;
                var page = rootFrame == null ? null : rootFrame.Content as SecondaryWindowPage;
                if (page != null) page.UpdateTitle(title);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WindowManagerService: could not retitle the window - {ex.Message}");
            }
        }

        private static async void ConsolidateCurrentView(ViewLifetimeControl view)
        {
            try
            {
                var consolidated = await ApplicationView.GetForCurrentView().TryConsolidateAsync();
                if (!consolidated) ForceRelease(view);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WindowManagerService: consolidating a window failed - {ex.Message}");
                ForceRelease(view);
            }
        }

        private static void ForceRelease(ViewLifetimeControl view)
        {
            if (view == null) return;

            try
            {
                view.StopViewInUse();
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"WindowManagerService: view {view.Id} was already released - {ex.Message}");
            }
        }

        private static ViewLifetimeControl FindById(int viewId)
        {
            lock (s_secondaryViews)
            {
                foreach (var view in s_secondaryViews)
                {
                    if (view.Id == viewId) return view;
                }
            }
            return null;
        }

        private static void CaptureMainView()
        {
            if (s_mainDispatcher != null) return;

            s_mainViewId = ApplicationView.GetForCurrentView().Id;
            s_mainDispatcher = CoreWindow.GetForCurrentThread().Dispatcher;
        }

        private static async Task<T> RunOnMainAsync<T>(CoreDispatcher mainDispatcher, Func<Task<T>> work)
        {
            if (mainDispatcher.HasThreadAccess) return await work();

            var completion = new TaskCompletionSource<T>();
            await mainDispatcher.RunAsync(CoreDispatcherPriority.Normal, () => RunAndComplete(work, completion));
            return await completion.Task;
        }

        private static async void RunAndComplete<T>(Func<Task<T>> work, TaskCompletionSource<T> completion)
        {
            try
            {
                completion.TrySetResult(await work());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        private static ViewLifetimeControl Find(string key)
        {
            lock (s_secondaryViews)
            {
                foreach (var view in s_secondaryViews)
                {
                    if (string.Equals(view.Key, key, StringComparison.Ordinal)) return view;
                }
            }
            return null;
        }

        private static async Task<WindowSlot> FindOrCreateAsync(WindowRequest request)
        {
            var existing = Find(request.Key);
            if (existing != null) return new WindowSlot(existing, false);

            if (!s_opening.Add(request.Key)) return null;

            try
            {
                var view = await CreateViewAsync(request);
                if (view == null) return null;

                lock (s_secondaryViews)
                {
                    s_secondaryViews.Add(view);
                }
                return new WindowSlot(view, true);
            }
            finally
            {
                s_opening.Remove(request.Key);
            }
        }

        private static void Forget(ViewLifetimeControl view)
        {
            lock (s_secondaryViews)
            {
                s_secondaryViews.Remove(view);
            }
        }

        private static async Task<bool> TryShowAsync(ViewLifetimeControl view, int anchorViewId)
        {
            try
            {
                view.StartViewInUse();
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"WindowManagerService: view {view.Id} is closing - {ex.Message}");
                Forget(view);
                return false;
            }

            try
            {
                return await ApplicationViewSwitcher.TryShowAsStandaloneAsync(
                    view.Id, ViewSizePreference.Default,
                    anchorViewId, ViewSizePreference.Default);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WindowManagerService: could not show view {view.Id} - {ex.Message}");
                return false;
            }
            finally
            {
                try
                {
                    view.StopViewInUse();
                }
                catch (InvalidOperationException ex)
                {
                    Debug.WriteLine($"WindowManagerService: view {view.Id} closed while being shown - {ex.Message}");
                }
            }
        }

        private static async Task ResizeShownViewAsync(ViewLifetimeControl view, WindowRequest request)
        {
            if (!request.PreferredSize.HasValue) return;

            var size = request.PreferredSize.Value;
            try
            {
                await view.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (!ApplicationView.GetForCurrentView().TryResizeView(size))
                        {
                            Debug.WriteLine($"WindowManagerService: Windows kept view {view.Id} at its own size");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"WindowManagerService: could not resize view {view.Id} - {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WindowManagerService: resize dispatch failed - {ex.Message}");
            }
        }

        private static async Task ReopenAsync(ViewLifetimeControl view, object parameter)
        {
            try
            {
                await view.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        var rootFrame = Window.Current.Content as Frame;
                        var page = rootFrame == null ? null : rootFrame.Content as SecondaryWindowPage;
                        if (page != null) page.Reopen(parameter);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"WindowManagerService: could not reopen view {view.Id} - {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WindowManagerService: reopen dispatch failed - {ex.Message}");
            }
        }

        private static async Task<ViewLifetimeControl> CreateViewAsync(WindowRequest request)
        {
            ViewLifetimeControl view = null;

            var newView = CoreApplication.CreateNewView();
            await newView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    AccentColorService.ApplyActiveAccentToCurrentView();

                    var created = ViewLifetimeControl.CreateForCurrentView(request);
                    created.StartViewInUse();
                    created.Released += OnViewReleased;

                    var rootFrame = new Frame();
                    AppearanceService.ApplyThemeTo(rootFrame);
                    Window.Current.Content = rootFrame;

                    rootFrame.Navigate(typeof(SecondaryWindowPage), created);

                    var applicationView = ApplicationView.GetForCurrentView();
                    if (request.PreferredSize.HasValue)
                    {
                        applicationView.SetPreferredMinSize(new Size(AppConstants.SocialWindowMinWidth, AppConstants.SocialWindowMinHeight));
                    }

                    Window.Current.Activate();
                    applicationView.Title = request.Title ?? "";

                    view = created;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WindowManagerService: could not build the window for {request.Key} - {ex.GetType().Name}: {ex.Message}"
                                    + (ex.InnerException != null ? $" | inner: {ex.InnerException.Message}" : ""));
                    CloseCurrentWindowNow();
                }
            });

            return view;
        }

        private static async void OnViewReleased(object sender, EventArgs e)
        {
            try
            {
                var view = sender as ViewLifetimeControl;
                if (view == null) return;

                var rootFrame = Window.Current.Content as Frame;
                var page = rootFrame == null ? null : rootFrame.Content as SecondaryWindowPage;
                if (page != null) page.Release();

                SocialInlineVideo.StopCurrentView();
                SocialEmojiPicker.ForgetCurrentView();
                SearchItem.ForgetSubscribersOnView(view.Id);
                SocialNoteHub.ForgetView(view.Id);
                DialogService.ForgetView(view.Id);
                MisskeyAuthService.ForgetView(view.Id);

                var mainDispatcher = s_mainDispatcher;
                if (mainDispatcher != null)
                {
                    await mainDispatcher.RunAsync(CoreDispatcherPriority.Normal, () => Forget(view));
                }

                CloseCurrentWindowNow();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WindowManagerService: releasing a window failed - {ex.Message}");
            }
        }

        private static void CloseCurrentWindowNow()
        {
            try
            {
                Window.Current.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WindowManagerService: could not close the window - {ex.Message}");
            }
        }

        private sealed class WindowSlot
        {
            public WindowSlot(ViewLifetimeControl view, bool created)
            {
                View = view;
                Created = created;
            }

            public ViewLifetimeControl View { get; private set; }

            public bool Created { get; private set; }
        }
    }
}
