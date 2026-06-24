namespace YASN.WindowLayout
{
    /// <summary>
    /// Describes one monitor of the desktop as reported to the CLI: its index in the screen list,
    /// its physical-pixel bounds and working area, its scale factor, and whether it is primary.
    /// Bounds use absolute physical pixels in the virtual-desktop coordinate space.
    /// </summary>
    /// <param name="Index">The monitor's zero-based index in the screen enumeration.</param>
    /// <param name="PhysicalBounds">The full monitor bounds in absolute physical pixels.</param>
    /// <param name="WorkingArea">The usable area (excluding taskbars/docks) in absolute physical pixels.</param>
    /// <param name="Scaling">The monitor scale factor (physical pixels per device-independent pixel).</param>
    /// <param name="IsPrimary">Whether this monitor is the primary display.</param>
    public sealed record ScreenInfo(
        int Index,
        WindowRect PhysicalBounds,
        WindowRect WorkingArea,
        double Scaling,
        bool IsPrimary);
}
