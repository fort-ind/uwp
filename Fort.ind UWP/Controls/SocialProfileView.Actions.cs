using System;
using System.Diagnostics;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace Fort.ind_UWP
{
    public sealed partial class SocialProfileView
    {
        private bool _followHandlerAttached;

        private bool _noteHandlerAttached;

        private bool _followBusy;

        private void AttachNoteHandler()
        {
            if (_noteHandlerAttached || _released) return;

            SocialNoteService.Changed += SocialNoteService_Changed;
            _noteHandlerAttached = true;

            _feeds.Sweep();
        }

        private void DetachNoteHandler()
        {
            if (!_noteHandlerAttached) return;

            SocialNoteService.Changed -= SocialNoteService_Changed;
            _noteHandlerAttached = false;
        }

        private async void SocialNoteService_Changed(object sender, SocialNoteChange change)
        {
            try
            {
                if (change == null || change.Kind == SocialNoteChangeKind.Reset) return;

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (!_released) _feeds.ApplyChange(change);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SocialProfileView: could not apply a note change - {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: note change handler failed - {ex.Message}");
            }
        }

        private void AttachFollowHandler()
        {
            if (_followHandlerAttached || _released) return;

            SocialFollowService.Changed += SocialFollowService_Changed;
            _followHandlerAttached = true;

            UpdateFollowButtons();
        }

        private void DetachFollowHandler()
        {
            if (!_followHandlerAttached) return;

            SocialFollowService.Changed -= SocialFollowService_Changed;
            _followHandlerAttached = false;
        }

        private async void SocialFollowService_Changed(object sender, SocialFollowChangedEventArgs e)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        ApplyFollowChange(e.User);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SocialProfileView: could not show a follow change - {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: follow change handler failed - {ex.Message}");
            }
        }

        private void ApplyFollowChange(SocialUserDetail updated)
        {
            if (_released || _detail == null) return;

            if (updated != null && string.Equals(updated.User.Id, _detail.User.Id, StringComparison.Ordinal))
            {
                _detail = updated;
                Details.PaintRelation(updated);
                UpdateDetailsVisibility();
            }

            UpdateFollowButtons();
        }

        private void UpdateFollowButtons()
        {
            var state = SocialFollowService.StateOf(_detail);
            if (state == SocialFollowState.None)
            {
                FollowButton.Visibility = Visibility.Collapsed;
                CompactFollowButton.Visibility = Visibility.Collapsed;
                return;
            }

            var label = SocialFollowService.LabelFor(state);
            FollowText.Text = label;
            CompactFollowText.Text = label;

            if (SocialFollowService.IsEmphasised(state))
            {
                object style;
                if (Resources.TryGetValue("FollowAccentButtonStyle", out style)) FollowButton.Style = style as Style;
            }
            else
            {
                FollowButton.ClearValue(StyleProperty);
            }

            FollowButton.IsEnabled = !_followBusy;
            CompactFollowButton.IsEnabled = !_followBusy;
            FollowButton.Visibility = Visibility.Visible;
            CompactFollowButton.Visibility = Visibility.Visible;
        }

        private async void FollowButton_Click(object sender, RoutedEventArgs e)
        {
            if (_followBusy || _detail == null || _released) return;

            _followBusy = true;
            try
            {
                UpdateFollowButtons();

                var result = await SocialFollowService.ToggleWithFeedbackAsync(this, _detail);
                if (result.Updated != null) ApplyFollowChange(result.Updated);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: follow action failed - {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _followBusy = false;
                try
                {
                    if (!_released) UpdateFollowButtons();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SocialProfileView: could not restore the follow button - {ex.Message}");
                }
            }
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var fallback = _handle == null ? null : SocialLinks.InstanceUrl("/" + _handle);
                SocialMenus.ShowAt(SocialMenus.BuildForUser(User, fallback, this), MoreButton, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: could not show the profile menu - {ex.Message}");
            }
        }
    }
}
