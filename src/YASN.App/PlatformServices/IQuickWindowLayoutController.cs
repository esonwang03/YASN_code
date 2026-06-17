using Avalonia.Controls;
using YASN.WindowLayout;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Applies quick move and resize commands to Avalonia windows.
    /// </summary>
    public interface IQuickWindowLayoutController
    {
        /// <summary>
        /// Moves a window to a screen region.
        /// </summary>
        /// <param name="window">The target window.</param>
        /// <param name="target">The target screen region.</param>
        void Move(Window window, QuickMoveTarget target);

        /// <summary>
        /// Resizes a window around its current center.
        /// </summary>
        /// <param name="window">The target window.</param>
        /// <param name="width">The requested width.</param>
        /// <param name="height">The requested height.</param>
        void Resize(Window window, double width, double height);

        /// <summary>
        /// Applies explicit window bounds chosen on the quick-layout overlay.
        /// </summary>
        /// <param name="window">The target window.</param>
        /// <param name="bounds">Bounds with physical-pixel left/top and DIP width/height.</param>
        void ApplyBounds(Window window, WindowRect bounds);
    }
}
