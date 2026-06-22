using YASN.WindowLayout;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the platform seam mapping physical pixels to and from Avalonia's window-position space.
    /// The mapping is the source of correct window placement on macOS, where <c>Window.Position</c> is
    /// logical points, versus Windows, where it is physical pixels — so these tests pin the per-platform
    /// behaviour the overlay and quick-layout controller depend on.
    /// </summary>
    public sealed class WindowPositionScalingTests
    {
        /// <summary>
        /// On Windows, position space is physical pixels, so the conversion is the identity regardless
        /// of DPI. Both 1.0 and 2.0 scaling must pass through unchanged, proving DPI never alters the
        /// Windows path (the original, working behaviour must not regress).
        /// </summary>
        [Theory]
        [InlineData(1.0)]
        [InlineData(2.0)]
        public void WindowsPositionSpaceIsIdentity(double scaling)
        {
            Assert.Equal(400, WindowPositionScaling.PhysicalToPosition(400, scaling, positionIsLogical: false), 3);
            Assert.Equal(400, WindowPositionScaling.PositionToPhysical(400, scaling, positionIsLogical: false), 3);
        }

        /// <summary>
        /// On macOS at 2x, position space is logical points: physical pixels are halved going out and
        /// doubled coming back. This is exactly the conversion that keeps the overlay full-screen and
        /// the note placed where the user clicked on Retina.
        /// </summary>
        [Fact]
        public void MacPositionSpaceConvertsByScaling()
        {
            Assert.Equal(200, WindowPositionScaling.PhysicalToPosition(400, 2.0, positionIsLogical: true), 3);
            Assert.Equal(400, WindowPositionScaling.PositionToPhysical(200, 2.0, positionIsLogical: true), 3);
        }

        /// <summary>
        /// The two directions must be exact inverses at fractional scaling, or a reposition would drift
        /// every time it round-trips through the seam (read position -> physical -> write position).
        /// </summary>
        [Fact]
        public void MacRoundTripIsLossless()
        {
            const double original = 933;
            double physical = WindowPositionScaling.PositionToPhysical(original, 1.5, positionIsLogical: true);
            double back = WindowPositionScaling.PhysicalToPosition(physical, 1.5, positionIsLogical: true);

            Assert.Equal(original, back, 6);
        }

        /// <summary>
        /// A non-positive scaling must fall back to identity rather than divide by zero, so a window
        /// reporting an invalid scale still places sanely instead of throwing or producing infinity.
        /// </summary>
        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        public void NonPositiveScalingFallsBackToIdentity(double scaling)
        {
            Assert.Equal(300, WindowPositionScaling.PhysicalToPosition(300, scaling, positionIsLogical: true), 3);
            Assert.Equal(300, WindowPositionScaling.PositionToPhysical(300, scaling, positionIsLogical: true), 3);
        }
    }
}
