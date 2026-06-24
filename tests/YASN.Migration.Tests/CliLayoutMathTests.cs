using YASN.WindowLayout;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the <c>note layout</c> coordinate math: a screen-relative physical-pixel rectangle
    /// becomes absolute physical position plus DIP size for the target monitor. This is the contract
    /// the CLI relies on to place a window correctly across multi-monitor and HiDPI setups.
    /// </summary>
    public sealed class CliLayoutMathTests
    {
        private static ScreenInfo Screen(double left, double top, double scaling)
        {
            return new ScreenInfo(
                0,
                new WindowRect(left, top, 1920, 1080),
                new WindowRect(left, top, 1920, 1040),
                scaling,
                true);
        }

        [Fact]
        public void PrimaryScreenAtOriginUnscaled()
        {
            WindowRect bounds = CliLayoutMath.Resolve(Screen(0, 0, 1.0), 100, 200, 700, 600);

            // Absolute position equals screen-relative position; size equals physical extent at 1.0.
            Assert.Equal(100, bounds.Left);
            Assert.Equal(200, bounds.Top);
            Assert.Equal(600, bounds.Width);
            Assert.Equal(400, bounds.Height);
        }

        [Fact]
        public void SecondScreenAddsScreenOrigin()
        {
            // A monitor to the right of primary at x=1920.
            WindowRect bounds = CliLayoutMath.Resolve(Screen(1920, 0, 1.0), 50, 60, 250, 260);

            Assert.Equal(1970, bounds.Left);
            Assert.Equal(60, bounds.Top);
            Assert.Equal(200, bounds.Width);
            Assert.Equal(200, bounds.Height);
        }

        [Fact]
        public void HiDpiScreenConvertsPhysicalToDip()
        {
            // At 2.0 scaling, a 600x400 physical rectangle is a 300x200 DIP window.
            WindowRect bounds = CliLayoutMath.Resolve(Screen(0, 0, 2.0), 0, 0, 600, 400);

            Assert.Equal(300, bounds.Width);
            Assert.Equal(200, bounds.Height);
        }

        [Fact]
        public void NonPositiveExtentThrows()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => CliLayoutMath.Resolve(Screen(0, 0, 1.0), 100, 100, 100, 200));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => CliLayoutMath.Resolve(Screen(0, 0, 1.0), 100, 100, 200, 50));
        }
    }
}
