namespace YASN.WindowLayout
{
    /// <summary>
    /// One monitor as the quick-layout map sees it: its bounds in physical pixels (the space
    /// <c>Screen.Bounds</c> lives in) and its scale factor (physical pixels per logical point).
    /// </summary>
    /// <param name="PhysicalBounds">The monitor bounds in physical pixels.</param>
    /// <param name="Scaling">The monitor scale factor; always greater than zero.</param>
    public sealed record QuickLayoutMonitor(WindowRect PhysicalBounds, double Scaling);

    /// <summary>
    /// Projects the physical virtual desktop onto a smaller dialog canvas and back, the way the
    /// operating system's display-arrangement screen shows monitors as proportional placeholders.
    /// The whole multi-monitor union is fit into the canvas with one aspect-preserving scale and
    /// centred, so the map keeps the real relative sizes and gaps between monitors. The transform is
    /// pure (no Avalonia controls) so it can be unit-tested for single- and multi-monitor desktops.
    /// </summary>
    public sealed class QuickLayoutMap
    {
        private readonly IReadOnlyList<QuickLayoutMonitor> monitors;
        private readonly WindowRect union;
        private readonly double scale;
        private readonly double offsetX;
        private readonly double offsetY;

        /// <summary>
        /// Builds a map fitting every monitor into a canvas of the given device-independent size.
        /// </summary>
        /// <param name="monitors">The monitors to lay out; must contain at least one entry.</param>
        /// <param name="canvasWidthDip">The map canvas width, in device-independent pixels.</param>
        /// <param name="canvasHeightDip">The map canvas height, in device-independent pixels.</param>
        public QuickLayoutMap(IReadOnlyList<QuickLayoutMonitor> monitors, double canvasWidthDip, double canvasHeightDip)
        {
            if (monitors is null || monitors.Count == 0)
            {
                throw new ArgumentException("At least one monitor is required.", nameof(monitors));
            }

            this.monitors = monitors;
            union = Union(monitors);

            double safeCanvasWidth = canvasWidthDip <= 0 ? 1.0 : canvasWidthDip;
            double safeCanvasHeight = canvasHeightDip <= 0 ? 1.0 : canvasHeightDip;
            double unionWidth = union.Width <= 0 ? 1.0 : union.Width;
            double unionHeight = union.Height <= 0 ? 1.0 : union.Height;

            scale = Math.Min(safeCanvasWidth / unionWidth, safeCanvasHeight / unionHeight);
            offsetX = (safeCanvasWidth - unionWidth * scale) / 2.0;
            offsetY = (safeCanvasHeight - unionHeight * scale) / 2.0;
        }

        /// <summary>The monitors this map projects, in their original order.</summary>
        public IReadOnlyList<QuickLayoutMonitor> Monitors => monitors;

        /// <summary>The canvas-space rectangle covering the whole virtual desktop (the drawable area).</summary>
        public WindowRect ContentRect => new WindowRect(offsetX, offsetY, union.Width * scale, union.Height * scale);

        /// <summary>
        /// Projects a physical-pixel rectangle into canvas (device-independent) space.
        /// </summary>
        /// <param name="physical">The rectangle in physical pixels.</param>
        /// <returns>The rectangle in canvas space.</returns>
        public WindowRect ToMapRect(WindowRect physical)
        {
            return new WindowRect(
                offsetX + (physical.Left - union.Left) * scale,
                offsetY + (physical.Top - union.Top) * scale,
                physical.Width * scale,
                physical.Height * scale);
        }

        /// <summary>
        /// Projects a canvas-space rectangle back into physical-pixel space.
        /// </summary>
        /// <param name="map">The rectangle in canvas space.</param>
        /// <returns>The rectangle in physical pixels.</returns>
        public WindowRect FromMapRect(WindowRect map)
        {
            double safeScale = scale <= 0 ? 1.0 : scale;
            return new WindowRect(
                union.Left + (map.Left - offsetX) / safeScale,
                union.Top + (map.Top - offsetY) / safeScale,
                map.Width / safeScale,
                map.Height / safeScale);
        }

        /// <summary>
        /// Resolves the scale factor of the monitor that contains a physical-pixel point, so a note
        /// dropped onto a different-DPI monitor is sized for that monitor. Falls back to the first
        /// monitor's scaling when the point lands in a gap between monitors.
        /// </summary>
        /// <param name="physicalX">The point's physical-pixel X coordinate.</param>
        /// <param name="physicalY">The point's physical-pixel Y coordinate.</param>
        /// <returns>The target monitor scale factor; always greater than zero.</returns>
        public double ScalingAt(double physicalX, double physicalY)
        {
            foreach (QuickLayoutMonitor monitor in monitors)
            {
                WindowRect b = monitor.PhysicalBounds;
                if (physicalX >= b.Left && physicalX < b.Left + b.Width &&
                    physicalY >= b.Top && physicalY < b.Top + b.Height)
                {
                    return monitor.Scaling <= 0 ? 1.0 : monitor.Scaling;
                }
            }

            return monitors[0].Scaling <= 0 ? 1.0 : monitors[0].Scaling;
        }

        private static WindowRect Union(IReadOnlyList<QuickLayoutMonitor> monitors)
        {
            double left = monitors[0].PhysicalBounds.Left;
            double top = monitors[0].PhysicalBounds.Top;
            double right = left + monitors[0].PhysicalBounds.Width;
            double bottom = top + monitors[0].PhysicalBounds.Height;

            for (int i = 1; i < monitors.Count; i++)
            {
                WindowRect b = monitors[i].PhysicalBounds;
                left = Math.Min(left, b.Left);
                top = Math.Min(top, b.Top);
                right = Math.Max(right, b.Left + b.Width);
                bottom = Math.Max(bottom, b.Top + b.Height);
            }

            return new WindowRect(left, top, right - left, bottom - top);
        }
    }
}
