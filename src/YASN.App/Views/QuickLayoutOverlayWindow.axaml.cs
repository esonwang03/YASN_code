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
        private readonly PixelRect virtualBounds;
        private readonly PixelPoint virtualOrigin;

        private Point? dragStart;

        /// <summary>
        /// Initializes the overlay for the XAML designer.
        /// </summary>
        public QuickLayoutOverlayWindow()
            : this(640, 400)
        {
        }

        /// <summary>
        /// Initializes the overlay for a target window of the given current size.
        /// </summary>
        /// <param name="currentWidthDip">The target window width, in DIP, used for a click reposition.</param>
        /// <param name="currentHeightDip">The target window height, in DIP, used for a click reposition.</param>
        public QuickLayoutOverlayWindow(double currentWidthDip, double currentHeightDip)
        {
            this.currentWidthDip = currentWidthDip;
            this.currentHeightDip = currentHeightDip;
            InitializeComponent();

            overlayCanvas = this.FindControl<Canvas>("OverlayCanvas")
                ?? throw new InvalidOperationException("OverlayCanvas was not found.");
            selectionRectangle = this.FindControl<Rectangle>("SelectionRectangle")
                ?? throw new InvalidOperationException("SelectionRectangle was not found.");

            PixelRect bounds = GetVirtualDesktopBounds();
            virtualBounds = bounds;
            virtualOrigin = bounds.Position;
            Position = bounds.Position;

            // Size is deferred to OnOpened: RenderScaling is only valid once the window is shown.
            // Computing it here reads an uninitialized scale, which on macOS Retina (where the
            // primary screen reports Scaling 1.0 despite a 2x render scale) sizes the overlay 2x too
            // large and breaks the click/drag -> position/size mapping.

            overlayCanvas.PointerPressed += HandlePointerPressed;
            overlayCanvas.PointerMoved += HandlePointerMoved;
            overlayCanvas.PointerReleased += HandlePointerReleased;
            KeyDown += HandleKeyDown;
        }

        /// <summary>
        /// Sizes the overlay to span the virtual desktop once the window is shown and
        /// <see cref="TopLevel.RenderScaling"/> reports the real scale factor.
        /// </summary>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            double scaling = RenderScalingSafe();
            Width = virtualBounds.Width / scaling;
            Height = virtualBounds.Height / scaling;
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

        private double RenderScalingSafe()
        {
            double scaling = RenderScaling;
            if (scaling > 0)
            {
                return scaling;
            }

            return Screens.Primary?.Scaling is { } primary and > 0 ? primary : 1.0;
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
                RenderScalingSafe(),
                virtualOrigin.X,
                virtualOrigin.Y,
                currentWidthDip,
                currentHeightDip,
                MinSelectionDip);

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
