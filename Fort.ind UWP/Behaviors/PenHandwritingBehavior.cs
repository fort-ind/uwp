using System;
using System.Diagnostics;
using Microsoft.Xaml.Interactivity;
using Windows.Devices.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    public sealed class PenHandwritingBehavior : Behavior<Control>
    {
        private TextBox _box;

        private HandwritingView _view;

        private int _appliedVersion = -1;

        private bool _fontApplied;

        private bool _useFont;

        private bool _writeOnBox;

        private InlineInkLayer _layer;

        public bool WriteOnBox { get; set; }

        public event EventHandler TextWritten;

        public static void AttachTo(Control control)
        {
            if (control == null) return;

            try
            {
                Interaction.GetBehaviors(control).Add(new PenHandwritingBehavior());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PenHandwritingBehavior: could not attach - {ex.Message}");
            }
        }

        protected override void OnAttached()
        {
            base.OnAttached();

            try
            {
                AssociatedObject.Loaded += OnAssociatedObjectLoaded;
                AssociatedObject.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(OnPointerEntered), true);
                AssociatedObject.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
                AssociatedObject.GotFocus += OnGotFocus;
                Resolve();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PenHandwritingBehavior: could not attach - {ex.Message}");
            }
        }

        protected override void OnDetaching()
        {
            try
            {
                AssociatedObject.Loaded -= OnAssociatedObjectLoaded;
                AssociatedObject.RemoveHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(OnPointerEntered));
                AssociatedObject.RemoveHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved));
                AssociatedObject.GotFocus -= OnGotFocus;
                ReleaseBox();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PenHandwritingBehavior: could not detach - {ex.Message}");
            }

            base.OnDetaching();
        }

        private void OnAssociatedObjectLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Resolve();
                Sync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PenHandwritingBehavior: could not sync on load - {ex.Message}");
            }
        }

        private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                Resolve();
                Sync();
                ShowLayerFor(e);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PenHandwritingBehavior: could not sync on pointer - {ex.Message}");
            }
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                if (_writeOnBox && (_layer == null || !_layer.IsOpen)) ShowLayerFor(e);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PenHandwritingBehavior: could not open the ink layer - {ex.Message}");
            }
        }

        private void ShowLayerFor(PointerRoutedEventArgs e)
        {
            if (!_writeOnBox || _box == null || e.Pointer.PointerDeviceType != PointerDeviceType.Pen) return;

            if (_layer == null) _layer = new InlineInkLayer(_box, OnLayerWrote);
            _layer.Show();
        }

        private void OnLayerWrote()
        {
            TextWritten?.Invoke(this, EventArgs.Empty);
        }

        private void OnGotFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                Resolve();
                Sync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PenHandwritingBehavior: could not sync on focus - {ex.Message}");
            }
        }

        private void Resolve()
        {
            if (_box != null || AssociatedObject == null) return;

            _box = AssociatedObject as TextBox ?? VisualTreeSearch.FindDescendant<TextBox>(AssociatedObject);
            if (_box == null) return;

            _box.TextChanged += OnTextChanged;
            _appliedVersion = -1;
        }

        private void ReleaseBox()
        {
            CloseLayer();
            if (_box != null) _box.TextChanged -= OnTextChanged;
            HookView(null);
            RestoreFont();
            _box = null;
        }

        private void Sync()
        {
            if (_box == null) return;

            HookView(_box.HandwritingView);

            var version = InkSettingsService.Version;
            if (version == _appliedVersion) return;
            _appliedVersion = version;

            var mode = InkSettingsService.PenTextMode;
            var viewEnabled = mode == PenTextMode.WindowsView || (mode == PenTextMode.WriteOnBox && !WriteOnBox);
            _writeOnBox = WriteOnBox && mode == PenTextMode.WriteOnBox;

            _box.IsHandwritingViewEnabled = viewEnabled;
            _useFont = viewEnabled && InkSettingsService.HandwritingFont;
            UpdateFont();
            if (!_writeOnBox) CloseLayer();
        }

        private void CloseLayer()
        {
            if (_layer == null) return;

            _layer.Close();
            _layer = null;
        }

        private void HookView(HandwritingView view)
        {
            if (view == _view) return;

            if (_view != null)
            {
                _view.Opened -= OnViewOpened;
                _view.Closed -= OnViewClosed;
            }

            _view = view;
            if (_view != null)
            {
                _view.Opened += OnViewOpened;
                _view.Closed += OnViewClosed;
            }
        }

        private void OnViewOpened(HandwritingView sender, HandwritingPanelOpenedEventArgs args)
        {
            try
            {
                UpdateFont();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PenHandwritingBehavior: could not apply the handwriting font - {ex.Message}");
            }
        }

        private void OnViewClosed(HandwritingView sender, HandwritingPanelClosedEventArgs args)
        {
            try
            {
                RestoreFont();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PenHandwritingBehavior: could not restore the font - {ex.Message}");
            }
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (_fontApplied || _useFont) UpdateFont();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PenHandwritingBehavior: could not follow the handwriting view - {ex.Message}");
            }
        }

        private void UpdateFont()
        {
            if (_box == null) return;

            var view = _box.HandwritingView;
            var name = view != null && view.IsOpen && _useFont
                       ? InkSettingsService.HandwritingFontFamilyName
                       : null;

            if (name == null)
            {
                RestoreFont();
                return;
            }

            _box.FontFamily = new FontFamily(name);
            _fontApplied = true;
        }

        private void RestoreFont()
        {
            if (!_fontApplied || _box == null) return;

            _box.ClearValue(Control.FontFamilyProperty);
            _fontApplied = false;
        }
    }
}
