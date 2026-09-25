using System;
using System.Diagnostics;
using Microsoft.Xaml.Interactivity;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public sealed class WindowActivationStateBehavior : Behavior<Control>
    {
        private Window _window;

        public string ActiveStateName { get; set; }

        public string InactiveStateName { get; set; }

        protected override void OnAttached()
        {
            base.OnAttached();

            try
            {
                _window = Window.Current;
                if (_window == null) return;

                _window.Activated += OnWindowActivated;
                AssociatedObject.Loaded += OnAssociatedObjectLoaded;

                ApplyCurrentState();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WindowActivationStateBehavior: Failed to attach - {ex.Message}");
            }
        }

        protected override void OnDetaching()
        {
            try
            {
                AssociatedObject.Loaded -= OnAssociatedObjectLoaded;

                if (_window != null)
                {
                    _window.Activated -= OnWindowActivated;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WindowActivationStateBehavior: Failed to detach - {ex.Message}");
            }

            _window = null;
            base.OnDetaching();
        }

        private void OnAssociatedObjectLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyCurrentState();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WindowActivationStateBehavior: Failed to sync on load - {ex.Message}");
            }
        }

        private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
        {
            try
            {
                ApplyState(e.WindowActivationState != CoreWindowActivationState.Deactivated);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WindowActivationStateBehavior: Failed to follow window activation - {ex.Message}");
            }
        }

        private void ApplyCurrentState()
        {
            if (_window == null) return;

            ApplyState(_window.CoreWindow.ActivationMode != CoreWindowActivationMode.Deactivated);
        }

        private void ApplyState(bool active)
        {
            var stateName = active ? ActiveStateName : InactiveStateName;
            if (AssociatedObject == null || string.IsNullOrEmpty(stateName)) return;

            VisualStateManager.GoToState(AssociatedObject, stateName, false);
        }
    }
}
