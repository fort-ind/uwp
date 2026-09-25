using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
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
                Debug.WriteLine("WindowManagerService: new windows are opened from the main window only");
                return false;
            }

            CaptureMainView();

            var existing = Find(navTag);
            if (existing != null && await TryShowAsync(existing)) return true;

            if (!s_opening.Add(navTag)) return false;

            try
            {
                var view = await CreateViewAsync(navTag, title, header, pageType);
                if (view == null) return false;

                s_secondaryViews.Add(view);
                return await TryShowAsync(view);
            }
            finally
            {
                s_opening.Remove(navTag);
            }
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

        private static void CaptureMainView()
        {
            if (s_mainDispatcher != null) return;

            s_mainViewId = ApplicationView.GetForCurrentView().Id;
            s_mainDispatcher = CoreWindow.GetForCurrentThread().Dispatcher;
        }

        private static ViewLifetimeControl Find(string navTag)
        {
            foreach (var view in s_secondaryViews)
            {
                if (string.Equals(view.NavTag, navTag, StringComparison.Ordinal)) return view;
            }
            return null;
        }

        private static async Task<bool> TryShowAsync(ViewLifetimeControl view)
        {
            try
            {
                view.StartViewInUse();
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"WindowManagerService: view {view.Id} is closing - {ex.Message}");
                s_secondaryViews.Remove(view);
                return false;
            }

            try
            {
                return await ApplicationViewSwitcher.TryShowAsStandaloneAsync(
                    view.Id, ViewSizePreference.Default,
                    ApplicationView.GetForCurrentView().Id, ViewSizePreference.Default);
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

        private static async Task<ViewLifetimeControl> CreateViewAsync(string navTag, string title, string header, Type pageType)
        {
            ViewLifetimeControl view = null;

            var newView = CoreApplication.CreateNewView();
            await newView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    AccentColorService.ApplyActiveAccentToCurrentView();

                    var created = ViewLifetimeControl.CreateForCurrentView(navTag, title, header, pageType);
                    created.StartViewInUse();
                    created.Released += OnViewReleased;

                    var rootFrame = new Frame();
                    AppearanceService.ApplyThemeTo(rootFrame);
                    Window.Current.Content = rootFrame;

                    rootFrame.Navigate(typeof(SecondaryWindowPage), created);

                    Window.Current.Activate();
                    ApplicationView.GetForCurrentView().Title = title;

                    view = created;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WindowManagerService: could not build the window for {navTag} - {ex.GetType().Name}: {ex.Message}"
                                    + (ex.InnerException != null ? $" | inner: {ex.InnerException.Message}" : ""));
                    CloseCurrentWindow();
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

                SearchItem.ForgetSubscribersOnView(view.Id);
                DialogService.ForgetView(view.Id);

                var mainDispatcher = s_mainDispatcher;
                if (mainDispatcher != null)
                {
                    await mainDispatcher.RunAsync(CoreDispatcherPriority.Normal, () => s_secondaryViews.Remove(view));
                }

                CloseCurrentWindow();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WindowManagerService: releasing a window failed - {ex.Message}");
            }
        }

        private static void CloseCurrentWindow()
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
    }
}
