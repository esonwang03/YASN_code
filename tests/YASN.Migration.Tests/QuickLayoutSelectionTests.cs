using YASN.WindowLayout;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the quick-layout monitor map projects monitors into canvas space and back, and that a
    /// click or drag on the map resolves to the right window bounds — including across monitors that
    /// differ in DPI, the case that motivated the map-based design.
    /// </summary>
    public sealed class QuickLayoutSelectionTests
    {
        // A 1920x1080 primary monitor at 100% with a 1920x1080 secondary at 200% to its right, in
        // physical pixels. The secondary's physical width is the same but it represents fewer DIP.
        private static QuickLayoutMap TwoMonitorMap(double canvasWidth = 600, double canvasHeight = 400)
        {
            QuickLayoutMonitor primary = new QuickLayoutMonitor(new WindowRect(0, 0, 1920, 1080), 1.0);
            QuickLayoutMonitor secondary = new QuickLayoutMonitor(new WindowRect(1920, 0, 1920, 1080), 2.0);
            return new QuickLayoutMap(new[] { primary, secondary }, canvasWidth, canvasHeight);
        }

        /// <summary>
        /// A map fits the whole virtual desktop into the canvas with one aspect-preserving scale, so the
        /// real relative monitor sizes and gaps survive the projection.
        /// </summary>
        [Fact]
        public void MapFitsUnionPreservingAspect()
        {
            // Union is 3840x1080. A 600x400 canvas fits to scale 600/3840 = 0.15625 (width-bound), so the
            // mapped content is 600 wide and 168.75 tall, centred vertically.
            QuickLayoutMap map = TwoMonitorMap();
            WindowRect content = map.ContentRect;

            Assert.Equal(0, content.Left, 3);
            Assert.Equal(600, content.Width, 3);
            Assert.Equal(168.75, content.Height, 3);
            Assert.Equal((400 - 168.75) / 2.0, content.Top, 3);
        }

        /// <summary>
        /// Projecting a monitor to map space and back is a round trip, so the map introduces no drift.
        /// </summary>
        [Fact]
        public void ToMapRectAndBackRoundTrips()
        {
            QuickLayoutMap map = TwoMonitorMap();
            WindowRect physical = new WindowRect(1920, 0, 1920, 1080);

            WindowRect roundTripped = map.FromMapRect(map.ToMapRect(physical));

            Assert.Equal(physical.Left, roundTripped.Left, 3);
            Assert.Equal(physical.Top, roundTripped.Top, 3);
            Assert.Equal(physical.Width, roundTripped.Width, 3);
            Assert.Equal(physical.Height, roundTripped.Height, 3);
        }

        /// <summary>
        /// A short click keeps the current size and positions the top-left at the gesture origin.
        /// </summary>
        [Fact]
        public void ShortClickRepositionsKeepingSize()
        {
            QuickLayoutMap map = TwoMonitorMap();
            // A near-zero drag near the primary monitor's centre; scale is 0.15625, vertical offset
            // 115.625, so the origin map (150, 200) -> physical (960, ~540), size unchanged.
            WindowRect result = QuickLayoutSelection.Resolve(
                startMapX: 150,
                startMapY: 200,
                endMapX: 152,
                endMapY: 201,
                map: map,
                currentWidthDip: 900,
                currentHeightDip: 560,
                minWidthDip: 200,
                minHeightDip: 120);

            Assert.Equal(150 / 0.15625, result.Left, 3);
            Assert.Equal((200 - 115.625) / 0.15625, result.Top, 3);
            Assert.Equal(900, result.Width, 3);
            Assert.Equal(560, result.Height, 3);
        }

        /// <summary>
        /// A drag on the 100% monitor resizes to the drawn rectangle, with width/height in DIP equal to
        /// the physical span (scaling 1.0 there).
        /// </summary>
        [Fact]
        public void DragOnPrimaryResizesToSelection()
        {
            QuickLayoutMap map = TwoMonitorMap();
            // Map x in [0,300) is the primary monitor (physical [0,1920)). A 100x50 map drag there spans
            // 100/0.15625 = 640 physical px wide, 320 tall, which is 640x320 DIP at 100%.
            WindowRect result = QuickLayoutSelection.Resolve(
                startMapX: 50,
                startMapY: 150,
                endMapX: 150,
                endMapY: 200,
                map: map,
                currentWidthDip: 900,
                currentHeightDip: 560,
                minWidthDip: 200,
                minHeightDip: 120);

            Assert.Equal(50 / 0.15625, result.Left, 3);
            Assert.Equal(640, result.Width, 3);
            Assert.Equal(320, result.Height, 3);
        }

        /// <summary>
        /// A drag landing on the 200% monitor is sized for that monitor: the same physical span yields
        /// half the DIP size, so a note dragged onto a high-DPI display is not oversized.
        /// </summary>
        [Fact]
        public void DragOnHighDpiMonitorSizesForThatMonitor()
        {
            QuickLayoutMap map = TwoMonitorMap();
            // Map x in [300,600) is the secondary monitor (physical [1920,3840), scaling 2.0). A 100x50
            // map drag spans 640x320 physical px = 320x160 DIP at 200%.
            WindowRect result = QuickLayoutSelection.Resolve(
                startMapX: 350,
                startMapY: 150,
                endMapX: 450,
                endMapY: 200,
                map: map,
                currentWidthDip: 900,
                currentHeightDip: 560,
                minWidthDip: 200,
                minHeightDip: 120);

            Assert.True(result.Left >= 1920, $"expected left on secondary monitor, was {result.Left}");
            Assert.Equal(320, result.Width, 3);
            Assert.Equal(160, result.Height, 3);
        }

        /// <summary>
        /// Reversed drags normalize so the rectangle origin is the top-left corner.
        /// </summary>
        [Fact]
        public void ReversedDragNormalizesOrigin()
        {
            QuickLayoutMap map = TwoMonitorMap();
            WindowRect forward = QuickLayoutSelection.Resolve(
                50, 150, 150, 200, map, 900, 560, 200, 120);
            WindowRect reversed = QuickLayoutSelection.Resolve(
                150, 200, 50, 150, map, 900, 560, 200, 120);

            Assert.Equal(forward.Left, reversed.Left, 3);
            Assert.Equal(forward.Top, reversed.Top, 3);
            Assert.Equal(forward.Width, reversed.Width, 3);
            Assert.Equal(forward.Height, reversed.Height, 3);
        }

        /// <summary>
        /// A drag below the note's minimum size repositions at the current size instead of shrinking the
        /// note, and the live below-minimum check agrees with that decision.
        /// </summary>
        [Fact]
        public void TooSmallDragRepositionsKeepingSize()
        {
            QuickLayoutMap map = TwoMonitorMap();
            // A 10x6 map drag on the primary spans 64x38.4 physical px = 64x38.4 DIP at 100%, below a
            // 200x120 minimum, so the result keeps the current 900x560 size at the drag origin.
            WindowRect result = QuickLayoutSelection.Resolve(
                startMapX: 50,
                startMapY: 150,
                endMapX: 60,
                endMapY: 156,
                map: map,
                currentWidthDip: 900,
                currentHeightDip: 560,
                minWidthDip: 200,
                minHeightDip: 120);

            Assert.Equal(50 / 0.15625, result.Left, 3);
            Assert.Equal(900, result.Width, 3);
            Assert.Equal(560, result.Height, 3);

            Assert.True(QuickLayoutSelection.IsBelowMinimum(50, 150, 60, 156, map, 200, 120));
            Assert.False(QuickLayoutSelection.IsBelowMinimum(50, 150, 150, 200, map, 200, 120));
        }
    }
}
