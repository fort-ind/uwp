using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Windows.Devices.Input;
using Windows.UI.Core;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Fort.ind_UWP
{
    public enum HandwritingPadKey
    {
        Space,
        Backspace,
        Enter
    }

    public sealed partial class SocialHandwritingPad : UserControl
    {
        private const int AlternateLimit = 5;

        private readonly DispatcherTimer _commitTimer;

        private bool _committing;

        private int _appliedVersion = -1;

        public SocialHandwritingPad()
        {
            this.InitializeComponent();

            _commitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AppConstants.InkCommitDelayMilliseconds) };
            _commitTimer.Tick += CommitTimer_Tick;

            var presenter = Ink.InkPresenter;
            presenter.StrokeInput.StrokeStarted += StrokeInput_StrokeStarted;
            presenter.StrokesCollected += InkPresenter_StrokesCollected;
            presenter.UnprocessedInput.PointerEntered += UnprocessedInput_PointerEntered;

            WritingArea.AddHandler(PointerEnteredEvent, new PointerEventHandler(WritingArea_PointerEntered), true);
            Loaded += (sender, e) => ApplyInputTypes();
        }

        public event EventHandler<IReadOnlyList<InkRecognitionWord>> Recognized;

        public event EventHandler<HandwritingPadKey> KeyPressed;

        public event EventHandler<string> AlternateChosen;

        public event EventHandler CloseRequested;

        public void Reset()
        {
            _commitTimer.Stop();
            Ink.InkPresenter.StrokeContainer.Clear();
            ClearAlternates();
            HintText.Visibility = Visibility.Visible;
        }

        public void ShowAlternates(IReadOnlyList<string> alternates)
        {
            AlternatesPanel.Children.Clear();
            if (alternates == null) return;

            foreach (var alternate in alternates.Take(AlternateLimit))
            {
                var chip = new Button
                {
                    Content = alternate,
                    Tag = alternate,
                    Padding = new Thickness(10, 4, 10, 4),
                    MinWidth = 0,
                    AllowFocusOnInteraction = false
                };
                AutomationProperties.SetName(chip, LocalizedStrings.Format("InkPadAlternateFormat", alternate));
                chip.Click += Alternate_Click;
                AlternatesPanel.Children.Add(chip);
            }
        }

        public void ClearAlternates()
        {
            AlternatesPanel.Children.Clear();
        }

        private void ApplyInputTypes()
        {
            _appliedVersion = InkSettingsService.Version;
            Ink.InkPresenter.InputDeviceTypes = InkSettingsService.InkInputTypes;
        }

        private void WritingArea_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                NotePointer(e.Pointer.PointerDeviceType);
                if (_appliedVersion != InkSettingsService.Version) ApplyInputTypes();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialHandwritingPad: could not refresh the input types - {ex.Message}");
            }
        }

        private void UnprocessedInput_PointerEntered(InkUnprocessedInput sender, PointerEventArgs args)
        {
            NotePointer(args.CurrentPoint.PointerDevice.PointerDeviceType);
        }

        private void NotePointer(PointerDeviceType type)
        {
            if (type != PointerDeviceType.Pen || InkSettingsService.PenSeen) return;

            InkSettingsService.NotePenSeen();
            ApplyInputTypes();
        }

        private void StrokeInput_StrokeStarted(InkStrokeInput sender, PointerEventArgs args)
        {
            _commitTimer.Stop();
            HintText.Visibility = Visibility.Collapsed;
            NotePointer(args.CurrentPoint.PointerDevice.PointerDeviceType);
        }

        private void InkPresenter_StrokesCollected(InkPresenter sender, InkStrokesCollectedEventArgs args)
        {
            _commitTimer.Stop();
            _commitTimer.Start();
        }

        private void CommitTimer_Tick(object sender, object e)
        {
            _commitTimer.Stop();
            Commit();
        }

        private async void Commit()
        {
            if (_committing) return;

            var container = Ink.InkPresenter.StrokeContainer;
            var strokes = container.GetStrokes().ToList();
            if (strokes.Count == 0) return;

            _committing = true;
            try
            {
                var words = await InkRecognition.RecognizeAsync(strokes);

                foreach (var stroke in container.GetStrokes())
                {
                    stroke.Selected = strokes.Contains(stroke);
                }
                container.DeleteSelected();

                if (words.Count == 0)
                {
                    AutomationHelper.AnnounceStatus(this, LocalizedStrings.Get("InkNotRecognized"), "InkNotRecognized");
                }
                else
                {
                    Recognized?.Invoke(this, words);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialHandwritingPad: could not recognise the ink - {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _committing = false;
            }

            try
            {
                if (container.GetStrokes().Count > 0) _commitTimer.Start();
                else HintText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialHandwritingPad: could not finish the commit - {ex.Message}");
            }
        }

        private void Alternate_Click(object sender, RoutedEventArgs e)
        {
            var alternate = (sender as FrameworkElement)?.Tag as string;
            if (!string.IsNullOrEmpty(alternate)) AlternateChosen?.Invoke(this, alternate);
        }

        private void SpaceButton_Click(object sender, RoutedEventArgs e)
        {
            KeyPressed?.Invoke(this, HandwritingPadKey.Space);
        }

        private void BackspaceButton_Click(object sender, RoutedEventArgs e)
        {
            KeyPressed?.Invoke(this, HandwritingPadKey.Backspace);
        }

        private void EnterButton_Click(object sender, RoutedEventArgs e)
        {
            KeyPressed?.Invoke(this, HandwritingPadKey.Enter);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
