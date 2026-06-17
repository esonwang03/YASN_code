using Avalonia.Controls;
using YASN.Core;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Applies note window stacking levels.
    /// </summary>
    public interface IWindowLevelController
    {
        /// <summary>
        /// Gets whether bottom-most placement is supported.
        /// </summary>
        bool SupportsBottomMost { get; }

        /// <summary>
        /// Applies the requested level to a window.
        /// </summary>
        /// <param name="window">The target window.</param>
        /// <param name="level">The requested level.</param>
        void Apply(Window window, WindowLevel level);
    }
}
