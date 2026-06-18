namespace YASN.WindowLayout
{
    /// <summary>
    /// Computes window bounds for entering and leaving the editor split (text and preview) mode.
    /// Avalonia exposes window position in physical pixels but width in device-independent pixels
    /// (DIP), so this keeps the two spaces explicit: the window's left is physical and its width is
    /// DIP. When entering split mode the window grows leftward with its right edge fixed.
    /// </summary>
    public static class EditorModeLayout
    {
        /// <summary>
        /// Holds a computed window left (physical pixels) and width (DIP).
        /// </summary>
        /// <param name="LeftPhysical">The window left coordinate, in physical pixels.</param>
        /// <param name="WidthDip">The window width, in device-independent pixels.</param>
        public readonly record struct ModeBounds(double LeftPhysical, double WidthDip);

        /// <summary>
        /// Doubles the window width, growing leftward so the right edge stays fixed, clamped to the
        /// allowed width range.
        /// </summary>
        /// <param name="leftPhysical">The current left, in physical pixels.</param>
        /// <param name="widthDip">The current width, in DIP.</param>
        /// <param name="scaling">The window scale factor (physical pixels per DIP).</param>
        /// <param name="minWidthDip">The minimum allowed width, in DIP.</param>
        /// <param name="maxWidthDip">The maximum allowed width, in DIP.</param>
        /// <returns>The expanded bounds.</returns>
        public static ModeBounds ExpandLeft(
            double leftPhysical,
            double widthDip,
            double scaling,
            double minWidthDip,
            double maxWidthDip)
        {
            double safeScaling = scaling <= 0 ? 1.0 : scaling;
            double targetWidth = widthDip * 2;
            if (maxWidthDip >= minWidthDip)
            {
                targetWidth = Math.Min(targetWidth, maxWidthDip);
            }

            targetWidth = Math.Max(targetWidth, minWidthDip);

            // Hold the right edge fixed: shift left by the physical-pixel width that was added.
            double addedWidthPhysical = (targetWidth - widthDip) * safeScaling;
            double newLeft = leftPhysical - addedWidthPhysical;
            return new ModeBounds(newLeft, targetWidth);
        }

        /// <summary>
        /// Restores the window to the bounds saved before entering split mode.
        /// </summary>
        /// <param name="savedLeftPhysical">The left saved before expansion, in physical pixels.</param>
        /// <param name="savedWidthDip">The width saved before expansion, in DIP.</param>
        /// <returns>The restored bounds.</returns>
        public static ModeBounds Restore(double savedLeftPhysical, double savedWidthDip)
        {
            return new ModeBounds(savedLeftPhysical, savedWidthDip);
        }

        /// <summary>
        /// Halves the window width, holding the right edge fixed, clamped to the minimum width. This is
        /// the inverse of <see cref="ExpandLeft"/> and is used when leaving split mode for a window that
        /// never recorded a pre-split width (e.g. a note that opened directly in split mode), so the
        /// frame still collapses instead of keeping its wide split bounds.
        /// </summary>
        /// <param name="leftPhysical">The current left, in physical pixels.</param>
        /// <param name="widthDip">The current width, in DIP.</param>
        /// <param name="scaling">The window scale factor (physical pixels per DIP).</param>
        /// <param name="minWidthDip">The minimum allowed width, in DIP.</param>
        /// <returns>The collapsed bounds.</returns>
        public static ModeBounds Collapse(
            double leftPhysical,
            double widthDip,
            double scaling,
            double minWidthDip)
        {
            double safeScaling = scaling <= 0 ? 1.0 : scaling;
            double targetWidth = Math.Max(widthDip / 2, minWidthDip);

            // Hold the right edge fixed: shift right by the physical-pixel width that was removed.
            double removedWidthPhysical = (widthDip - targetWidth) * safeScaling;
            double newLeft = leftPhysical + removedWidthPhysical;
            return new ModeBounds(newLeft, targetWidth);
        }
    }
}
