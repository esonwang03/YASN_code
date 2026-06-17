using YASN.WindowLayout;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies cross-platform quick move and resize geometry.
    /// </summary>
    public sealed class QuickWindowLayoutTests
    {
        /// <summary>
        /// Moves a window to screen quadrants without relying on virtual desktop APIs.
        /// </summary>
        [Fact]
        public void MoveToQuadrantUsesScreenWorkingArea()
        {
            WindowRect screen = new WindowRect(0, 0, 1200, 800);
            WindowRect current = new WindowRect(100, 120, 300, 200);

            WindowRect result = QuickWindowLayout.Move(current, screen, QuickMoveTarget.RightHalf);

            Assert.Equal(new WindowRect(600, 0, 600, 800), result);
        }

        /// <summary>
        /// Resizes around the current center while respecting minimum size and screen bounds.
        /// </summary>
        [Fact]
        public void ResizeAroundCenterClampsToWorkingArea()
        {
            WindowRect screen = new WindowRect(0, 0, 1000, 700);
            WindowRect current = new WindowRect(300, 200, 500, 300);

            WindowRect result = QuickWindowLayout.Resize(current, screen, 700, 500, minWidth: 320, minHeight: 240);

            Assert.Equal(new WindowRect(200, 100, 700, 500), result);
        }

        /// <summary>
        /// Keeps very large resize requests inside the working area.
        /// </summary>
        [Fact]
        public void ResizeClampsOversizedWindowToWorkingArea()
        {
            WindowRect screen = new WindowRect(10, 20, 900, 600);
            WindowRect current = new WindowRect(200, 200, 300, 300);

            WindowRect result = QuickWindowLayout.Resize(current, screen, 1200, 900, minWidth: 320, minHeight: 240);

            Assert.Equal(screen, result);
        }
    }
}
