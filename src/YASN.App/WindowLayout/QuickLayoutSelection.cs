namespace YASN.WindowLayout
{
    /// <summary>
    /// Resolves the target window bounds chosen on the quick-layout monitor map. The map draws every
    /// monitor as a proportional placeholder on a small canvas; the user clicks a point to reposition
    /// the note there, or drags a rectangle to move and resize it. Pointer coordinates arrive in canvas
    /// (device-independent) space relative to the map's top-left; this converts a press/release gesture
    /// into a window rectangle whose left/top are physical pixels (Avalonia window position) and whose
    /// width/height are device-independent pixels (Avalonia window size), sized for the monitor the
    /// result lands on. A drag whose device-independent size falls below the note's minimum is treated
    /// as a reposition (keep the current size), so an accidental small drag never shrinks the note.
    /// </summary>
    public static class QuickLayoutSelection
    {
        /// <summary>
        /// Resolves the target bounds for a click or too-small drag (reposition only) or a drag large
        /// enough to resize (move and resize).
        /// </summary>
        /// <param name="startMapX">The pointer-press X, in canvas space.</param>
        /// <param name="startMapY">The pointer-press Y, in canvas space.</param>
        /// <param name="endMapX">The pointer-release X, in canvas space.</param>
        /// <param name="endMapY">The pointer-release Y, in canvas space.</param>
        /// <param name="map">The monitor map that projects canvas space back to physical pixels.</param>
        /// <param name="currentWidthDip">The window's current width, in DIP, kept for a reposition.</param>
        /// <param name="currentHeightDip">The window's current height, in DIP, kept for a reposition.</param>
        /// <param name="minWidthDip">The note's minimum width, in DIP; smaller drags reposition only.</param>
        /// <param name="minHeightDip">The note's minimum height, in DIP; smaller drags reposition only.</param>
        /// <returns>The target bounds: physical-pixel left/top with DIP width/height.</returns>
        public static WindowRect Resolve(
            double startMapX,
            double startMapY,
            double endMapX,
            double endMapY,
            QuickLayoutMap map,
            double currentWidthDip,
            double currentHeightDip,
            double minWidthDip,
            double minHeightDip)
        {
            DragMeasure measure = Measure(startMapX, startMapY, endMapX, endMapY, map);

            if (measure.WidthDip < minWidthDip || measure.HeightDip < minHeightDip)
            {
                // Too small to be a meaningful resize: reposition the top-left at the gesture origin and
                // keep the current size, so a click or an accidental tiny drag just moves the note.
                return new WindowRect(measure.Left, measure.Top, currentWidthDip, currentHeightDip);
            }

            return new WindowRect(measure.Left, measure.Top, measure.WidthDip, measure.HeightDip);
        }

        /// <summary>
        /// Reports whether a drag is below the note's minimum size, so the overlay can warn that
        /// releasing will reposition at the current size rather than resize. Matches the decision
        /// <see cref="Resolve"/> makes on release.
        /// </summary>
        /// <param name="startMapX">The pointer-press X, in canvas space.</param>
        /// <param name="startMapY">The pointer-press Y, in canvas space.</param>
        /// <param name="endMapX">The pointer-current X, in canvas space.</param>
        /// <param name="endMapY">The pointer-current Y, in canvas space.</param>
        /// <param name="map">The monitor map that projects canvas space back to physical pixels.</param>
        /// <param name="minWidthDip">The note's minimum width, in DIP.</param>
        /// <param name="minHeightDip">The note's minimum height, in DIP.</param>
        /// <returns>True when the drag's device-independent size is below either minimum.</returns>
        public static bool IsBelowMinimum(
            double startMapX,
            double startMapY,
            double endMapX,
            double endMapY,
            QuickLayoutMap map,
            double minWidthDip,
            double minHeightDip)
        {
            DragMeasure measure = Measure(startMapX, startMapY, endMapX, endMapY, map);
            return measure.WidthDip < minWidthDip || measure.HeightDip < minHeightDip;
        }

        // Normalizes a press/current gesture into its physical top-left and the device-independent size
        // it would produce on the monitor that top-left lands on. Shared by Resolve and IsBelowMinimum so
        // the live warning and the released result always agree.
        private static DragMeasure Measure(
            double startMapX,
            double startMapY,
            double endMapX,
            double endMapY,
            QuickLayoutMap map)
        {
            double originMapX = Math.Min(startMapX, endMapX);
            double originMapY = Math.Min(startMapY, endMapY);
            double dragWidthMap = Math.Abs(endMapX - startMapX);
            double dragHeightMap = Math.Abs(endMapY - startMapY);

            WindowRect physical = map.FromMapRect(new WindowRect(originMapX, originMapY, dragWidthMap, dragHeightMap));
            double scaling = map.ScalingAt(physical.Left, physical.Top);

            return new DragMeasure(physical.Left, physical.Top, physical.Width / scaling, physical.Height / scaling);
        }

        private readonly record struct DragMeasure(double Left, double Top, double WidthDip, double HeightDip);
    }
}
