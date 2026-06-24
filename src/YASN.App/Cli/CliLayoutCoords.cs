namespace YASN.Cli
{
    /// <summary>
    /// The four screen-relative physical-pixel coordinates for a <c>note layout</c> request: the
    /// left-top and right-bottom corners of the target rectangle. Passed from the protocol layer to
    /// the router, which converts them to absolute physical position and DIP size for the screen.
    /// </summary>
    /// <param name="LeftTopX">The left edge in physical pixels relative to the screen's left.</param>
    /// <param name="LeftTopY">The top edge in physical pixels relative to the screen's top.</param>
    /// <param name="RightBottomX">The right edge in physical pixels relative to the screen's left.</param>
    /// <param name="RightBottomY">The bottom edge in physical pixels relative to the screen's top.</param>
    public sealed record CliLayoutCoords(
        double LeftTopX,
        double LeftTopY,
        double RightBottomX,
        double RightBottomY);
}
