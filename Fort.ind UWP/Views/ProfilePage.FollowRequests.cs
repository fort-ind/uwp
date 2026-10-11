using System;
using System.Threading;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace Fort.ind_UWP
{
    public sealed partial class ProfilePage
    {
        private bool _requestHandlerAttached;

        private void AttachFollowRequests()
        {
            if (!_requestHandlerAttached)
            {
                SocialFollowRequestService.Changed += OnFollowRequestChanged;
                _requestHandlerAttached = true;
            }

            UpdateFollowRequestsBanner();
            RefreshPendingRequests();
        }

        private void DetachFollowRequests()
        {
            if (!_requestHandlerAttached) return;

            SocialFollowRequestService.Changed -= OnFollowRequestChanged;
            _requestHandlerAttached = false;
        }

        private async void RefreshPendingRequests()
        {
            try
            {
                if (ProfileService.CurrentUser == null) return;

                await SocialFollowRequestService.RefreshPendingAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppLog.Error("ProfilePage: could not check the pending follow requests", ex);
            }
        }

        private async void OnFollowRequestChanged(object sender, SocialFollowRequestChangedEventArgs e)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (!_released) UpdateFollowRequestsBanner();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("ProfilePage: could not show the follow requests", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("ProfilePage: follow request change handler failed", ex);
            }
        }

        private void UpdateFollowRequestsBanner()
        {
            var count = ProfileService.CurrentUser == null ? 0 : SocialFollowRequestService.PendingCount.GetValueOrDefault();
            if (count <= 0)
            {
                FollowRequestsInfoBar.IsOpen = false;
                return;
            }

            FollowRequestsInfoBar.Message = SocialFollowRequestService.PendingCountIsPartial
                                            ? LocalizedStrings.Format("FollowRequestsBannerManyFormat", count)
                                            : count == 1
                                              ? LocalizedStrings.Get("FollowRequestsBannerOne")
                                              : LocalizedStrings.Format("FollowRequestsBannerFormat", count);
            FollowRequestsInfoBar.IsOpen = true;
        }

        private void FollowRequestsViewButton_Click(object sender, RoutedEventArgs e)
        {
            Details_FollowListRequested(this, SocialFollowList.Requests);
        }
    }
}
