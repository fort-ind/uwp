using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    public sealed class InlineInkLayer
    {
        private const double HorizontalReach = 8;

        private const double VerticalReach = 12;

        private const double SingleLineMinimumHeight = 72;

        private const double TapSize = 6;

        private const double StrikeMinimumWidth = 24;

        private const double StrikeAspect = 3;

        private const double CaretMinimumSize = 6;

        private const double CaretMaximumSize = 64;

        private const double InkThickness = 2.5;

        private static readonly bool s_popupXamlRootSupported =
            ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Controls.Primitives.Popup", "XamlRoot");

        private readonly TextBox _box;

        private readonly Action _written;

        private readonly DispatcherTimer _commitTimer;

        private Popup _popup;

        private InkCanvas _canvas;

        private ScrollViewer _scroller;

        private Point _origin;

        private bool _committing;

        private bool _closeAfterCommit;

        public InlineInkLayer(TextBox box, Action written)
        {
            _box = box;
            _written = written;
            _commitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AppConstants.InkCommitDelayMilliseconds) };
            _commitTimer.Tick += CommitTimer_Tick;
        }

        public bool IsOpen
        {
            get { return _popup != null && _popup.IsOpen; }
        }

        public void Show()
        {
            if (IsOpen || _box.IsReadOnly || !_box.IsEnabled || _box.ActualWidth <= 0) return;

            EnsurePopup();
            Place();
            ApplyAttributes();
            WatchScroller();
            _closeAfterCommit = false;
            _popup.IsOpen = true;
        }

        public void Close()
        {
            _commitTimer.Stop();
            _closeAfterCommit = false;
            UnwatchScroller();

            if (_canvas != null) _canvas.InkPresenter.StrokeContainer.Clear();
            if (_popup != null) _popup.IsOpen = false;
        }

        private void EnsurePopup()
        {
            if (_popup != null) return;

            _canvas = new InkCanvas();
            _canvas.InkPresenter.StrokeInput.StrokeStarted += StrokeInput_StrokeStarted;
            _canvas.InkPresenter.StrokesCollected += InkPresenter_StrokesCollected;
            _canvas.InkPresenter.UnprocessedInput.PointerExited += UnprocessedInput_PointerExited;
            _canvas.InkPresenter.UnprocessedInput.PointerLost += UnprocessedInput_PointerLost;

            var host = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            host.Children.Add(_canvas);
            AutomationProperties.SetAccessibilityView(host, AccessibilityView.Raw);

            _popup = new Popup
            {
                Child = host,
                IsLightDismissEnabled = false
            };
            if (s_popupXamlRootSupported) _popup.XamlRoot = _box.XamlRoot;
        }

        private void Place()
        {
            var width = _box.ActualWidth;
            var height = _box.ActualHeight;
            var vertical = VerticalReach;
            if (!_box.AcceptsReturn && height + 2 * vertical < SingleLineMinimumHeight)
            {
                vertical = (SingleLineMinimumHeight - height) / 2;
            }

            var topLeft = _box.TransformToVisual(null).TransformPoint(new Point(0, 0));
            _origin = new Point(HorizontalReach, vertical);

            var host = (Grid)_popup.Child;
            host.Width = width + 2 * HorizontalReach;
            host.Height = height + 2 * vertical;
            _popup.HorizontalOffset = topLeft.X - HorizontalReach;
            _popup.VerticalOffset = topLeft.Y - vertical;
        }

        private void ApplyAttributes()
        {
            var presenter = _canvas.InkPresenter;
            presenter.InputDeviceTypes = CoreInputDeviceTypes.Pen;

            var color = Colors.Black;
            var brush = _box.Foreground as SolidColorBrush;
            if (brush != null) color = brush.Color;
            else if (_box.ActualTheme == ElementTheme.Dark) color = Colors.White;

            var attributes = new InkDrawingAttributes
            {
                Color = color,
                Size = new Size(InkThickness, InkThickness),
                FitToCurve = true,
                IgnorePressure = false
            };
            presenter.UpdateDefaultDrawingAttributes(attributes);
        }

        private void WatchScroller()
        {
            UnwatchScroller();
            _scroller = VisualTreeSearch.FindAncestor<ScrollViewer>(_box);
            if (_scroller != null) _scroller.ViewChanging += Scroller_ViewChanging;
        }

        private void UnwatchScroller()
        {
            if (_scroller == null) return;
            _scroller.ViewChanging -= Scroller_ViewChanging;
            _scroller = null;
        }

        private void Scroller_ViewChanging(object sender, ScrollViewerViewChangingEventArgs e)
        {
            Close();
        }

        private void StrokeInput_StrokeStarted(InkStrokeInput sender, PointerEventArgs args)
        {
            _commitTimer.Stop();
            _closeAfterCommit = false;
        }

        private void InkPresenter_StrokesCollected(InkPresenter sender, InkStrokesCollectedEventArgs args)
        {
            _commitTimer.Stop();
            _commitTimer.Start();
        }

        private void UnprocessedInput_PointerExited(InkUnprocessedInput sender, PointerEventArgs args)
        {
            LeaveSoon();
        }

        private void UnprocessedInput_PointerLost(InkUnprocessedInput sender, PointerEventArgs args)
        {
            LeaveSoon();
        }

        private void LeaveSoon()
        {
            try
            {
                if (_canvas.InkPresenter.StrokeContainer.GetStrokes().Count == 0 && !_committing)
                {
                    Close();
                    return;
                }

                _closeAfterCommit = true;
                if (!_committing)
                {
                    _commitTimer.Stop();
                    Commit();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("InlineInkLayer: could not close", ex);
            }
        }

        private void CommitTimer_Tick(object sender, object e)
        {
            _commitTimer.Stop();
            Commit();
        }

        private async void Commit()
        {
            if (_committing) return;

            var container = _canvas.InkPresenter.StrokeContainer;
            var strokes = container.GetStrokes().ToList();
            if (strokes.Count == 0)
            {
                if (_closeAfterCommit) Close();
                return;
            }

            _committing = true;
            try
            {
                var layout = new TextLayoutMap(_box);
                if (!TryGesture(strokes, layout))
                {
                    var words = await InkRecognition.RecognizeAsync(strokes);
                    var text = InkRecognition.Join(words);
                    if (text.Length > 0)
                    {
                        var bounds = UnionBounds(strokes);
                        var first = strokes[0].BoundingRect;
                        var anchor = ToBox(new Point(bounds.Left, first.Top + first.Height / 2));
                        InsertText(layout.IndexAt(anchor), text);
                    }
                    else
                    {
                        AutomationHelper.AnnounceStatus(_box, LocalizedStrings.Get("InkNotRecognized"), "InkNotRecognized");
                    }
                }

                RemoveStrokes(container, strokes);
            }
            catch (Exception ex)
            {
                AppLog.Error("InlineInkLayer: could not turn the ink into text", ex);
                RemoveStrokes(container, strokes);
            }
            finally
            {
                _committing = false;
            }

            try
            {
                if (container.GetStrokes().Count > 0) _commitTimer.Start();
                else if (_closeAfterCommit) Close();
            }
            catch (Exception ex)
            {
                AppLog.Error("InlineInkLayer: could not finish the commit", ex);
            }
        }

        private static void RemoveStrokes(InkStrokeContainer container, List<InkStroke> strokes)
        {
            foreach (var stroke in container.GetStrokes())
            {
                stroke.Selected = strokes.Contains(stroke);
            }

            container.DeleteSelected();
        }

        private bool TryGesture(List<InkStroke> strokes, TextLayoutMap layout)
        {
            if (strokes.Count != 1) return false;

            var stroke = strokes[0];
            var bounds = stroke.BoundingRect;

            if (bounds.Width < TapSize && bounds.Height < TapSize)
            {
                var point = ToBox(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
                PlaceCaret(layout.IndexAt(point));
                return true;
            }

            if (layout.Length == 0) return false;

            if (bounds.Width >= StrikeMinimumWidth && bounds.Width >= StrikeAspect * bounds.Height)
            {
                var top = ToBox(new Point(bounds.Left, bounds.Top));
                var bottom = ToBox(new Point(bounds.Right, bounds.Bottom));
                int start, end;
                if (layout.TryFindStruck(top.X, bottom.X, (top.Y + bottom.Y) / 2, out start, out end))
                {
                    DeleteWords(start, end);
                    return true;
                }

                return false;
            }

            Point apex;
            if (IsCaretShape(stroke, out apex))
            {
                var point = ToBox(apex);
                var line = layout.LineAbove(point);
                if (line < 0) return false;

                InsertSpace(layout.IndexAt(new Point(point.X, line)));
                return true;
            }

            return false;
        }

        private static bool IsCaretShape(InkStroke stroke, out Point apex)
        {
            apex = default(Point);

            var bounds = stroke.BoundingRect;
            if (bounds.Width < CaretMinimumSize || bounds.Height < CaretMinimumSize) return false;
            if (bounds.Width > CaretMaximumSize || bounds.Height > CaretMaximumSize) return false;

            var points = stroke.GetInkPoints();
            if (points.Count < 3) return false;

            var apexIndex = Enumerable.Range(0, points.Count).OrderBy(i => points[i].Position.Y).First();

            if (apexIndex < points.Count / 5 || apexIndex > points.Count * 4 / 5) return false;

            var start = points[0].Position;
            var end = points[points.Count - 1].Position;
            apex = points[apexIndex].Position;

            var drop = bounds.Height / 2;
            return start.Y - apex.Y >= drop && end.Y - apex.Y >= drop && start.X < apex.X && apex.X < end.X;
        }

        private static Rect UnionBounds(List<InkStroke> strokes)
        {
            var bounds = strokes[0].BoundingRect;
            for (int i = 1; i < strokes.Count; i++)
            {
                bounds.Union(strokes[i].BoundingRect);
            }

            return bounds;
        }

        private Point ToBox(Point canvasPoint)
        {
            return new Point(canvasPoint.X - _origin.X, canvasPoint.Y - _origin.Y);
        }

        private void InsertText(int index, string text)
        {
            var current = _box.Text ?? "";
            index = Math.Max(0, Math.Min(index, current.Length));

            var before = current.Substring(0, index);
            var after = current.Substring(index);
            if (before.Length > 0 && !char.IsWhiteSpace(before[before.Length - 1]) && !InkRecognition.StartsWithClosingPunctuation(text))
            {
                text = " " + text;
            }
            if (after.Length > 0 && !char.IsWhiteSpace(after[0]) && !InkRecognition.StartsWithClosingPunctuation(after))
            {
                text = text + " ";
            }

            ReplaceText(before + text + after, index + text.Length);
        }

        private void InsertSpace(int index)
        {
            var current = _box.Text ?? "";
            index = Math.Max(0, Math.Min(index, current.Length));
            ReplaceText(current.Substring(0, index) + " " + current.Substring(index), index + 1);
        }

        private void DeleteWords(int start, int end)
        {
            var current = _box.Text ?? "";
            while (start > 0 && !char.IsWhiteSpace(current[start - 1])) start--;
            while (end < current.Length && !char.IsWhiteSpace(current[end])) end++;

            if (end < current.Length && current[end] == ' ') end++;
            else if (start > 0 && current[start - 1] == ' ') start--;

            ReplaceText(current.Substring(0, start) + current.Substring(end), start);
        }

        private void ReplaceText(string text, int caret)
        {
            if (_box.MaxLength > 0 && text.Length > _box.MaxLength) return;

            _box.Text = text;
            PlaceCaret(caret);
            _written?.Invoke();
        }

        private void PlaceCaret(int index)
        {
            if (_box.FocusState == FocusState.Unfocused) _box.Focus(FocusState.Programmatic);
            _box.SelectionStart = Math.Max(0, Math.Min(index, (_box.Text ?? "").Length));
            _box.SelectionLength = 0;
        }

        private sealed class TextLayoutMap
        {
            private readonly List<Rect> _leading = new List<Rect>();

            private readonly List<double> _trailing = new List<double>();

            private readonly string _text;

            public TextLayoutMap(TextBox box)
            {
                _text = box.Text ?? "";
                for (int i = 0; i < _text.Length; i++)
                {
                    _leading.Add(box.GetRectFromCharacterIndex(i, false));
                    _trailing.Add(box.GetRectFromCharacterIndex(i, true).X);
                }
            }

            public int Length
            {
                get { return _text.Length; }
            }

            public int IndexAt(Point point)
            {
                if (_text.Length == 0) return 0;

                var last = _leading[_leading.Count - 1];
                if (point.Y > last.Bottom) return _text.Length;

                var lineTop = NearestLineTop(point.Y);
                var best = -1;
                var bestDistance = double.MaxValue;
                foreach (var i in Enumerable.Range(0, _text.Length).Where(index => SameLine(_leading[index].Top, lineTop)))
                {
                    var distance = Math.Abs(_leading[i].X - point.X);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = i;
                    }

                    if (IsLineBreak(_text[i])) continue;

                    distance = Math.Abs(_trailing[i] - point.X);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = i + 1;
                    }
                }

                return best < 0 ? _text.Length : best;
            }

            public double LineAbove(Point point)
            {
                var above = _leading.Where(rect => rect.Top <= point.Y).ToList();
                if (above.Count == 0) return -1;

                var line = above.OrderByDescending(rect => rect.Top).First();
                return line.Top + line.Height / 2;
            }

            public bool TryFindStruck(double left, double right, double y, out int start, out int end)
            {
                var struck = Enumerable.Range(0, _text.Length).Where(i => IsStruck(i, left, right, y)).ToList();
                start = struck.Count == 0 ? -1 : struck[0];
                end = struck.Count == 0 ? -1 : struck[struck.Count - 1] + 1;
                return struck.Count > 0;
            }

            private bool IsStruck(int index, double left, double right, double y)
            {
                if (char.IsWhiteSpace(_text[index])) return false;

                var rect = _leading[index];
                var center = (rect.X + _trailing[index]) / 2;
                return center >= left && center <= right && y >= rect.Top && y <= rect.Bottom;
            }

            private double NearestLineTop(double y)
            {
                var best = _leading[0].Top;
                var bestDistance = double.MaxValue;
                foreach (var rect in _leading)
                {
                    var distance = y < rect.Top ? rect.Top - y : y > rect.Bottom ? y - rect.Bottom : 0;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = rect.Top;
                    }
                }

                return best;
            }

            private static bool SameLine(double top, double lineTop)
            {
                return Math.Abs(top - lineTop) < 1;
            }

            private static bool IsLineBreak(char value)
            {
                return value == '\r' || value == '\n';
            }
        }
    }
}
