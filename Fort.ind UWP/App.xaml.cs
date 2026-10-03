using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Toolkit.Uwp.Notifications;
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
            this.EnteredBackground += OnEnteredBackground;
            MemoryService.Initialize();
        }

        private void OnEnteredBackground(object sender, EnteredBackgroundEventArgs e)
        {
            try
            {
                MemoryService.OnEnteredBackground();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: background memory trim failed - {ex.Message}");
            }
        }

        public static bool ResumingFromTermination { get; private set; }

        protected override async void OnLaunched(Windows.ApplicationModel.Activation.LaunchActivatedEventArgs e)
        {
            bool showStartupErrorDialog = false;
            try
            {
                AccentColorService.ApplySavedAccent();
                AccentColorService.ApplyActiveAccentToCurrentView();

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

                    var launchNavTag = JumpListService.ResolveNavTag(e.Arguments);
                    var composeRequested = JumpListService.IsComposeRequest(e.Arguments);

                    var pinnedGameUrl = GameTileService.ResolveGameUrl(e.Arguments);
                    if (pinnedGameUrl != null)
                    {
                        launchNavTag = AppConstants.NavigationGames;
                    }

                    if (isFirstNavigation)
                    {
                        rootFrame.Navigate(typeof(MainPage), launchNavTag);
                    }
                    else if (launchNavTag != null)
                    {
                        var mainPage = rootFrame.Content as MainPage;
                        if (mainPage != null)
                        {
                            mainPage.NavigateToTag(launchNavTag);
                        }
                    }

                    Window.Current.Activate();

                    if (isFirstNavigation)
                    {
                        s_composeAfterRestore = composeRequested;
                        var ignored = rootFrame.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,
                                                                    RestoreSessionInBackground);
                    }
                    else if (composeRequested)
                    {
                        var ignoredCompose = rootFrame.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,
                                                                           OpenComposeFromLaunch);
                    }

                    if (pinnedGameUrl != null)
                    {
                        var pinnedTileId = e.TileId;
                        var ignoredLaunch = rootFrame.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,
                                                                          () => LaunchPinnedGame(pinnedGameUrl, pinnedTileId));
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
                StartSocialNotifications();

                if (s_composeAfterRestore)
                {
                    s_composeAfterRestore = false;
                    OpenComposeFromLaunch();
                }

                await JumpListService.EnsureTasksAsync();
                await GameTileService.RefreshPinnedTilesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: background session restore failed - {ex.Message}");
            }
        }

        private static bool s_composeAfterRestore;

        private static async void OpenComposeFromLaunch()
        {
            try
            {
                var owner = Window.Current.Content;
                if (owner != null) await SocialWindows.ShowComposeAsync(owner, SocialComposeMode.New, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: could not open the composer from the jump list - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void StartSocialNotifications()
        {
            SocialContentService.Initialize();
            SocialFollowService.Initialize();
            SocialNoteService.Initialize();
            SocialNoteCapture.Initialize();
            SocialNotificationService.Initialize();
            SocialNotificationService.ReconcileInBackground();
        }

        protected override async void OnBackgroundActivated(BackgroundActivatedEventArgs args)
        {
            base.OnBackgroundActivated(args);

            try
            {
                if (SocialNotificationService.IsBackgroundCheck(args.TaskInstance))
                {
                    await SocialNotificationService.RunBackgroundCheckAsync(args.TaskInstance);
                }
                else if (SocialToastActions.IsToastAction(args.TaskInstance))
                {
                    await SocialToastActions.RunAsync(args.TaskInstance);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: background activation failed - {ex.GetType().Name}: {ex.Message}");
            }
        }

        protected override void OnShareTargetActivated(ShareTargetActivatedEventArgs args)
        {
            try
            {
                AccentColorService.ApplySavedAccent();
                AccentColorService.ApplyActiveAccentToCurrentView();
                AppearanceService.EnsureLoaded();

                var frame = new Frame();
                frame.NavigationFailed += OnNavigationFailed;
                AppearanceService.ApplyThemeTo(frame);
                Window.Current.Content = frame;

                frame.Navigate(typeof(SocialSharePage), args.ShareOperation);
                Window.Current.Activate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: share activation failed - {ex}");
            }
        }

        private sealed class ToastTarget
        {
            public ToastTarget(string open, string noteId, string account, bool restore)
            {
                Open = open;
                NoteId = noteId;
                Account = account;
                Restore = restore;
            }

            public string Open { get; private set; }

            public string NoteId { get; private set; }

            public string Account { get; private set; }

            public bool Restore { get; private set; }

            public bool OpensNotifications
            {
                get { return string.Equals(Open, AppConstants.ToastOpenNotifications, StringComparison.Ordinal); }
            }

            public bool OpensNote
            {
                get { return string.Equals(Open, AppConstants.ToastOpenNote, StringComparison.Ordinal) && !string.IsNullOrEmpty(NoteId); }
            }
        }

        private static ToastTarget ReadToastTarget(IActivatedEventArgs args)
        {
            var toastArgs = args as ToastNotificationActivatedEventArgs;
            if (toastArgs == null || string.IsNullOrEmpty(toastArgs.Argument)) return null;

            try
            {
                var parsed = ToastArguments.Parse(toastArgs.Argument);
                string open;
                if (!parsed.TryGetValue(AppConstants.ToastArgumentOpen, out open)) return null;

                string noteId;
                string account;
                string restore;
                parsed.TryGetValue(AppConstants.ToastArgumentNote, out noteId);
                parsed.TryGetValue(AppConstants.ToastArgumentAccount, out account);
                parsed.TryGetValue(AppConstants.ToastArgumentRestore, out restore);

                var target = new ToastTarget(open, noteId, account, !string.IsNullOrEmpty(restore));
                return target.OpensNotifications || target.OpensNote ? target : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: could not read the toast arguments - {ex.Message}");
                return null;
            }
        }

        private static void ShowToastTarget(MainPage mainPage, ToastTarget target)
        {
            if (mainPage == null || target == null) return;

            try
            {
                if (target.OpensNotifications || !SocialContentService.IsCurrentAccount(target.Account))
                {
                    mainPage.ShowSocialNotifications();
                    return;
                }

                var restored = false;
                if (target.Restore)
                {
                    var text = SocialToastActions.TakeFailedReply(target.Account, target.NoteId);
                    if (!string.IsNullOrEmpty(text))
                    {
                        SocialDraftService.SaveReplyDraft(target.Account, target.NoteId,
                                                          SocialComposeDraft.Create(text, null, null, false, null, null, null, null,
                                                                                    null, target.NoteId, null));
                        restored = true;
                    }
                }

                mainPage.ShowSocialThread(target.NoteId, restored);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: could not open the toast's note - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static async void LaunchPinnedGame(string url, string tileId)
        {
            try
            {
                var game = await GameTileService.FindPinnedGameAsync(url);
                if (game != null)
                {
                    await WebLauncher.LaunchAsync(game.Url);
                }
                else if (GameTileService.IsTileFor(tileId, url))
                {
                    await OfferToUnpinMissingGameAsync(tileId);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: could not launch the pinned game - {ex.Message}");
            }
        }

        private static async Task OfferToUnpinMissingGameAsync(string tileId)
        {
            var owner = Window.Current.Content;

            var unpin = await DialogService.ShowActionMessageAsync(owner,
                                                                   LocalizedStrings.Get("MissingPinnedGameDialogTitle"),
                                                                   LocalizedStrings.Get("MissingPinnedGameDialogBody"),
                                                                   LocalizedStrings.Get("MissingPinnedGameDialogUnpin"),
                                                                   LocalizedStrings.Get("MissingPinnedGameDialogKeep"));
            if (!unpin) return;

            if (await GameTileService.UnpinTileAsync(tileId))
            {
                AutomationHelper.AnnounceStatus(owner,
                                                LocalizedStrings.Get("MissingPinnedGameUnpinnedAnnouncement"),
                                                "MissingPinnedGame");
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
                var toastTarget = ReadToastTarget(args);

                if (isColdStart)
                {
                    AccentColorService.ApplySavedAccent();
                    AccentColorService.ApplyActiveAccentToCurrentView();

                    rootFrame = new Frame();
                    rootFrame.NavigationFailed += OnNavigationFailed;

                    ResumingFromTermination = args.PreviousExecutionState == ApplicationExecutionState.Terminated;

                    ApplySavedTheme(rootFrame);
                    Window.Current.Content = rootFrame;
                    rootFrame.Navigate(typeof(MainPage), toastTarget != null ? AppConstants.NavigationSocial : null);
                }
                else if (toastTarget != null)
                {
                    ShowToastTarget(rootFrame.Content as MainPage, toastTarget);
                }

                Window.Current.Activate();

                if (isColdStart)
                {
                    await rootFrame.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => { });

                    await LocalStorageService.InitializeAsync();
                    await ProfileService.TryRestoreSessionAsync();
                    StartSocialNotifications();

                    if (toastTarget != null && toastTarget.OpensNote)
                    {
                        ShowToastTarget(rootFrame.Content as MainPage, toastTarget);
                    }
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
                    await GameTileService.RefreshPinnedTilesAsync();
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
                SocialNotificationService.OnSuspending();
                SocialNoteCapture.OnSuspending();

                if (!SocialNotificationService.OwnsBadge && !LiveTileService.TileCleared)
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
                if (!SocialNotificationService.OwnsBadge)
                {
                    LiveTileService.ClearBadge();
                }

                SocialNotificationService.OnResuming();
                SocialNoteCapture.OnResuming();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"App: resume badge clear failed - {ex.Message}");
            }
        }
    }
}
