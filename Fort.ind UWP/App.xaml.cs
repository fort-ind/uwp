using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    sealed partial class App : Application
    {
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
            this.Resuming += OnResuming;
        }

        public static bool ResumingFromTermination { get; private set; }

        protected override async void OnLaunched(Windows.ApplicationModel.Activation.LaunchActivatedEventArgs e)
        {
            bool showStartupErrorDialog = false;
            try
            {
                AccentColorService.ApplySavedAccent();

                Frame rootFrame = Window.Current.Content as Frame;

                if (rootFrame == null)
                {
                    rootFrame = new Frame();

                    rootFrame.NavigationFailed += OnNavigationFailed;

                    ResumingFromTermination = e.PreviousExecutionState == ApplicationExecutionState.Terminated;

                    ApplySavedTheme(rootFrame);

                    Window.Current.Content = rootFrame;
                }

                if (!e.PrelaunchActivated)
                {
                    var isFirstNavigation = rootFrame.Content == null;

                    var jumpNavTag = JumpListService.ResolveNavTag(e.Arguments);

                    if (isFirstNavigation)
                    {
                        rootFrame.Navigate(typeof(MainPage), jumpNavTag);
                    }
                    else if (jumpNavTag != null)
                    {
                        var mainPage = rootFrame.Content as MainPage;
                        if (mainPage != null)
                        {
                            mainPage.NavigateToTag(jumpNavTag);
                        }
                    }

                    Window.Current.Activate();

                    if (isFirstNavigation)
                    {
                        var ignored = rootFrame.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,
                                                                    RestoreSessionInBackground);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Critical: OnLaunched failed - {ex}");
                showStartupErrorDialog = true;
            }

            if (showStartupErrorDialog)
            {
                try
                {
                    if (Window.Current.Content != null)
                    {
                        await DialogService.ShowMessageAsync(Window.Current.Content,
                                                             LocalizedStrings.Get("StartupErrorDialogTitle"),
                                                             LocalizedStrings.Get("StartupErrorDialogBody"),
                                                             LocalizedStrings.Get("DialogOk"));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Critical: startup error dialog failed - {ex.Message}");
                }
            }
        }

        private static void ApplySavedTheme(Frame rootFrame)
        {
            if (rootFrame == null) return;

            try
            {
                var savedTheme = Windows.Storage.ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTheme]?.ToString();
                switch (savedTheme)
                {
                    case AppConstants.ThemeLight: rootFrame.RequestedTheme = ElementTheme.Light; break;
                    case AppConstants.ThemeDark: rootFrame.RequestedTheme = ElementTheme.Dark; break;
                    default: rootFrame.RequestedTheme = ElementTheme.Default; break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: Failed to apply saved theme - {ex.Message}");
            }
        }

        private async void RestoreSessionInBackground()
        {
            try
            {
                await LocalStorageService.InitializeAsync();
                await ProfileService.TryRestoreSessionAsync();

                await JumpListService.EnsureTasksAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: background session restore failed - {ex.Message}");
            }
        }

        protected override async void OnActivated(Windows.ApplicationModel.Activation.IActivatedEventArgs args)
        {
            try
            {
                var protocolArgs = args.Kind == Windows.ApplicationModel.Activation.ActivationKind.Protocol
                                   ? args as Windows.ApplicationModel.Activation.ProtocolActivatedEventArgs
                                   : null;

                Frame rootFrame = Window.Current.Content as Frame;
                var isColdStart = rootFrame == null;

                if (isColdStart)
                {
                    AccentColorService.ApplySavedAccent();

                    rootFrame = new Frame();
                    rootFrame.NavigationFailed += OnNavigationFailed;

                    ResumingFromTermination = args.PreviousExecutionState == ApplicationExecutionState.Terminated;

                    ApplySavedTheme(rootFrame);
                    Window.Current.Content = rootFrame;
                    rootFrame.Navigate(typeof(MainPage));
                }

                Window.Current.Activate();

                if (isColdStart)
                {
                    await rootFrame.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => { });

                    await LocalStorageService.InitializeAsync();
                    await ProfileService.TryRestoreSessionAsync();
                }

                MisskeyAuthResult signInResult = null;
                if (protocolArgs != null)
                {
                    Debug.WriteLine($"OnActivated: protocol callback received for {protocolArgs.Uri.Scheme}://{protocolArgs.Uri.Host}");

                    signInResult = await MisskeyAuthService.HandleProtocolActivationAsync(protocolArgs.Uri);
                    if (signInResult != null && signInResult.Success)
                    {
                        await ProfileService.ApplySignInResultAsync(signInResult);
                    }
                }

                if (isColdStart)
                {
                    await JumpListService.EnsureTasksAsync();
                }

                if (signInResult != null && !signInResult.Success)
                {
                    await ShowSignInFailedAsync(signInResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnActivated failed: {ex.Message}");
            }
        }

        private static async Task ShowSignInFailedAsync(string reason)
        {
            try
            {
                var body = string.IsNullOrWhiteSpace(reason)
                           ? LocalizedStrings.Get("SignInFailedDialogBody")
                           : LocalizedStrings.Format("SignInFailedDialogBodyFormat", reason);

                await DialogService.ShowMessageAsync(Window.Current.Content,
                                                     LocalizedStrings.Get("SignInFailedDialogTitle"),
                                                     body,
                                                     LocalizedStrings.Get("DialogOk"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: could not report the sign-in failure - {ex.Message}");
            }
        }

        private async void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            e.Handled = true;

            try
            {
                Debug.WriteLine($"Navigation failed: {e.SourcePageType.FullName} - {(e.Exception != null ? e.Exception.Message : "Unknown error")}");

                await DialogService.ShowMessageAsync(Window.Current.Content,
                                                     LocalizedStrings.Get("NavigationErrorDialogTitle"),
                                                     LocalizedStrings.Get("NavigationErrorDialogBody"),
                                                     LocalizedStrings.Get("DialogOk"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error dialog failed: {ex.Message}");
            }
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            SuspendingDeferral deferral = e.SuspendingOperation.GetDeferral();

            try
            {
                if (!LiveTileService.TileCleared)
                {
                    LiveTileService.UpdateBadgeGlyph(LiveTileService.NewContentBadgeGlyph);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: suspend badge update failed - {ex.Message}");
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void OnResuming(object sender, object e)
        {
            try
            {
                LiveTileService.ClearBadge();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: resume badge clear failed - {ex.Message}");
            }
        }
    }
}
