using Avalonia.Controls;
using Avalonia.Platform;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Resolves the scale factor (physical pixels per logical point) of the screen a window currently
    /// occupies. Shared by the quick-layout controller and the overlay so both use one definition of
    /// "the scaling where the note lives" — the overlay can span several monitors, so its own
    /// <c>RenderScaling</c> is ambiguous; the target window's screen is the authoritative source.
    /// </summary>
    public static class WindowScreenScaling
    {
        /// <summary>
        /// Gets the scale factor of the screen the window is on, falling back to the window's render
        /// scaling and finally to 1.0 when no positive value is available.
        /// </summary>
        /// <param name="window">The window whose screen scaling is wanted.</param>
        /// <returns>The scale factor, always greater than zero.</returns>
        public static double Get(Window window)
        {
            Screen? screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            double scaling = screen?.Scaling ?? window.RenderScaling;

            return scaling <= 0 ? 1.0 : scaling;
        }
    }
}
