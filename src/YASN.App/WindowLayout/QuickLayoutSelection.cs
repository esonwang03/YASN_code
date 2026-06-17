using Avalonia;

namespace YASN.WindowLayout
{
    /// <summary>
    /// Resolves the target window bounds chosen on the quick-layout overlay. The overlay reports
    /// pointer coordinates in device-independent pixels (DIP) relative to its own top-left, which
    /// sits at the virtual-desktop origin expressed in physical pixels. This converts a press/release
    /// gesture into a window rectangle whose left/top are physical pixels (Avalonia window position)
    /// and whose width/height are DIP (Avalonia window size).
    /// </summary>
    public static class QuickLayoutSelection
    {
        /// <summary>
        /// Resolves the target bounds for a click (reposition only) or drag (move and resize).
        /// </summary>
        /// <param name="startDip">The pointer-press point, in overlay DIP.</param>
        /// <param name="endDip">The pointer-release point, in overlay DIP.</param>
        /// <param name="overlayScaling">The overlay's rendering scale factor.</param>
        /// <param name="virtualOriginPhysicalX">The virtual-desktop left origin, in physical pixels.</param>
        /// <param name="virtualOriginPhysicalY">The virtual-desktop top origin, in physical pixels.</param>
        /// <param name="currentWidthDip">The window's current width, in DIP, used for a click.</param>
        /// <param name="currentHeightDip">The window's current height, in DIP, used for a click.</param>
        /// <param name="minSelectionDip">The minimum drag size, in DIP, that counts as a resize.</param>
        /// <returns>The target bounds: physical-pixel left/top with DIP width/height.</returns>
        public static WindowRect Resolve(
            Point startDip,
            Point endDip,
            double overlayScaling,
            double virtualOriginPhysicalX,
            double virtualOriginPhysicalY,
            double currentWidthDip,
            double currentHeightDip,
            double minSelectionDip)
        {
            double scaling = overlayScaling <= 0 ? 1.0 : overlayScaling;
            double dragWidthDip = Math.Abs(endDip.X - startDip.X);
            double dragHeightDip = Math.Abs(endDip.Y - startDip.Y);
            bool isClick = dragWidthDip < minSelectionDip || dragHeightDip < minSelectionDip;

            if (isClick)
            {
                // Reposition only: top-left lands at the release point, size is unchanged.
                double leftPhysical = virtualOriginPhysicalX + endDip.X * scaling;
                double topPhysical = virtualOriginPhysicalY + endDip.Y * scaling;
                return new WindowRect(leftPhysical, topPhysical, currentWidthDip, currentHeightDip);
            }

            double originDipX = Math.Min(startDip.X, endDip.X);
            double originDipY = Math.Min(startDip.Y, endDip.Y);
            double left = virtualOriginPhysicalX + originDipX * scaling;
            double top = virtualOriginPhysicalY + originDipY * scaling;

            return new WindowRect(left, top, dragWidthDip, dragHeightDip);
        }
    }
}
