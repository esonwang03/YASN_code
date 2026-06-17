using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using YASN.Core;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Applies window levels on Windows, including true bottom-most placement via SetWindowPos.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsWindowLevelController : IWindowLevelController
    {
        private static readonly nint HwndBottom = new nint(1);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;

        /// <summary>
        /// Gets whether bottom-most placement is supported by this controller.
        /// </summary>
        public bool SupportsBottomMost => true;

        /// <summary>
        /// Applies the requested level using Topmost for normal/topmost and SetWindowPos for bottom-most.
        /// </summary>
        /// <param name="window">The target window.</param>
        /// <param name="level">The requested level.</param>
        public void Apply(Window window, WindowLevel level)
        {
            if (level == WindowLevel.BottomMost)
            {
                window.Topmost = false;
                nint handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
                if (handle != nint.Zero)
                {
                    SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
                }

                return;
            }

            window.Topmost = level == WindowLevel.TopMost;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    }
}
