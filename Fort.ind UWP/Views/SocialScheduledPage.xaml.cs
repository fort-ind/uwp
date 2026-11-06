using System;
using System.Collections.ObjectModel;
using System.Threading;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SocialScheduledPage : Page, IReleasablePage
    {
        private enum ListState
        {
            Loading,
            Ready,
            Empty,
            Failed,
            NeedsPermission
        }

        private readonly ObservableCollection<SocialScheduledItem> _items = new ObservableCollection<SocialScheduledItem>();

        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        private SocialScheduledArgs _args;

        private ListState _state = ListState.Loading;

        private int _loadVersion;

        private bool _handlersAttached;

        private bool _released;

        public SocialScheduledPage()
        {
            this.InitializeComponent();

            ScheduledList.ItemsSource = _items;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            try
            {
                var args = e.Parameter as SocialScheduledArgs;
                if (args == null) return;

                if (!SocialContentService.IsCurrentAccount(args.AccountId))
                {
                    WindowManagerService.CloseCurrentWindow();
                    return;
                }

                _args = args;
                Load(false);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialScheduledPage: could not show the scheduled notes", ex);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            if (e.NavigationMode == NavigationMode.Back) Release();
        }

        public void Release()
        {
            if (_released) return;
            _released = true;

            DetachHandlers();

            try
            {
                _cancellation.Cancel();
                _cancellation.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialScheduledPage: could not cancel the pending load", ex);
            }

            _items.Clear();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_released || _handlersAttached) return;

            SocialScheduleService.Changed += SocialScheduleService_Changed;
            ProfileService.AuthStateChanged += ProfileService_AuthStateChanged;
            _handlersAttached = true;
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            DetachHandlers();
        }

        private void DetachHandlers()
        {
            if (!_handlersAttached) return;

            SocialScheduleService.Changed -= SocialScheduleService_Changed;
            ProfileService.AuthStateChanged -= ProfileService_AuthStateChanged;
            _handlersAttached = false;
        }

        private async void SocialScheduleService_Changed(object sender, EventArgs e)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (!_released && _args != null) Load(true);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SocialScheduledPage: could not refresh the list", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialScheduledPage: change handler failed", ex);
            }
        }

        private async void ProfileService_AuthStateChanged(object sender, bool isSignedIn)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (_released || _args == null || SocialContentService.IsCurrentAccount(_args.AccountId)) return;

                        if (Frame != null && Frame.CanGoBack) Frame.GoBack();
                        else WindowManagerService.CloseCurrentWindow();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SocialScheduledPage: could not leave after an account change", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialScheduledPage: auth handler failed", ex);
            }
        }

        private async void Load(bool silent)
        {
            try
            {
                if (_released) return;

                var version = ++_loadVersion;
                if (!silent || _items.Count == 0) SetState(ListState.Loading);

                if (!SocialPermissions.Has(SocialPermissions.ReadNotesSchedule))
                {
                    _items.Clear();
                    SetState(ListState.NeedsPermission);
                    return;
                }

                var result = await SocialScheduleService.FetchAsync(_cancellation.Token);
                if (_released || version != _loadVersion) return;

                switch (result.Status)
                {
                    case SocialApiStatus.Ok:
                        _items.Clear();
                        foreach (var scheduled in result.Value)
                        {
                            _items.Add(SocialScheduledItem.Create(scheduled));
                        }

                        SetState(_items.Count == 0 ? ListState.Empty : ListState.Ready);
                        break;
                    case SocialApiStatus.PermissionDenied:
                        SocialPermissions.Forget(SocialPermissions.ReadNotesSchedule);
                        _items.Clear();
                        SetState(ListState.NeedsPermission);
                        break;
                    default:
                        if (_items.Count == 0) SetState(ListState.Failed);
                        break;
                }
            }
            catch (OperationCanceledException) when (_released)
            {
                System.Diagnostics.Debug.WriteLine("SocialScheduledPage: load cancelled");
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialScheduledPage: load failed", ex);
                if (!_released && _items.Count == 0) SetState(ListState.Failed);
            }
        }

        private void SetState(ListState state)
        {
            _state = state;

            LoadingRing.IsActive = state == ListState.Loading;
            LoadingRing.Visibility = state == ListState.Loading ? Visibility.Visible : Visibility.Collapsed;

            var showPanel = state != ListState.Loading && state != ListState.Ready;
            StatePanel.Visibility = showPanel ? Visibility.Visible : Visibility.Collapsed;
            FooterHost.Visibility = state == ListState.Ready ? Visibility.Collapsed : Visibility.Visible;
            if (!showPanel) return;

            switch (state)
            {
                case ListState.Empty:
                    StateGlyph.Glyph = "";
                    StateText.Text = LocalizedStrings.Get("SocialScheduledEmpty");
                    StateButton.Visibility = Visibility.Collapsed;
                    break;
                case ListState.NeedsPermission:
                    StateGlyph.Glyph = "";
                    StateText.Text = LocalizedStrings.Get("SocialScheduledNeedPermission");
                    StateButton.Content = LocalizedStrings.Get("SocialScheduledSignInButton");
                    StateButton.Visibility = Visibility.Visible;
                    break;
                case ListState.Failed:
                    StateGlyph.Glyph = "";
                    StateText.Text = LocalizedStrings.Get("SocialScheduledFailed");
                    StateButton.Content = LocalizedStrings.Get("SocialRetryButton");
                    StateButton.Visibility = Visibility.Visible;
                    break;
            }

            AutomationHelper.AnnounceLiveRegion(StateText);
        }

        private async void StateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_state == ListState.Failed)
                {
                    Load(false);
                }
                else if (_state == ListState.NeedsPermission)
                {
                    await SocialPermissions.OfferSignInAsync(this, SocialSignInPrompt.Schedule);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialScheduledPage: state button failed", ex);
            }
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = (sender as FrameworkElement)?.DataContext as SocialScheduledItem;
                if (item == null) return;

                await SocialWindows.ShowScheduledComposeAsync(this, item.Scheduled);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialScheduledPage: could not edit the scheduled note", ex);
            }
        }

        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = (sender as FrameworkElement)?.DataContext as SocialScheduledItem;
                if (item == null) return;

                if (!await SocialScheduleService.CancelAsync(this, item.Scheduled)) return;

                _items.Remove(item);
                if (_items.Count == 0 && !_released) SetState(ListState.Empty);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialScheduledPage: could not delete the scheduled note", ex);
            }
        }
    }
}
