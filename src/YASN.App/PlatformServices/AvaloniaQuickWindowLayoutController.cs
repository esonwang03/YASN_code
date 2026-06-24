using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using YASN.WindowLayout;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Applies quick layout commands using Avalonia's cross-platform window and screen APIs.
    /// </summary>
    public sealed class AvaloniaQuickWindowLayoutController : IQuickWindowLayoutController
    {
        /// <summary>
        /// Moves a window to a screen region.
        /// </summary>
        /// <param name="window">The target window.</param>
        /// <param name="target">The target screen region.</param>
        public void Move(Window window, QuickMoveTarget target)
        {
            double scaling = WindowScreenScaling.Get(window);
            WindowRect current = ToPhysicalRect(window, scaling);
            WindowRect result = QuickWindowLayout.Move(current, GetWorkingArea(window), target);
            AppLogger.Debug($"QuickLayout move: target={target} scaling={scaling} current={current} result={result}");
            Apply(window, result, scaling);
        }

        /// <summary>
        /// Resizes a window around its current center.
        /// </summary>
        /// <param name="window">The target window.</param>
        /// <param name="width">The requested width in logical units.</param>
        /// <param name="height">The requested height in logical units.</param>
        public void Resize(Window window, double width, double height)
        {
            double scaling = WindowScreenScaling.Get(window);
            WindowRect current = ToPhysicalRect(window, scaling);
            WindowRect result = QuickWindowLayout.Resize(
                current,
                GetWorkingArea(window),
                width * scaling,
                height * scaling,
                window.MinWidth * scaling,
                window.MinHeight * scaling);
            AppLogger.Debug($"QuickLayout resize: requestedDip={width}x{height} scaling={scaling} current={current} result={result}");
            Apply(window, result, scaling);
        }

        /// <summary>
        /// Applies explicit window bounds chosen on the quick-layout monitor map.
        /// </summary>
        /// <param name="window">The target window.</param>
        /// <param name="bounds">Bounds with physical-pixel left/top and DIP width/height.</param>
        public void ApplyBounds(Window window, WindowRect bounds)
        {
            // Left/Top arrive in physical pixels (window position space) and Width/Height in DIP, already
            // sized for the monitor the bounds land on. The position seam (identity on Windows, divide-
            // by-scaling on macOS) must use that target monitor's scaling — not the window's current
            // screen — so a note moved across monitors of differing DPI lands at the right place.
            double scaling = ScalingAt(window, bounds.Left, bounds.Top);
            int left = (int)Math.Round(WindowPositionScaling.PhysicalToPosition(bounds.Left, scaling, WindowPositionScaling.PositionIsLogical));
            int top = (int)Math.Round(WindowPositionScaling.PhysicalToPosition(bounds.Top, scaling, WindowPositionScaling.PositionIsLogical));
            window.Position = new PixelPoint(left, top);
            window.Width = Math.Max(window.MinWidth, bounds.Width);
            window.Height = Math.Max(window.MinHeight, bounds.Height);
            AppLogger.Debug($"QuickLayout applyBounds: bounds={bounds} scaling={scaling} pos=({left},{top}) sizeDip={window.Width}x{window.Height}");
        }

        // Resolves the scale factor of the monitor that contains a physical-pixel point, falling back to
        // the window's current screen scaling when the point lands in a gap between monitors.
        private static double ScalingAt(Window window, double physicalX, double physicalY)
        {
            foreach (Screen screen in window.Screens.All)
            {
                PixelRect b = screen.Bounds;
                if (physicalX >= b.X && physicalX < b.X + b.Width &&
                    physicalY >= b.Y && physicalY < b.Y + b.Height)
                {
                    return screen.Scaling <= 0 ? 1.0 : screen.Scaling;
                }
            }

            return WindowScreenScaling.Get(window);
        }

        // Avalonia exposes window width/height in logical units while screen working areas are physical
        // pixels, and Window.Position is physical pixels on Windows but logical points on macOS. Quick-
        // layout math must run in a single space, so everything is normalized to physical pixels here:
        // the position is mapped through the platform seam and the DIP size is multiplied by scaling.
        private static WindowRect ToPhysicalRect(Window window, double scaling)
        {
            double left = WindowPositionScaling.PositionToPhysical(window.Position.X, scaling, WindowPositionScaling.PositionIsLogical);
            double top = WindowPositionScaling.PositionToPhysical(window.Position.Y, scaling, WindowPositionScaling.PositionIsLogical);
            return new WindowRect(left, top, window.Width * scaling, window.Height * scaling);
        }

        private static WindowRect GetWorkingArea(Window window)
        {
            Screen? screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            PixelRect area = screen?.WorkingArea ?? new PixelRect(window.Position, new PixelSize((int)window.Width, (int)window.Height));

            return new WindowRect(area.X, area.Y, area.Width, area.Height);
        }

        private static void Apply(Window window, WindowRect rect, double scaling)
        {
            int left = (int)Math.Round(WindowPositionScaling.PhysicalToPosition(rect.Left, scaling, WindowPositionScaling.PositionIsLogical));
            int top = (int)Math.Round(WindowPositionScaling.PhysicalToPosition(rect.Top, scaling, WindowPositionScaling.PositionIsLogical));
            window.Position = new PixelPoint(left, top);
            window.Width = rect.Width / scaling;
            window.Height = rect.Height / scaling;
            AppLogger.Debug($"QuickLayout apply: rect={rect} scaling={scaling} pos=({left},{top}) sizeDip={window.Width}x{window.Height}");
        }
    }
}
