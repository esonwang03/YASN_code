namespace YASN.WindowLayout
{
    /// <summary>
    /// Pure coordinate math for the <c>note layout</c> CLI verb. Translates a rectangle expressed in
    /// physical pixels relative to a chosen screen's top-left into the form
    /// <see cref="PlatformServices.IQuickWindowLayoutController.ApplyBounds"/> expects: absolute
    /// physical-pixel left/top in the virtual-desktop space and device-independent-pixel width/height.
    /// Kept free of Avalonia types so it is unit-testable without a live window.
    /// </summary>
    public static class CliLayoutMath
    {
        /// <summary>
        /// Resolves a screen-relative physical rectangle into absolute-physical position and DIP size.
        /// </summary>
        /// <param name="screen">The target monitor whose origin and scaling anchor the result.</param>
        /// <param name="leftTopX">The left edge in physical pixels relative to the screen's left.</param>
        /// <param name="leftTopY">The top edge in physical pixels relative to the screen's top.</param>
        /// <param name="rightBottomX">The right edge in physical pixels relative to the screen's left.</param>
        /// <param name="rightBottomY">The bottom edge in physical pixels relative to the screen's top.</param>
        /// <returns>
        /// Bounds with absolute physical-pixel <see cref="WindowRect.Left"/>/<see cref="WindowRect.Top"/>
        /// and DIP <see cref="WindowRect.Width"/>/<see cref="WindowRect.Height"/>.
        /// </returns>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// Thrown when the rectangle has non-positive width or height.
        /// </exception>
        public static WindowRect Resolve(
            ScreenInfo screen,
            double leftTopX,
            double leftTopY,
            double rightBottomX,
            double rightBottomY)
        {
            double physicalWidth = rightBottomX - leftTopX;
            double physicalHeight = rightBottomY - leftTopY;
            if (physicalWidth <= 0 || physicalHeight <= 0)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(rightBottomX),
                    "Right-bottom must be below and to the right of left-top.");
            }

            double scaling = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
            double absoluteLeft = screen.PhysicalBounds.Left + leftTopX;
            double absoluteTop = screen.PhysicalBounds.Top + leftTopY;
            return new WindowRect(absoluteLeft, absoluteTop, physicalWidth / scaling, physicalHeight / scaling);
        }
    }
}
