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
            double scaling = GetScaling(window);
            WindowRect result = QuickWindowLayout.Move(ToPhysicalRect(window, scaling), GetWorkingArea(window), target);
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
            double scaling = GetScaling(window);
            WindowRect result = QuickWindowLayout.Resize(
                ToPhysicalRect(window, scaling),
                GetWorkingArea(window),
                width * scaling,
                height * scaling,
                window.MinWidth * scaling,
                window.MinHeight * scaling);
            Apply(window, result, scaling);
        }

        /// <summary>
        /// Applies explicit window bounds chosen on the quick-layout overlay.
        /// </summary>
        /// <param name="window">The target window.</param>
        /// <param name="bounds">Bounds with physical-pixel left/top and DIP width/height.</param>
        public void ApplyBounds(Window window, WindowRect bounds)
        {
            // Left/Top arrive in physical pixels (window position space) and Width/Height in DIP
            // (window size space), so each component is applied to its matching coordinate space.
            window.Position = new PixelPoint((int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top));
            window.Width = Math.Max(window.MinWidth, bounds.Width);
            window.Height = Math.Max(window.MinHeight, bounds.Height);
        }

        // Avalonia exposes window position in physical pixels but width/height in logical units,
        // while screen working areas are physical pixels. Quick-layout math must run in a single
        // space, so everything is normalized to physical pixels here using the window scaling.
        private static WindowRect ToPhysicalRect(Window window, double scaling)
        {
            return new WindowRect(window.Position.X, window.Position.Y, window.Width * scaling, window.Height * scaling);
        }

        private static double GetScaling(Window window)
        {
            Screen? screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            double scaling = screen?.Scaling ?? window.RenderScaling;

            return scaling <= 0 ? 1.0 : scaling;
        }

        private static WindowRect GetWorkingArea(Window window)
        {
            Screen? screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            PixelRect area = screen?.WorkingArea ?? new PixelRect(window.Position, new PixelSize((int)window.Width, (int)window.Height));

            return new WindowRect(area.X, area.Y, area.Width, area.Height);
        }

        private static void Apply(Window window, WindowRect rect, double scaling)
        {
            window.Position = new PixelPoint((int)Math.Round(rect.Left), (int)Math.Round(rect.Top));
            window.Width = rect.Width / scaling;
            window.Height = rect.Height / scaling;
        }
    }
}
