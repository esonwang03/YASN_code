using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using YASN.WindowLayout;

namespace YASN.Views
{
    /// <summary>
    /// A centred dialog that arranges every monitor as a proportional placeholder, the way the
    /// operating system's display-arrangement screen does. A short click inside the map repositions
    /// the target window to that point; dragging a rectangle moves and resizes it. The note's current
    /// bounds show as a ghost so the user can orient. Escape or a right-click cancels. The dialog
    /// result is the chosen <see cref="WindowRect"/> (physical-pixel left/top, DIP width/height), or
    /// null when cancelled.
    /// </summary>
    public sealed partial class QuickLayoutOverlayWindow : Window
    {
        private readonly Canvas mapCanvas;
        private readonly Rectangle selectionRectangle;
        private readonly Border warningBox;
        private readonly IReadOnlyList<QuickLayoutMonitor> monitors;
        private readonly WindowRect currentPhysicalBounds;
        private readonly double currentWidthDip;
        private readonly double currentHeightDip;
        private readonly double minWidthDip;
        private readonly double minHeightDip;

        private QuickLayoutMap? map;
        private Point? dragStart;

        /// <summary>
        /// Initializes the overlay for the XAML designer with a single placeholder monitor.
        /// </summary>
        public QuickLayoutOverlayWindow()
            : this(
                new[] { new QuickLayoutMonitor(new WindowRect(0, 0, 1920, 1080), 1.0) },
                new WindowRect(200, 150, 640, 400),
                640,
                400,
                200,
                120)
        {
        }

        /// <summary>
        /// Initializes the overlay for a target window of the given current bounds and the desktop's
        /// monitor arrangement.
        /// </summary>
        /// <param name="monitors">The monitors to draw, with physical bounds and per-monitor scaling.</param>
        /// <param name="currentPhysicalBounds">The note's current bounds, in physical pixels, drawn as a ghost.</param>
        /// <param name="currentWidthDip">The note's current width, in DIP, kept for a click reposition.</param>
        /// <param name="currentHeightDip">The note's current height, in DIP, kept for a click reposition.</param>
        /// <param name="minWidthDip">The note's minimum width, in DIP; smaller drags reposition only.</param>
        /// <param name="minHeightDip">The note's minimum height, in DIP; smaller drags reposition only.</param>
        public QuickLayoutOverlayWindow(
            IReadOnlyList<QuickLayoutMonitor> monitors,
            WindowRect currentPhysicalBounds,
            double currentWidthDip,
            double currentHeightDip,
            double minWidthDip,
            double minHeightDip)
        {
            this.monitors = monitors;
            this.currentPhysicalBounds = currentPhysicalBounds;
            this.currentWidthDip = currentWidthDip;
            this.currentHeightDip = currentHeightDip;
            this.minWidthDip = minWidthDip;
            this.minHeightDip = minHeightDip;
            InitializeComponent();

            mapCanvas = this.FindControl<Canvas>("MapCanvas")
                ?? throw new InvalidOperationException("MapCanvas was not found.");
            selectionRectangle = this.FindControl<Rectangle>("SelectionRectangle")
                ?? throw new InvalidOperationException("SelectionRectangle was not found.");
            warningBox = this.FindControl<Border>("WarningBox")
                ?? throw new InvalidOperationException("WarningBox was not found.");

            // The canvas only has a real size once laid out, so the map is (re)built on size changes
            // rather than in the constructor; SizeChanged also fires for the initial measure.
            mapCanvas.SizeChanged += HandleCanvasSizeChanged;
            mapCanvas.PointerPressed += HandlePointerPressed;
            mapCanvas.PointerMoved += HandlePointerMoved;
            mapCanvas.PointerReleased += HandlePointerReleased;
            KeyDown += HandleKeyDown;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void HandleCanvasSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            BuildMap();
        }

        // Rebuilds the monitor placeholders, labels, and the current-bounds ghost for the current canvas
        // size. The selection rectangle is kept (it is a fixed child) and any prior placeholders cleared.
        private void BuildMap()
        {
            if (mapCanvas.Bounds.Width <= 0 || mapCanvas.Bounds.Height <= 0)
            {
                return;
            }

            map = new QuickLayoutMap(monitors, mapCanvas.Bounds.Width, mapCanvas.Bounds.Height);

            for (int i = mapCanvas.Children.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(mapCanvas.Children[i], selectionRectangle))
                {
                    mapCanvas.Children.RemoveAt(i);
                }
            }

            for (int i = 0; i < monitors.Count; i++)
            {
                WindowRect rect = map.ToMapRect(monitors[i].PhysicalBounds);
                AddPlaceholder(rect, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            WindowRect ghost = map.ToMapRect(currentPhysicalBounds);
            AddGhost(ghost);

            // Keep the selection rectangle drawn on top of the placeholders.
            mapCanvas.Children.Remove(selectionRectangle);
            mapCanvas.Children.Add(selectionRectangle);
        }

        private void AddPlaceholder(WindowRect rect, string label)
        {
            Border border = new Border
            {
                Width = Math.Max(0, rect.Width),
                Height = Math.Max(0, rect.Height),
                Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = label,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    FontSize = 22,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Canvas.SetLeft(border, rect.Left);
            Canvas.SetTop(border, rect.Top);
            mapCanvas.Children.Add(border);
        }

        private void AddGhost(WindowRect rect)
        {
            Rectangle ghost = new Rectangle
            {
                Width = Math.Max(0, rect.Width),
                Height = Math.Max(0, rect.Height),
                Stroke = new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF)),
                StrokeThickness = 1,
                StrokeDashArray = new AvaloniaList<double> { 3, 2 },
                Fill = new SolidColorBrush(Color.FromArgb(0x33, 0x3D, 0x7E, 0xFF)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(ghost, rect.Left);
            Canvas.SetTop(ghost, rect.Top);
            mapCanvas.Children.Add(ghost);
        }

        private void HandlePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(mapCanvas);
            if (point.Properties.IsRightButtonPressed)
            {
                Close(null);
                return;
            }

            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            dragStart = point.Position;
            Canvas.SetLeft(selectionRectangle, point.Position.X);
            Canvas.SetTop(selectionRectangle, point.Position.Y);
            selectionRectangle.Width = 0;
            selectionRectangle.Height = 0;
            selectionRectangle.IsVisible = true;
            warningBox.IsVisible = false;
        }

        private void HandlePointerMoved(object? sender, PointerEventArgs e)
        {
            if (dragStart is not { } start || map is null)
            {
                return;
            }

            Point current = ClampToCanvas(e.GetPosition(mapCanvas));
            double left = Math.Min(start.X, current.X);
            double top = Math.Min(start.Y, current.Y);
            Canvas.SetLeft(selectionRectangle, left);
            Canvas.SetTop(selectionRectangle, top);
            selectionRectangle.Width = Math.Abs(current.X - start.X);
            selectionRectangle.Height = Math.Abs(current.Y - start.Y);

            // Warn once the drag has started but is still below the note's minimum, so the user knows a
            // release now will reposition at the current size rather than resize.
            bool started = selectionRectangle.Width > 0 || selectionRectangle.Height > 0;
            warningBox.IsVisible = started && QuickLayoutSelection.IsBelowMinimum(
                start.X, start.Y, current.X, current.Y, map, minWidthDip, minHeightDip);
        }

        private void HandlePointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (dragStart is not { } start || map is null)
            {
                return;
            }

            Point end = ClampToCanvas(e.GetPosition(mapCanvas));
            dragStart = null;
            selectionRectangle.IsVisible = false;
            warningBox.IsVisible = false;

            WindowRect result = QuickLayoutSelection.Resolve(
                start.X,
                start.Y,
                end.X,
                end.Y,
                map,
                currentWidthDip,
                currentHeightDip,
                minWidthDip,
                minHeightDip);

            AppLogger.Debug($"QuickLayout release: start={start} end={end} result={result}");

            Close(result);
        }

        // Keeps a gesture inside the drawable map area so a note cannot be dropped into the void around
        // the monitor placeholders, matching how the OS arrangement screen confines its drag.
        private Point ClampToCanvas(Point point)
        {
            if (map is null)
            {
                return point;
            }

            WindowRect content = map.ContentRect;
            double x = Math.Clamp(point.X, content.Left, content.Left + content.Width);
            double y = Math.Clamp(point.Y, content.Top, content.Top + content.Height);
            return new Point(x, y);
        }

        private void HandleKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        }
    }
}
