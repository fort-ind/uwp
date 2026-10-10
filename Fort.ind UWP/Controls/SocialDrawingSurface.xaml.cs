using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Input.Inking;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;

namespace Fort.ind_UWP
{
    public enum DrawingAspect
    {
        Landscape,
        Wide,
        Square,
        Portrait
    }

    public enum DrawingBackground
    {
        White,
        Black,
        Tint,
        Transparent
    }

    public sealed class SocialDrawingResult
    {
        public SocialDrawingResult(byte[] bytes, string name, string contentType)
        {
            Bytes = bytes;
            Name = name;
            ContentType = contentType;
        }

        public byte[] Bytes { get; private set; }

        public string Name { get; private set; }

        public string ContentType { get; private set; }
    }

    public sealed partial class SocialDrawingSurface : UserControl
    {
        private const double LogicalLongSide = 800;

        private const double ExportScale = 2;

        private const uint MarkupMaximumSide = 2560;

        private const double MarkupLogicalLongSide = 1000;

        private const string FallbackTint = "#2D1B69";

        private readonly InkHistory _history;

        private readonly List<Point> _lassoPoints = new List<Point>();

        private Polyline _lasso;

        private Rectangle _selectionBox;

        private Rect _selection = Rect.Empty;

        private bool _moving;

        private Point _lastMove;

        private Point _moveTotal;

        private SoftwareBitmap _photo;

        private bool _photoJpeg;

        private string _photoName;

        private DrawingAspect _aspect;

        private DrawingBackground _background;

        private bool _exporting;

        private int _appliedVersion = -1;

        public SocialDrawingSurface()
        {
            this.InitializeComponent();

            var presenter = Ink.InkPresenter;
            _history = new InkHistory(presenter.StrokeContainer);
            _history.Changed += History_Changed;

            presenter.StrokesCollected += InkPresenter_StrokesCollected;
            presenter.StrokesErased += InkPresenter_StrokesErased;
            presenter.StrokeInput.StrokeStarted += StrokeInput_StrokeStarted;
            presenter.UnprocessedInput.PointerEntered += UnprocessedInput_PointerEntered;
            presenter.UnprocessedInput.PointerPressed += UnprocessedInput_PointerPressed;
            presenter.UnprocessedInput.PointerMoved += UnprocessedInput_PointerMoved;
            presenter.UnprocessedInput.PointerReleased += UnprocessedInput_PointerReleased;

            Paper.AddHandler(PointerEnteredEvent, new PointerEventHandler(Paper_PointerEntered), true);

            AddAccelerator(VirtualKey.Z, VirtualKeyModifiers.Control, (s, e) => Undo());
            AddAccelerator(VirtualKey.Y, VirtualKeyModifiers.Control, (s, e) => Redo());
            AddAccelerator(VirtualKey.Delete, VirtualKeyModifiers.None, (s, e) => DeleteSelection());
            AddAccelerator(VirtualKey.Escape, VirtualKeyModifiers.None, (s, e) => CancelSoon());

            Loaded += SocialDrawingSurface_Loaded;
        }

        public event EventHandler<SocialDrawingResult> Attached;

        public event EventHandler Cancelled;

        public bool IsMarkup
        {
            get { return _photo != null; }
        }

        private bool HasInk
        {
            get { return Ink.InkPresenter.StrokeContainer.GetStrokes().Count > 0; }
        }

        private void AddAccelerator(VirtualKey key, VirtualKeyModifiers modifiers, Action<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> action)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (sender, args) =>
            {
                try
                {
                    args.Handled = true;
                    action(sender, args);
                }
                catch (Exception ex)
                {
                    AppLog.Error("SocialDrawingSurface: shortcut failed", ex);
                }
            };
            KeyboardAccelerators.Add(accelerator);
            KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        }

        private void SocialDrawingSurface_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var eraser = Toolbar.GetToolButton(InkToolbarTool.Eraser) as InkToolbarEraserButton;
                if (eraser != null) eraser.IsClearAllVisible = false;
                ApplyInputTypes();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialDrawingSurface: could not set up the toolbar", ex);
            }
        }

        public void OpenBlank()
        {
            Reset();
            _photo = null;
            _photoName = null;
            Photo.Source = null;

            _aspect = InkSettingsService.DrawingAspect;
            _background = InkSettingsService.DrawingBackground;
            BackgroundMenu.Visibility = Visibility.Visible;
            AspectMenu.Visibility = Visibility.Visible;
            AttachLabel.Text = LocalizedStrings.Get("InkDrawAttach");

            ApplyAspect();
            ApplyBackground();
            Begin();
        }

        public async Task<bool> OpenPhotoAsync(IRandomAccessStream stream, string name, string contentType)
        {
            Reset();

            var photo = await InkExport.DecodePhotoAsync(stream, MarkupMaximumSide);
            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(photo);

            _photo = photo;
            _photoName = name;
            _photoJpeg = !string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase);
            Photo.Source = source;

            var longest = Math.Max(photo.PixelWidth, photo.PixelHeight);
            Paper.Width = Math.Round(photo.PixelWidth * MarkupLogicalLongSide / longest);
            Paper.Height = Math.Round(photo.PixelHeight * MarkupLogicalLongSide / longest);
            PaperBackground.Background = new SolidColorBrush(Colors.Transparent);

            BackgroundMenu.Visibility = Visibility.Collapsed;
            AspectMenu.Visibility = Visibility.Collapsed;
            AttachLabel.Text = LocalizedStrings.Get("InkDrawDone");

            Begin();
            return true;
        }

        private void Begin()
        {
            ApplyInputTypes();
            UpdateButtons();

            var pen = Toolbar.GetToolButton(InkToolbarTool.BallpointPen);
            if (pen != null)
            {
                Toolbar.ActiveTool = pen;
                pen.Focus(FocusState.Programmatic);
            }
        }

        public void Reset()
        {
            ClearSelection();
            Ink.InkPresenter.StrokeContainer.Clear();
            _history.Reset();
            SetExporting(false);
        }

        private void ApplyInputTypes()
        {
            _appliedVersion = InkSettingsService.Version;
            Ink.InkPresenter.InputDeviceTypes = InkSettingsService.InkInputTypes;
        }

        private void Paper_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                NotePointer(e.Pointer.PointerDeviceType);
                if (_appliedVersion != InkSettingsService.Version) ApplyInputTypes();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialDrawingSurface: could not refresh the input types", ex);
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
            NotePointer(args.CurrentPoint.PointerDevice.PointerDeviceType);
            ClearSelection();
        }

        private void InkPresenter_StrokesCollected(InkPresenter sender, InkStrokesCollectedEventArgs args)
        {
            _history.RecordAdded(args.Strokes);
        }

        private void InkPresenter_StrokesErased(InkPresenter sender, InkStrokesErasedEventArgs args)
        {
            ClearSelection();
            _history.RecordRemoved(args.Strokes);
        }

        private void History_Changed(object sender, EventArgs e)
        {
            UpdateButtons();
        }

        private void Undo()
        {
            ClearSelection();
            _history.Undo();
        }

        private void Redo()
        {
            ClearSelection();
            _history.Redo();
        }

        private void UpdateButtons()
        {
            UndoButton.IsEnabled = _history.CanUndo && !_exporting;
            RedoButton.IsEnabled = _history.CanRedo && !_exporting;
            ClearAllItem.IsEnabled = HasInk;
            AttachButton.IsEnabled = HasInk && !_exporting;
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            Undo();
        }

        private void RedoButton_Click(object sender, RoutedEventArgs e)
        {
            Redo();
        }

        private void Toolbar_ActiveToolChanged(InkToolbar sender, object args)
        {
            try
            {
                var lasso = Toolbar.ActiveTool == LassoButton;
                Ink.InkPresenter.InputProcessingConfiguration.Mode = lasso ? InkInputProcessingMode.None : InkInputProcessingMode.Inking;
                if (!lasso) ClearSelection();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialDrawingSurface: could not switch tools", ex);
            }
        }

        private void UnprocessedInput_PointerPressed(InkUnprocessedInput sender, PointerEventArgs args)
        {
            try
            {
                if (Toolbar.ActiveTool != LassoButton) return;

                var point = args.CurrentPoint.Position;
                if (!_selection.IsEmpty && _selection.Contains(point))
                {
                    _moving = true;
                    _lastMove = point;
                    _moveTotal = new Point(0, 0);
                    return;
                }

                ClearSelection();
                _lassoPoints.Clear();
                _lassoPoints.Add(point);
                _lasso = new Polyline
                {
                    Stroke = new SolidColorBrush(AccentColor()),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 5, 2 }
                };
                _lasso.Points.Add(point);
                SelectionLayer.Children.Add(_lasso);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialDrawingSurface: could not start a selection", ex);
            }
        }

        private void UnprocessedInput_PointerMoved(InkUnprocessedInput sender, PointerEventArgs args)
        {
            try
            {
                var point = args.CurrentPoint.Position;
                if (_moving)
                {
                    var offset = new Point(point.X - _lastMove.X, point.Y - _lastMove.Y);
                    _lastMove = point;
                    _moveTotal = new Point(_moveTotal.X + offset.X, _moveTotal.Y + offset.Y);
                    Ink.InkPresenter.StrokeContainer.MoveSelected(offset);
                    _selection = new Rect(_selection.X + offset.X, _selection.Y + offset.Y, _selection.Width, _selection.Height);
                    ShowSelection();
                    return;
                }

                if (_lasso == null) return;
                _lassoPoints.Add(point);
                _lasso.Points.Add(point);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialDrawingSurface: could not follow the selection", ex);
            }
        }

        private void UnprocessedInput_PointerReleased(InkUnprocessedInput sender, PointerEventArgs args)
        {
            try
            {
                var container = Ink.InkPresenter.StrokeContainer;
                if (_moving)
                {
                    _moving = false;
                    var moved = container.GetStrokes().Where(stroke => stroke.Selected).ToList();
                    _history.RecordMoved(moved, _moveTotal);
                    return;
                }

                if (_lasso == null) return;

                _lassoPoints.Add(args.CurrentPoint.Position);
                SelectionLayer.Children.Remove(_lasso);
                _lasso = null;

                var bounds = container.SelectWithPolyLine(_lassoPoints);
                _lassoPoints.Clear();
                if (bounds.Width <= 0 || bounds.Height <= 0) return;

                _selection = bounds;
                ShowSelection();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialDrawingSurface: could not finish the selection", ex);
            }
        }

        private void ShowSelection()
        {
            if (_selectionBox == null)
            {
                _selectionBox = new Rectangle
                {
                    Stroke = new SolidColorBrush(AccentColor()),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
            }

            if (!SelectionLayer.Children.Contains(_selectionBox)) SelectionLayer.Children.Add(_selectionBox);
            _selectionBox.Width = _selection.Width;
            _selectionBox.Height = _selection.Height;
            Canvas.SetLeft(_selectionBox, _selection.X);
            Canvas.SetTop(_selectionBox, _selection.Y);
        }

        private void ClearSelection()
        {
            _selection = Rect.Empty;
            _moving = false;
            _lasso = null;
            _lassoPoints.Clear();
            SelectionLayer.Children.Clear();

            foreach (var stroke in Ink.InkPresenter.StrokeContainer.GetStrokes())
            {
                stroke.Selected = false;
            }
        }

        private void DeleteSelection()
        {
            var container = Ink.InkPresenter.StrokeContainer;
            var selected = container.GetStrokes().Where(stroke => stroke.Selected).ToList();
            if (selected.Count == 0) return;

            _history.RecordRemoved(selected);
            container.DeleteSelected();
            ClearSelection();
            UpdateButtons();
        }

        private void ClearAllItem_Click(object sender, RoutedEventArgs e)
        {
            var container = Ink.InkPresenter.StrokeContainer;
            var strokes = container.GetStrokes().ToList();
            if (strokes.Count == 0) return;

            ClearSelection();
            _history.RecordRemoved(strokes);
            container.Clear();
            UpdateButtons();
        }

        private static Color AccentColor()
        {
            return new UISettings().GetColorValue(UIColorType.Accent);
        }

        private void BackgroundItem_Click(object sender, RoutedEventArgs e)
        {
            DrawingBackground background;
            var tag = (sender as FrameworkElement)?.Tag as string;
            if (tag == null || !Enum.TryParse(tag, out background)) return;

            _background = background;
            InkSettingsService.DrawingBackground = background;
            ApplyBackground();
        }

        private void ApplyBackground()
        {
            WhiteItem.IsChecked = _background == DrawingBackground.White;
            BlackItem.IsChecked = _background == DrawingBackground.Black;
            TintItem.IsChecked = _background == DrawingBackground.Tint;
            TransparentItem.IsChecked = _background == DrawingBackground.Transparent;

            PaperBackground.Background = new SolidColorBrush(BackgroundColor());
            if (_background == DrawingBackground.Black || _background == DrawingBackground.Tint) PreferWhitePens();
        }

        private Color BackgroundColor()
        {
            switch (_background)
            {
                case DrawingBackground.Black:
                    return Colors.Black;
                case DrawingBackground.Tint:
                    var tag = AppearanceService.TintTag;
                    Color tint;
                    return tag != AppConstants.ThemeDefault && ColorHelper.TryHexToColor(tag, out tint) ? tint : ColorHelper.HexToColor(FallbackTint);
                case DrawingBackground.Transparent:
                    return Colors.Transparent;
                default:
                    return Colors.White;
            }
        }

        private void PreferWhitePens()
        {
            foreach (var tool in new[] { InkToolbarTool.BallpointPen, InkToolbarTool.Pencil })
            {
                var pen = Toolbar.GetToolButton(tool) as InkToolbarPenButton;
                if (pen == null || pen.Palette == null) continue;

                var current = pen.SelectedBrush as SolidColorBrush;
                if (current == null || current.Color != Colors.Black) continue;

                var white = pen.Palette.Select((brush, index) => new { Brush = brush as SolidColorBrush, Index = index })
                                       .FirstOrDefault(entry => entry.Brush != null && entry.Brush.Color == Colors.White);
                if (white != null) pen.SelectedBrushIndex = white.Index;
            }
        }

        private void AspectItem_Click(object sender, RoutedEventArgs e)
        {
            DrawingAspect aspect;
            var tag = (sender as FrameworkElement)?.Tag as string;
            if (tag == null || !Enum.TryParse(tag, out aspect)) return;

            _aspect = aspect;
            InkSettingsService.DrawingAspect = aspect;
            ApplyAspect();
        }

        private void ApplyAspect()
        {
            LandscapeItem.IsChecked = _aspect == DrawingAspect.Landscape;
            WideItem.IsChecked = _aspect == DrawingAspect.Wide;
            SquareItem.IsChecked = _aspect == DrawingAspect.Square;
            PortraitItem.IsChecked = _aspect == DrawingAspect.Portrait;

            var size = LogicalSize(_aspect);
            Paper.Width = size.Width;
            Paper.Height = size.Height;
        }

        private static Size LogicalSize(DrawingAspect aspect)
        {
            switch (aspect)
            {
                case DrawingAspect.Wide:
                    return new Size(LogicalLongSide, LogicalLongSide * 9 / 16);
                case DrawingAspect.Square:
                    return new Size(LogicalLongSide, LogicalLongSide);
                case DrawingAspect.Portrait:
                    return new Size(LogicalLongSide * 3 / 4, LogicalLongSide);
                default:
                    return new Size(LogicalLongSide, LogicalLongSide * 3 / 4);
            }
        }

        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await CancelAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialDrawingSurface: cancel failed", ex);
            }
        }

        private async void CancelSoon()
        {
            try
            {
                await CancelAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialDrawingSurface: cancel failed", ex);
            }
        }

        public async Task<bool> CancelAsync()
        {
            if (_exporting) return false;

            if (HasInk)
            {
                var discard = await DialogService.ShowConfirmAsync(this,
                                                                   LocalizedStrings.Get(IsMarkup ? "InkMarkupDiscardTitle" : "InkDrawDiscardTitle"),
                                                                   LocalizedStrings.Get("InkDrawDiscardBody"),
                                                                   LocalizedStrings.Get("InkDrawDiscardConfirm"),
                                                                   LocalizedStrings.Get("InkDrawKeepDrawing"),
                                                                   ContentDialogButton.Close);
                if (!discard) return false;
            }

            Reset();
            Cancelled?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private async void AttachButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await AttachAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialDrawingSurface: could not attach the drawing", ex);
                SetExporting(false);
                await DialogService.ShowMessageAsync(this,
                                                     LocalizedStrings.Get("InkDrawFailedTitle"),
                                                     LocalizedStrings.Get("SocialActionErrorGeneric"),
                                                     LocalizedStrings.Get("DialogOk"));
            }
        }

        private async Task AttachAsync()
        {
            if (_exporting || !HasInk) return;

            ClearSelection();
            SetExporting(true);

            var strokes = Ink.InkPresenter.StrokeContainer.GetStrokes();
            var canvasSize = new Size(Paper.Width, Paper.Height);
            byte[] bytes;
            string name;
            string contentType;

            if (IsMarkup)
            {
                bytes = await InkExport.RenderAsync(strokes, canvasSize, _photo.PixelWidth, _photo.PixelHeight, Colors.Transparent, _photo, _photoJpeg);
                contentType = _photoJpeg ? "image/jpeg" : "image/png";
                name = LocalizedStrings.Format("InkMarkupFileNameFormat", System.IO.Path.GetFileNameWithoutExtension(_photoName ?? "image"))
                       + (_photoJpeg ? ".jpg" : ".png");
            }
            else
            {
                bytes = await InkExport.RenderAsync(strokes, canvasSize,
                                                    (int)Math.Round(canvasSize.Width * ExportScale),
                                                    (int)Math.Round(canvasSize.Height * ExportScale),
                                                    BackgroundColor(), null, false);
                contentType = "image/png";
                name = LocalizedStrings.Format("InkDrawingFileNameFormat",
                                               DateTimeOffset.Now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture)) + ".png";
            }

            var result = new SocialDrawingResult(bytes, name, contentType);
            Reset();
            Attached?.Invoke(this, result);
        }

        private void SetExporting(bool exporting)
        {
            _exporting = exporting;
            AttachRing.IsActive = exporting;
            AttachRing.Visibility = exporting ? Visibility.Visible : Visibility.Collapsed;
            AttachLabel.Opacity = exporting ? 0 : 1;
            CancelButton.IsEnabled = !exporting;
            Toolbar.IsEnabled = !exporting;
            UpdateButtons();
        }
    }
}
