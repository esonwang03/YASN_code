using Avalonia;
using YASN.WindowLayout;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the quick-layout overlay translates click and drag gestures into window bounds.
    /// </summary>
    public sealed class QuickLayoutSelectionTests
    {
        /// <summary>
        /// A short click keeps the current size and positions the top-left at the click point.
        /// </summary>
        [Fact]
        public void ShortClickRepositionsKeepingSize()
        {
            WindowRect result = QuickLayoutSelection.Resolve(
                startDip: new Point(300, 200),
                endDip: new Point(304, 203),
                overlayScaling: 1.0,
                virtualOriginPhysicalX: 0,
                virtualOriginPhysicalY: 0,
                currentWidthDip: 900,
                currentHeightDip: 560,
                minSelectionDip: 12);

            Assert.Equal(new WindowRect(304, 203, 900, 560), result);
        }

        /// <summary>
        /// A drag produces bounds matching the drawn rectangle, offset by the virtual origin.
        /// </summary>
        [Fact]
        public void DragResizesToSelectionRectangle()
        {
            WindowRect result = QuickLayoutSelection.Resolve(
                startDip: new Point(100, 100),
                endDip: new Point(500, 400),
                overlayScaling: 1.0,
                virtualOriginPhysicalX: -1920,
                virtualOriginPhysicalY: 0,
                currentWidthDip: 900,
                currentHeightDip: 560,
                minSelectionDip: 12);

            Assert.Equal(new WindowRect(-1820, 100, 400, 300), result);
        }

        /// <summary>
        /// Reversed drags normalize so the rectangle origin is the top-left corner.
        /// </summary>
        [Fact]
        public void ReversedDragNormalizesOrigin()
        {
            WindowRect result = QuickLayoutSelection.Resolve(
                startDip: new Point(500, 400),
                endDip: new Point(100, 100),
                overlayScaling: 1.0,
                virtualOriginPhysicalX: 0,
                virtualOriginPhysicalY: 0,
                currentWidthDip: 900,
                currentHeightDip: 560,
                minSelectionDip: 12);

            Assert.Equal(new WindowRect(100, 100, 400, 300), result);
        }

        /// <summary>
        /// Scaling converts overlay DIP coordinates into physical-pixel left/top positions.
        /// </summary>
        [Fact]
        public void ScalingConvertsClickToPhysicalPosition()
        {
            WindowRect result = QuickLayoutSelection.Resolve(
                startDip: new Point(200, 150),
                endDip: new Point(202, 151),
                overlayScaling: 1.5,
                virtualOriginPhysicalX: 100,
                virtualOriginPhysicalY: 50,
                currentWidthDip: 800,
                currentHeightDip: 500,
                minSelectionDip: 12);

            // left = 100 + 202*1.5 = 403, top = 50 + 151*1.5 = 276.5
            Assert.Equal(403, result.Left, 3);
            Assert.Equal(276.5, result.Top, 3);
            Assert.Equal(800, result.Width, 3);
            Assert.Equal(500, result.Height, 3);
        }
    }
}
