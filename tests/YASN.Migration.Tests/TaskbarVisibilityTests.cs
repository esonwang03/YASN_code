using YASN.Core;
using YASN.PlatformServices;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the global taskbar-visibility mode parsing and the show/hide decision per level.
    /// </summary>
    public sealed class TaskbarVisibilityTests
    {
        /// <summary>
        /// AlwaysShow shows the window at every level.
        /// </summary>
        [Theory]
        [InlineData(WindowLevel.Normal)]
        [InlineData(WindowLevel.TopMost)]
        [InlineData(WindowLevel.BottomMost)]
        public void AlwaysShowShowsEveryLevel(WindowLevel level)
        {
            Assert.True(TaskbarVisibility.ShouldShowInTaskbar(level, TaskbarVisibilityMode.AlwaysShow));
        }

        /// <summary>
        /// AlwaysHide hides the window at every level.
        /// </summary>
        [Theory]
        [InlineData(WindowLevel.Normal)]
        [InlineData(WindowLevel.TopMost)]
        [InlineData(WindowLevel.BottomMost)]
        public void AlwaysHideHidesEveryLevel(WindowLevel level)
        {
            Assert.False(TaskbarVisibility.ShouldShowInTaskbar(level, TaskbarVisibilityMode.AlwaysHide));
        }

        /// <summary>
        /// HideTopMostOnly hides only topmost windows.
        /// </summary>
        [Theory]
        [InlineData(WindowLevel.Normal, true)]
        [InlineData(WindowLevel.BottomMost, true)]
        [InlineData(WindowLevel.TopMost, false)]
        public void HideTopMostOnlyHidesOnlyTopMost(WindowLevel level, bool expected)
        {
            Assert.Equal(expected, TaskbarVisibility.ShouldShowInTaskbar(level, TaskbarVisibilityMode.HideTopMostOnly));
        }

        /// <summary>
        /// Known persisted values parse to their modes.
        /// </summary>
        [Theory]
        [InlineData("ALWAYSSHOW", TaskbarVisibilityMode.AlwaysShow)]
        [InlineData("alwayshide", TaskbarVisibilityMode.AlwaysHide)]
        [InlineData("  HideTopMostOnly  ", TaskbarVisibilityMode.HideTopMostOnly)]
        public void ParseModeReadsKnownValues(string raw, TaskbarVisibilityMode expected)
        {
            Assert.Equal(expected, TaskbarVisibility.ParseMode(raw));
        }

        /// <summary>
        /// Unknown or empty values fall back to the default mode.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("nonsense")]
        public void ParseModeFallsBackToDefault(string? raw)
        {
            Assert.Equal(TaskbarVisibility.Default, TaskbarVisibility.ParseMode(raw));
        }
    }
}
