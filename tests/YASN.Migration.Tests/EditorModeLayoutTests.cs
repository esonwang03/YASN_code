using YASN.WindowLayout;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the editor split-mode window expansion and restoration geometry.
    /// </summary>
    public sealed class EditorModeLayoutTests
    {
        /// <summary>
        /// Entering split mode doubles the width and keeps the right edge fixed.
        /// </summary>
        [Fact]
        public void ExpandLeftDoublesWidthKeepingRightEdge()
        {
            EditorModeLayout.ModeBounds result = EditorModeLayout.ExpandLeft(
                leftPhysical: 200,
                widthDip: 450,
                scaling: 1.0,
                minWidthDip: 620,
                maxWidthDip: 3000);

            Assert.Equal(900, result.WidthDip, 3);
            Assert.Equal(-250, result.LeftPhysical, 3);
            // Right edge fixed: left + width*scaling unchanged.
            Assert.Equal(200 + 450 * 1.0, result.LeftPhysical + result.WidthDip * 1.0, 3);
        }

        /// <summary>
        /// The doubled width is clamped to the maximum allowed width.
        /// </summary>
        [Fact]
        public void ExpandLeftClampsToMaxWidth()
        {
            EditorModeLayout.ModeBounds result = EditorModeLayout.ExpandLeft(
                leftPhysical: 100,
                widthDip: 900,
                scaling: 1.0,
                minWidthDip: 620,
                maxWidthDip: 1500);

            Assert.Equal(1500, result.WidthDip, 3);
            Assert.Equal(100 - (1500 - 900), result.LeftPhysical, 3);
        }

        /// <summary>
        /// The expanded width never falls below the minimum width.
        /// </summary>
        [Fact]
        public void ExpandLeftRespectsMinWidth()
        {
            EditorModeLayout.ModeBounds result = EditorModeLayout.ExpandLeft(
                leftPhysical: 500,
                widthDip: 200,
                scaling: 1.0,
                minWidthDip: 620,
                maxWidthDip: 3000);

            Assert.Equal(620, result.WidthDip, 3);
        }

        /// <summary>
        /// Scaling keeps the right edge fixed in physical pixels.
        /// </summary>
        [Fact]
        public void ExpandLeftKeepsRightEdgeUnderScaling()
        {
            const double scaling = 1.5;
            EditorModeLayout.ModeBounds result = EditorModeLayout.ExpandLeft(
                leftPhysical: 600,
                widthDip: 400,
                scaling: scaling,
                minWidthDip: 620,
                maxWidthDip: 3000);

            double rightBefore = 600 + 400 * scaling;
            double rightAfter = result.LeftPhysical + result.WidthDip * scaling;
            Assert.Equal(rightBefore, rightAfter, 3);
        }

        /// <summary>
        /// Restoring returns the saved left and width exactly.
        /// </summary>
        [Fact]
        public void RestoreReturnsSavedBounds()
        {
            EditorModeLayout.ModeBounds result = EditorModeLayout.Restore(320, 450);

            Assert.Equal(320, result.LeftPhysical, 3);
            Assert.Equal(450, result.WidthDip, 3);
        }
    }
}
