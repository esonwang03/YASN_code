namespace YASN.WindowLayout
{
    /// <summary>
    /// Converts between screen physical-pixel space and Avalonia's <c>Window.Position</c> space.
    /// The two spaces differ by platform: on Windows <c>Window.Position</c> is physical pixels, so the
    /// conversion is the identity; on macOS Avalonia expresses <c>Window.Position</c> in logical points
    /// (it divides by the desktop scaling factor — see AvaloniaUI/Avalonia#11333), so physical pixels
    /// must be divided by the scale factor going out and multiplied coming back. Quick-layout geometry
    /// runs entirely in physical-pixel space (where <c>Screen.Bounds</c>/<c>WorkingArea</c> live); this
    /// seam is applied only where a value crosses into or out of <c>Window.Position</c>.
    /// </summary>
    public static class WindowPositionScaling
    {
        /// <summary>
        /// Whether the current OS expresses <c>Window.Position</c> in logical points (macOS) rather than
        /// physical pixels (Windows). Read once from the OS; pass it explicitly to the conversion
        /// methods so they stay pure and unit-testable for both platforms.
        /// </summary>
        public static bool PositionIsLogical { get; } = OperatingSystem.IsMacOS();

        /// <summary>
        /// Converts a physical-pixel coordinate into a <c>Window.Position</c> value for the current
        /// platform.
        /// </summary>
        /// <param name="physical">The coordinate in physical pixels.</param>
        /// <param name="scaling">The screen scale factor (physical pixels per logical point).</param>
        /// <param name="positionIsLogical">Whether position space is logical points (macOS).</param>
        /// <returns>The coordinate in <c>Window.Position</c> space.</returns>
        public static double PhysicalToPosition(double physical, double scaling, bool positionIsLogical)
        {
            if (!positionIsLogical)
            {
                return physical;
            }

            double safeScaling = scaling <= 0 ? 1.0 : scaling;
            return physical / safeScaling;
        }

        /// <summary>
        /// Converts a <c>Window.Position</c> value into a physical-pixel coordinate for the current
        /// platform.
        /// </summary>
        /// <param name="position">The coordinate in <c>Window.Position</c> space.</param>
        /// <param name="scaling">The screen scale factor (physical pixels per logical point).</param>
        /// <param name="positionIsLogical">Whether position space is logical points (macOS).</param>
        /// <returns>The coordinate in physical pixels.</returns>
        public static double PositionToPhysical(double position, double scaling, bool positionIsLogical)
        {
            if (!positionIsLogical)
            {
                return position;
            }

            double safeScaling = scaling <= 0 ? 1.0 : scaling;
            return position * safeScaling;
        }
    }
}
