using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using YASN.WindowLayout;

namespace YASN.Views
{
    /// <summary>
    /// Fullscreen translucent overlay spanning every monitor. A short click repositions the target
    /// window to the click point; dragging a rectangle moves and resizes it. Escape or a right-click
    /// cancels. The dialog result is the chosen <see cref="WindowRect"/> (physical-pixel left/top,
    /// DIP width/height), or null when cancelled.
    /// </summary>
    public sealed partial class QuickLayoutOverlayWindow : Window
    {
        // Minimum drag size in DIP that counts as a resize rather than a reposition click.
        private const double MinSelectionDip = 12;

        private readonly Canvas overlayCanvas;
        private readonly Rectangle selectionRectangle;
        private readonly double currentWidthDip;
        private readonly double currentHeightDip;
        private readonly double scaling;
        private readonly PixelPoint virtualOrigin;

        private Point? dragStart;

        /// <summary>
        /// Initializes the overlay for the XAML designer.
        /// </summary>
        public QuickLayoutOverlayWindow()
            : this(640, 400, 1.0)
        {
        }

        /// <summary>
        /// Initializes the overlay for a target window of the given current size and screen scaling.
        /// </summary>
        /// <param name="currentWidthDip">The target window width, in DIP, used for a click reposition.</param>
        /// <param name="currentHeightDip">The target window height, in DIP, used for a click reposition.</param>
        /// <param name="scaling">
        /// The scale factor of the screen the target window is on (physical pixels per DIP). Passed in
        /// rather than read from the overlay's own <see cref="TopLevel.RenderScaling"/>, which is
        /// ambiguous for a window spanning monitors and is not yet valid before the window is shown.
        /// </param>
        public QuickLayoutOverlayWindow(double currentWidthDip, double currentHeightDip, double scaling)
        {
            this.currentWidthDip = currentWidthDip;
            this.currentHeightDip = currentHeightDip;
            this.scaling = scaling <= 0 ? 1.0 : scaling;
            InitializeComponent();

            overlayCanvas = this.FindControl<Canvas>("OverlayCanvas")
                ?? throw new InvalidOperationException("OverlayCanvas was not found.");
            selectionRectangle = this.FindControl<Rectangle>("SelectionRectangle")
                ?? throw new InvalidOperationException("SelectionRectangle was not found.");

            PixelRect bounds = GetVirtualDesktopBounds();
            virtualOrigin = bounds.Position;

            // The union origin is physical pixels; Window.Position is physical on Windows but logical
            // points on macOS, so map it through the platform seam. Size is DIP on every platform, so
            // divide the physical span by scaling. Both use the target window's screen scaling, which
            // is valid here (no need to defer to OnOpened).
            Position = new PixelPoint(
                (int)Math.Round(WindowPositionScaling.PhysicalToPosition(bounds.X, this.scaling, WindowPositionScaling.PositionIsLogical)),
                (int)Math.Round(WindowPositionScaling.PhysicalToPosition(bounds.Y, this.scaling, WindowPositionScaling.PositionIsLogical)));
            Width = bounds.Width / this.scaling;
            Height = bounds.Height / this.scaling;

            AppLogger.Debug($"QuickLayout overlay: positionLogical={WindowPositionScaling.PositionIsLogical} scaling={this.scaling} union={bounds} pos={Position} sizeDip={Width}x{Height}");

            overlayCanvas.PointerPressed += HandlePointerPressed;
            overlayCanvas.PointerMoved += HandlePointerMoved;
            overlayCanvas.PointerReleased += HandlePointerReleased;
            KeyDown += HandleKeyDown;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private PixelRect GetVirtualDesktopBounds()
        {
            IReadOnlyList<Screen> screens = Screens.All;
            if (screens.Count == 0)
            {
                return Screens.Primary?.Bounds ?? new PixelRect(0, 0, 1920, 1080);
            }

            PixelRect union = screens[0].Bounds;
            for (int i = 1; i < screens.Count; i++)
            {
                union = union.Union(screens[i].Bounds);
            }

            return union;
        }

        private void HandlePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(overlayCanvas);
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
        }

        private void HandlePointerMoved(object? sender, PointerEventArgs e)
        {
            if (dragStart is not { } start)
            {
                return;
            }

            Point current = e.GetPosition(overlayCanvas);
            double left = Math.Min(start.X, current.X);
            double top = Math.Min(start.Y, current.Y);
            Canvas.SetLeft(selectionRectangle, left);
            Canvas.SetTop(selectionRectangle, top);
            selectionRectangle.Width = Math.Abs(current.X - start.X);
            selectionRectangle.Height = Math.Abs(current.Y - start.Y);
        }

        private void HandlePointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (dragStart is not { } start)
            {
                return;
            }

            Point end = e.GetPosition(overlayCanvas);
            dragStart = null;
            selectionRectangle.IsVisible = false;

            WindowRect result = QuickLayoutSelection.Resolve(
                start,
                end,
                scaling,
                virtualOrigin.X,
                virtualOrigin.Y,
                currentWidthDip,
                currentHeightDip,
                MinSelectionDip);

            AppLogger.Debug($"QuickLayout release: start={start} end={end} scaling={scaling} origin=({virtualOrigin.X},{virtualOrigin.Y}) result={result}");

            Close(result);
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
