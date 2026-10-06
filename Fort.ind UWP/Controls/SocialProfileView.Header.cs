using System;
using System.Diagnostics;
using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Windows.System.Power;
using Windows.UI.Composition;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    public sealed partial class SocialProfileView
    {
        private const double CompactAvatarSize = 40;

        private const double FooterMinHeight = 160;

        private const double CompactBarInteractiveProgress = 0.75;

        private const double FadedOutProgress = 0.5;

        private const double TitleBarSwitchProgress = 0.5;

        private const float MaxBlurAmount = 20;

        private const double RevealMilliseconds = 300;

        private const string BlurEffectName = "Blur";

        private const string BlurAmountProperty = BlurEffectName + ".BlurAmount";

        private const string BlurSourceName = "Backdrop";

        private const string ProgressExpression = "Clamp(-scroll.Translation.Y / Max(props.pin, 1), 0, 1)";

        private const string PinExpression = "Vector3(0, Max(0, -scroll.Translation.Y - props.pin), 0)";

        private const string BannerTravelExpression = "Vector3(0, props.bannerTravel * props.progress, 0)";

        private const string OverscrollZoomExpression = "1 + 0.25 * Clamp(scroll.Translation.Y / 50, 0, 1)";

        private const string AvatarTravelExpression = "Vector3(0, props.avatarTravel * props.progress, 0)";

        private const string AvatarScaleExpression = "Lerp(1, props.avatarScale, props.progress)";

        private const string VeilExpression = "props.progress";

        private const string BlurExpression = "props.blur * props.progress";

        private const string FadeOutExpression = "Clamp(1 - props.progress * 1.5, 0, 1)";

        private const string FadeInExpression = "Clamp((props.progress - 0.5) * 2, 0, 1)";

        private const string CompactRiseExpression = "Vector3(0, props.compactRise * (1 - Clamp((props.progress - 0.5) * 2, 0, 1)), 0)";

        private const string HostSizeExpression = "host.Size";

        private readonly UISettings _uiSettings = new UISettings();

        private readonly AccessibilitySettings _accessibilitySettings = new AccessibilitySettings();

        private readonly bool _animationsEnabled;

        private bool _collapseUnavailable;

        private ScrollViewer _scroller;

        private ScrollBar _verticalScrollBar;

        private CompositionPropertySet _scrollProperties;

        private CompositionPropertySet _collapseProperties;

        private Visual _bannerVisual;

        private SpriteVisual _blurVisual;

        private CompositionEffectBrush _blurBrush;

        private GaussianBlurEffect _blurEffect;

        private bool _collapseAttached;

        private double _pinOffset;

        private double _titleBarInset;

        private bool _effectsHandlersAttached;

        private bool _effectsEnabled = true;

        private bool _bannerSpansWindow = true;

        private byte[] _bannerPixels;

        private ElementTheme _bannerRestTheme = ElementTheme.Default;

        private ElementTheme _bannerPinnedTheme = ElementTheme.Default;

        private ElementTheme _titleBarTheme = ElementTheme.Default;

        public event EventHandler TitleBarChanged;

        public bool ExtendsUnderTitleBar
        {
            get { return _animationsEnabled && !_collapseUnavailable && !_released; }
        }

        public ElementTheme TitleBarTheme
        {
            get { return _titleBarTheme; }
        }

        public void SetTitleBarInset(double inset)
        {
            if (Math.Abs(inset - _titleBarInset) < 0.5) return;
            _titleBarInset = inset;

            try
            {
                ApplyBannerHeight(BannerHost.ActualWidth);
                ApplyScrollBarInset();
                UpdateCollapseMetrics();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: could not follow the title bar inset - {ex.Message}");
            }
        }

        private void SetUpRevealAnimations()
        {
            if (!_animationsEnabled) return;

            try
            {
                var compositor = ElementCompositionPreview.GetElementVisual(DetailsPanel).Compositor;
                var decelerate = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1.0f));

                var fade = compositor.CreateScalarKeyFrameAnimation();
                fade.Target = "Opacity";
                fade.InsertKeyFrame(0, 0);
                fade.InsertKeyFrame(1, 1, decelerate);
                fade.Duration = TimeSpan.FromMilliseconds(RevealMilliseconds);

                ElementCompositionPreview.SetImplicitShowAnimation(DetailsPanel, fade);
                ElementCompositionPreview.SetImplicitShowAnimation(TabBar, fade);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: the reveal animation could not be set up - {ex.Message}");
            }
        }

        private void RaiseTitleBarChanged()
        {
            var handler = TitleBarChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void View_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                AttachEffectsSettings();
                AttachFollowHandler();
                AttachNoteHandler();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: could not watch the effects settings - {ex.Message}");
            }
        }

        private void View_Unloaded(object sender, RoutedEventArgs e)
        {
            DetachEffectsSettings();
            DetachFollowHandler();
            DetachNoteHandler();

            if (KeepListsOnNextUnload) KeepListsOnNextUnload = false;
            else Trim();
        }

        private void AttachEffectsSettings()
        {
            if (_effectsHandlersAttached || _released) return;
            _effectsHandlersAttached = true;

            _uiSettings.AdvancedEffectsEnabledChanged += UISettings_AdvancedEffectsEnabledChanged;
            _accessibilitySettings.HighContrastChanged += AccessibilitySettings_HighContrastChanged;
            PowerManager.EnergySaverStatusChanged += PowerManager_EnergySaverStatusChanged;

            ApplyEffectsMode();
        }

        private void DetachEffectsSettings()
        {
            if (!_effectsHandlersAttached) return;
            _effectsHandlersAttached = false;

            try
            {
                _uiSettings.AdvancedEffectsEnabledChanged -= UISettings_AdvancedEffectsEnabledChanged;
                _accessibilitySettings.HighContrastChanged -= AccessibilitySettings_HighContrastChanged;
                PowerManager.EnergySaverStatusChanged -= PowerManager_EnergySaverStatusChanged;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: could not stop watching the effects settings - {ex.Message}");
            }
        }

        private void UISettings_AdvancedEffectsEnabledChanged(UISettings sender, object args)
        {
            QueueEffectsMode();
        }

        private void AccessibilitySettings_HighContrastChanged(AccessibilitySettings sender, object args)
        {
            QueueEffectsMode();
        }

        private void PowerManager_EnergySaverStatusChanged(object sender, object e)
        {
            QueueEffectsMode();
        }

        private async void QueueEffectsMode()
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        if (_effectsHandlersAttached) ApplyEffectsMode();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SocialProfileView: could not switch the banner effect - {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: could not queue the banner effect switch - {ex.Message}");
            }
        }

        private void ApplyEffectsMode()
        {
            var energySaver = PowerManager.EnergySaverStatus;
            _effectsEnabled = _blurVisual != null
                              && _uiSettings.AdvancedEffectsEnabled
                              && energySaver != EnergySaverStatus.On
                              && !_accessibilitySettings.HighContrast;

            if (_blurVisual != null) _blurVisual.IsVisible = _effectsEnabled;
            if (_collapseProperties != null)
            {
                BannerVeil.Visibility = _effectsEnabled ? Visibility.Collapsed : Visibility.Visible;
            }

            CompactBar.RequestedTheme = _effectsEnabled ? _bannerPinnedTheme : ElementTheme.Default;
            UpdateTitleBarTheme();
        }

        private void NotesList_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                AttachCollapsingHeader();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: the collapsing header could not start - {ex.GetType().Name}: {ex.Message}");
                _collapseUnavailable = true;
                RaiseTitleBarChanged();
            }
        }

        private void AttachCollapsingHeader()
        {
            if (_collapseAttached || _released) return;

            var scroller = VisualTreeSearch.FindDescendantByName(NotesList, "ScrollViewer") as ScrollViewer;
            if (scroller == null)
            {
                Debug.WriteLine("SocialProfileView: the list has no ScrollViewer yet; the header will not collapse");
                _collapseUnavailable = true;
                RaiseTitleBarChanged();
                return;
            }

            _collapseAttached = true;
            _scroller = scroller;
            _scroller.ViewChanged += Scroller_ViewChanged;
            _verticalScrollBar = VisualTreeSearch.FindDescendantByName(_scroller, "VerticalScrollBar") as ScrollBar;
            ApplyScrollBarInset();

            if (_animationsEnabled)
            {
                StartCollapsingHeader();
            }

            UpdateCollapseMetrics();
            ApplyFooterHeight();
            ApplyEffectsMode();
        }

        private void ApplyScrollBarInset()
        {
            if (_verticalScrollBar == null) return;

            var margin = new Thickness(0, _titleBarInset, 0, 0);
            if (_verticalScrollBar.Margin != margin) _verticalScrollBar.Margin = margin;
        }

        private void StartCollapsingHeader()
        {
            RaiseHeaderAboveItems();

            _scrollProperties = ElementCompositionPreview.GetScrollViewerManipulationPropertySet(_scroller);
            var compositor = _scrollProperties.Compositor;

            _collapseProperties = compositor.CreatePropertySet();
            _collapseProperties.InsertScalar("progress", 0);
            _collapseProperties.InsertScalar("pin", 0);
            _collapseProperties.InsertScalar("bannerTravel", 0);
            _collapseProperties.InsertScalar("avatarTravel", 0);
            _collapseProperties.InsertScalar("avatarScale", 1);
            _collapseProperties.InsertScalar("compactRise", 0);
            _collapseProperties.InsertScalar("blur", MaxBlurAmount);

            ElementCompositionPreview.SetIsTranslationEnabled(HeaderRoot, true);
            ElementCompositionPreview.SetIsTranslationEnabled(BannerHost, true);
            ElementCompositionPreview.SetIsTranslationEnabled(CompactBar, true);
            ElementCompositionPreview.SetIsTranslationEnabled(CompactTextPanel, true);
            ElementCompositionPreview.SetIsTranslationEnabled(AvatarPicture, true);

            var headerVisual = ElementCompositionPreview.GetElementVisual(HeaderRoot);
            var avatarVisual = ElementCompositionPreview.GetElementVisual(AvatarPicture);
            var compactVisual = ElementCompositionPreview.GetElementVisual(CompactBar);
            _bannerVisual = ElementCompositionPreview.GetElementVisual(BannerHost);

            headerVisual.Clip = compositor.CreateInsetClip();

            Animate(_collapseProperties, "progress", ProgressExpression);
            Animate(headerVisual, "Translation", PinExpression);
            Animate(_bannerVisual, "Translation", BannerTravelExpression);
            Animate(_bannerVisual, "Scale.X", OverscrollZoomExpression);
            Animate(_bannerVisual, "Scale.Y", OverscrollZoomExpression);
            Animate(ElementCompositionPreview.GetElementVisual(BannerVeil), "Opacity", VeilExpression);
            Animate(compactVisual, "Translation", BannerTravelExpression);
            Animate(compactVisual, "Opacity", FadeInExpression);
            Animate(ElementCompositionPreview.GetElementVisual(CompactTextPanel), "Translation", CompactRiseExpression);
            Animate(avatarVisual, "Translation", AvatarTravelExpression);
            Animate(avatarVisual, "Scale.X", AvatarScaleExpression);
            Animate(avatarVisual, "Scale.Y", AvatarScaleExpression);
            Animate(ElementCompositionPreview.GetElementVisual(IdentityPanel), "Opacity", FadeOutExpression);
            Animate(ElementCompositionPreview.GetElementVisual(ActionsPanel), "Opacity", FadeOutExpression);

            CreateBannerBlur(compositor);

            CompactBar.Visibility = Visibility.Visible;
        }

        private void CreateBannerBlur(Compositor compositor)
        {
            if (!MemoryService.AllowsBannerBlur) return;

            try
            {
                _blurEffect = new GaussianBlurEffect
                {
                    Name = BlurEffectName,
                    BlurAmount = 0,
                    BorderMode = EffectBorderMode.Hard,
                    Optimization = EffectOptimization.Balanced,
                    Source = new CompositionEffectSourceParameter(BlurSourceName)
                };

                var brush = compositor.CreateEffectFactory(_blurEffect, new[] { BlurAmountProperty }).CreateBrush();
                brush.SetSourceParameter(BlurSourceName, compositor.CreateBackdropBrush());

                var visual = compositor.CreateSpriteVisual();
                visual.Brush = brush;

                var size = compositor.CreateExpressionAnimation(HostSizeExpression);
                size.SetReferenceParameter("host", _bannerVisual);
                visual.StartAnimation("Size", size);

                Animate(brush.Properties, BlurAmountProperty, BlurExpression);

                ElementCompositionPreview.SetElementChildVisual(BannerHost, visual);

                _blurBrush = brush;
                _blurVisual = visual;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: the banner blur could not start, using the veil - {ex.GetType().Name}: {ex.Message}");
                _blurBrush = null;
                _blurVisual = null;
                _blurEffect = null;
            }
        }

        private void RaiseHeaderAboveItems()
        {
            var presenter = VisualTreeHelper.GetParent(HeaderRoot) as UIElement;
            var container = presenter == null ? null : VisualTreeHelper.GetParent(presenter) as UIElement;
            if (container == null)
            {
                Debug.WriteLine("SocialProfileView: no header container found; pinned notes may draw over the header");
                return;
            }

            Canvas.SetZIndex(container, 1);
        }

        private void Animate(CompositionObject target, string property, string expression)
        {
            var animation = target.Compositor.CreateExpressionAnimation(expression);
            animation.SetReferenceParameter("scroll", _scrollProperties);
            animation.SetReferenceParameter("props", _collapseProperties);
            target.StartAnimation(property, animation);
        }

        private void CollapseMetrics_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                UpdateCollapseMetrics();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: could not measure the collapsing header - {ex.Message}");
            }
        }

        private void UpdateCollapseMetrics()
        {
            UpdateBannerThemes();

            if (_collapseProperties == null) return;

            var bannerHeight = BannerHost.ActualHeight;
            var barHeight = CompactBar.ActualHeight;
            var avatarSize = AvatarPicture.ActualHeight;
            var tabTop = HeaderRoot.ActualHeight - TabsHeight();
            var pin = tabTop - barHeight - _titleBarInset;
            if (bannerHeight <= 0 || barHeight <= 0 || avatarSize <= 0 || pin <= 0) return;

            _pinOffset = pin;

            var avatarRestTop = bannerHeight + AvatarPicture.Margin.Top;
            var avatarPinnedTop = _pinOffset + _titleBarInset + (barHeight - CompactAvatarSize) / 2;

            _collapseProperties.InsertScalar("pin", (float)_pinOffset);
            _collapseProperties.InsertScalar("bannerTravel", (float)Math.Max(0, tabTop - bannerHeight));
            _collapseProperties.InsertScalar("avatarTravel", (float)(avatarPinnedTop - avatarRestTop));
            _collapseProperties.InsertScalar("avatarScale", (float)(CompactAvatarSize / avatarSize));
            _collapseProperties.InsertScalar("compactRise", (float)(barHeight / 2));

            _bannerVisual.CenterPoint = new Vector3((float)(BannerHost.ActualWidth / 2), (float)bannerHeight, 0);

            ApplyFooterHeight();
            UpdateHeaderInput();
        }

        private double TabsHeight()
        {
            return TabBar.IsShown() ? TabBar.ActualHeight : 0;
        }

        private double CompactBarHeight()
        {
            return CompactBar.ActualHeight > 0 ? CompactBar.ActualHeight : CompactBar.MinHeight;
        }

        private void ApplyBannerHeight(double width)
        {
            if (width <= 0) return;

            var height = Math.Round(width / BannerAspectRatio);
            if (_collapseProperties != null || ExtendsUnderTitleBar)
            {
                height = Math.Max(height, Math.Ceiling(_titleBarInset + CompactBarHeight()));
            }

            if (double.IsNaN(BannerHost.Height) || Math.Abs(BannerHost.Height - height) >= 1)
            {
                BannerHost.Height = height;
            }
        }

        private void UpdateBannerThemes()
        {
            var rest = ElementTheme.Default;
            var pinned = ElementTheme.Default;

            var bannerHeight = BannerHost.ActualHeight;
            if (_bannerPixels != null && bannerHeight > 0)
            {
                var restBottom = _titleBarInset / bannerHeight;
                var pinnedTop = 1 - (_titleBarInset + CompactBarHeight()) / bannerHeight;

                rest = ThemeForText(BlurhashImage.AverageColor(_bannerPixels, BlurhashPixelWidth, BlurhashPixelHeight, 0, restBottom));
                pinned = ThemeForText(BlurhashImage.AverageColor(_bannerPixels, BlurhashPixelWidth, BlurhashPixelHeight, pinnedTop, 1));
            }

            if (rest == _bannerRestTheme && pinned == _bannerPinnedTheme) return;

            _bannerRestTheme = rest;
            _bannerPinnedTheme = pinned;
            CompactBar.RequestedTheme = _effectsEnabled ? _bannerPinnedTheme : ElementTheme.Default;
            UpdateTitleBarTheme();
        }

        private static ElementTheme ThemeForText(Windows.UI.Color? background)
        {
            if (!background.HasValue) return ElementTheme.Default;

            return ColorHelper.ContrastingForeground(background.Value) == Windows.UI.Colors.White
                   ? ElementTheme.Dark
                   : ElementTheme.Light;
        }

        private void View_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                UpdateBannerSpan();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: could not follow the window width - {ex.Message}");
            }
        }

        private void UpdateBannerSpan()
        {
            var spans = NotesList.ActualWidth >= ActualWidth - 1;
            if (spans == _bannerSpansWindow) return;

            _bannerSpansWindow = spans;
            UpdateTitleBarTheme();
        }

        private void UpdateTitleBarTheme()
        {
            ElementTheme theme;
            if (!_bannerSpansWindow)
            {
                theme = ElementTheme.Default;
            }
            else if (CollapseProgress() < TitleBarSwitchProgress)
            {
                theme = _bannerRestTheme;
            }
            else
            {
                theme = _effectsEnabled ? _bannerPinnedTheme : ElementTheme.Default;
            }

            if (theme == _titleBarTheme) return;

            _titleBarTheme = theme;
            RaiseTitleBarChanged();
        }

        private double CollapseProgress()
        {
            if (_collapseProperties == null || _scroller == null || _pinOffset <= 0) return 0;

            return Math.Max(0, Math.Min(1, _scroller.VerticalOffset / _pinOffset));
        }

        private void NotesList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                ApplyFooterHeight();
                UpdateBannerSpan();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: could not size the list footer - {ex.Message}");
            }
        }

        private void ApplyFooterHeight()
        {
            var minHeight = FooterMinHeight;
            if (_collapseProperties != null && _scroller != null && _state != ListState.Ready)
            {
                minHeight = Math.Max(minHeight, _scroller.ViewportHeight - CompactBar.ActualHeight - TabsHeight() - _titleBarInset);
            }

            if (Math.Abs(FooterHost.MinHeight - minHeight) >= 1)
            {
                FooterHost.MinHeight = minHeight;
            }
        }

        private void Scroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            try
            {
                UpdateHeaderInput();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: could not update the header's input - {ex.Message}");
            }
        }

        private void UpdateHeaderInput()
        {
            if (_collapseProperties == null || _scroller == null || _pinOffset <= 0) return;

            var progress = _scroller.VerticalOffset / _pinOffset;

            var compactInteractive = progress >= CompactBarInteractiveProgress;
            if (CompactBar.IsHitTestVisible != compactInteractive)
            {
                CompactBar.IsHitTestVisible = compactInteractive;
            }

            var fullInteractive = progress < FadedOutProgress;
            if (ActionsPanel.IsHitTestVisible != fullInteractive)
            {
                ActionsPanel.IsHitTestVisible = fullInteractive;
                IdentityPanel.IsHitTestVisible = fullInteractive;
            }

            UpdateTitleBarTheme();
        }

        private bool IsHeaderPinned()
        {
            return _collapseProperties != null
                   && _scroller != null
                   && _pinOffset > 0
                   && _scroller.VerticalOffset >= _pinOffset - 1;
        }

        private void RepinHeader()
        {
            try
            {
                NotesList.UpdateLayout();
                _scroller.ChangeView(null, _pinOffset, null, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: could not keep the header pinned - {ex.Message}");
            }
        }

        private void CompactBar_Tapped(object sender, TappedRoutedEventArgs e)
        {
            try
            {
                e.Handled = true;
                if (_scroller != null) _scroller.ChangeView(null, 0, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialProfileView: could not scroll back to the top - {ex.Message}");
            }
        }
    }
}
