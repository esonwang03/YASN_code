using Avalonia.Controls;
using YASN.Core;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Applies cross-platform Avalonia window levels without faking unsupported bottom-most behavior.
    /// </summary>
    public sealed class AvaloniaWindowLevelController : IWindowLevelController
    {
        /// <summary>
        /// Gets whether bottom-most placement is supported by this controller.
        /// </summary>
        public bool SupportsBottomMost => false;

        /// <summary>
        /// Applies normal and topmost levels using Avalonia's built-in window property.
        /// </summary>
        /// <param name="window">The target window.</param>
        /// <param name="level">The requested level.</param>
        public void Apply(Window window, WindowLevel level)
        {
            window.Topmost = level == WindowLevel.TopMost;
        }
    }
}
