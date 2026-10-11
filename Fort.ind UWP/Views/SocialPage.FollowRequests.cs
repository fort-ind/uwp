using System;
using System.Linq;
using System.Threading;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace Fort.ind_UWP
{
    public sealed partial class SocialPage
    {
        private bool _requestHandlerAttached;

        private void AttachFollowRequests()
        {
            if (_requestHandlerAttached) return;

            SocialFollowRequestService.Changed += OnFollowRequestChanged;
            _requestHandlerAttached = true;
            ApplyPendingRequests();
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
                await SocialFollowRequestService.RefreshPendingAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not check the pending follow requests", ex);
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
                        if (e.UserId == null)
                        {
                            ApplyPendingRequests();
                            return;
                        }

                        foreach (var item in _notifications.Where(i => i.IsFollowRequest
                                                                       && string.Equals(i.Actor.Id, e.UserId, StringComparison.Ordinal)))
                        {
                            item.RequestState = e.State;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("SocialPage: could not show a follow request change", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: follow request change handler failed", ex);
            }
        }

        private void ApplyPendingRequests()
        {
            var undecided = _notifications.Where(i => i.IsFollowRequest
                                                       && (i.RequestState == SocialFollowRequestState.Pending
                                                           || i.RequestState == SocialFollowRequestState.None));
            foreach (var item in undecided.ToList())
            {
                item.RequestState = SocialFollowRequestService.StateOf(item.Actor.Id);
            }
        }

        private void FollowRequestAccept_Click(object sender, RoutedEventArgs e)
        {
            RespondToRequest(sender, true);
        }

        private void FollowRequestReject_Click(object sender, RoutedEventArgs e)
        {
            RespondToRequest(sender, false);
        }

        private async void RespondToRequest(object sender, bool accept)
        {
            var item = (sender as FrameworkElement)?.DataContext as SocialFeedItem;
            try
            {
                if (item == null || !item.IsFollowRequest || item.RequestBusy) return;

                item.RequestBusy = true;
                var state = await SocialFollowRequestService.RespondAsync(this, item.Actor, accept);
                if (state.HasValue)
                {
                    item.RequestState = state.Value;
                    AutomationHelper.AnnounceStatus(this, SocialFollowRequestService.OutcomeText(state.Value), "SocialFollowRequest");
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialPage: could not answer the follow request", ex);
            }
            finally
            {
                if (item != null) item.RequestBusy = false;
            }
        }
    }
}
