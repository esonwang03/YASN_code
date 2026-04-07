using System.Runtime.InteropServices;
using WindowsDesktop;
using YASN.Infrastructure.Logging;

namespace YASN.App.WindowLayout
{
    /// <summary>
    /// Represents a virtual desktop entry together with UI metadata used by the quick layout picker.
    /// </summary>
    internal sealed record VirtualDesktopEntry(VirtualDesktop? Desktop, Guid Id, string Name, bool IsCurrent, bool IsWindowDesktop);

    /// <summary>
    /// Queries and applies Windows virtual desktop state for floating notes.
    /// </summary>
    internal static class VirtualDesktopCatalog
    {
        private const int GroupNotInCorrectStateHResult = unchecked((int)0x8007139F);
        private const int RpcDisconnectedHResult = unchecked((int)0x80010108);
        private const int RpcServerUnavailableHResult = unchecked((int)0x800706BA);

        private static bool _virtualDesktopUnavailable;
        private static string? _virtualDesktopUnavailableReason;

        /// <summary>
        /// Gets the current list of virtual desktops, falling back to the current desktop when integration is unavailable.
        /// </summary>
        public static IReadOnlyList<VirtualDesktopEntry> GetDesktops(FloatingWindow? window = null)
        {
            if (_virtualDesktopUnavailable)
            {
                AppLogger.Debug($"Virtual desktop catalog disabled, fallback for {DescribeWindow(window)}: {_virtualDesktopUnavailableReason}");
                return CreateFallbackDesktopEntries();
            }

            VirtualDesktop? currentDesktop;
            Guid currentId;

            try
            {
                currentDesktop = VirtualDesktop.Current;
                currentId = currentDesktop?.Id ?? Guid.Empty;
            }
            catch (Exception ex)
            {
                HandleVirtualDesktopFailure(ex, $"Virtual desktop current-desktop query for {DescribeWindow(window)}");
                AppLogger.Warn($"Virtual desktop current-desktop query failed for {DescribeWindow(window)}: api=Current, error={DescribeException(ex)}");
                return CreateFallbackDesktopEntries();
            }

            VirtualDesktop? windowDesktop = null;
            Guid windowDesktopId = Guid.Empty;
            bool? windowIsCurrentDesktop = null;
            IntPtr hwnd = IntPtr.Zero;

            if (window != null)
            {
                hwnd = GetWindowHandle(window);
                if (hwnd != IntPtr.Zero)
                {
                    try
                    {
                        windowDesktop = VirtualDesktop.FromHwnd(hwnd);
                        windowDesktopId = windowDesktop?.Id ?? Guid.Empty;
                        windowIsCurrentDesktop = windowDesktopId != Guid.Empty && windowDesktopId == currentId;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn(
                            $"Virtual desktop window probe failed for {DescribeWindow(window)}: api=FromHwnd, hwnd={DescribeHwnd(hwnd)}, currentDesktop={DescribeDesktop(currentDesktop)}, error={DescribeException(ex)}");
                    }
                }

                AppLogger.Debug(
                    $"Virtual desktop window probe for {DescribeWindow(window)}: hwnd={DescribeHwnd(hwnd)}, windowDesktop={DescribeDesktop(windowDesktop)}, currentDesktop={DescribeDesktop(currentDesktop)}, isCurrent={windowIsCurrentDesktop}");
            }

            try
            {
                VirtualDesktopEntry[] desktops = VirtualDesktop.GetDesktops()
                    .Select((desktop, index) => new VirtualDesktopEntry(
                        desktop,
                        desktop.Id,
                        string.IsNullOrWhiteSpace(desktop.Name) ? $"Desktop {index + 1}" : desktop.Name,
                        desktop.Id == currentId,
                        desktop.Id == windowDesktopId))
                    .ToArray();

                AppLogger.Debug(
                    $"Virtual desktop catalog resolved {desktops.Length} desktop(s) for {DescribeWindow(window)}. Current={desktops.FirstOrDefault(static desktop => desktop.IsCurrent)?.Name ?? "none"} ({currentId}), windowDesktop={desktops.FirstOrDefault(static desktop => desktop.IsWindowDesktop)?.Name ?? "none"} ({windowDesktopId}), windowIsCurrent={windowIsCurrentDesktop?.ToString() ?? "n/a"}");
                return desktops;
            }
            catch (Exception ex)
            {
                HandleVirtualDesktopFailure(ex, $"Virtual desktop desktop-list query for {DescribeWindow(window)}");
                AppLogger.Warn(
                    $"Virtual desktop desktop-list query failed for {DescribeWindow(window)}: api=GetDesktops, hwnd={DescribeHwnd(hwnd)}, currentDesktop={DescribeDesktop(currentDesktop)}, error={DescribeException(ex)}");
                return CreateFallbackDesktopEntries();
            }
        }

        /// <summary>
        /// Tries to move a window to the specified virtual desktop.
        /// </summary>
        public static bool TryMoveWindowToDesktop(FloatingWindow target, VirtualDesktopEntry desktop)
        {
            if (target == null || desktop.Desktop == null || _virtualDesktopUnavailable)
            {
                return false;
            }

            IntPtr hwnd = GetWindowHandle(target);

            try
            {
                AppLogger.Debug($"Moving {DescribeWindow(target)} to virtual desktop {desktop.Name} ({desktop.Id})");
                target.MoveToDesktop(desktop.Desktop);
                AppLogger.Debug($"Moved {DescribeWindow(target)} to virtual desktop {desktop.Name} ({desktop.Id})");
                return true;
            }
            catch (Exception ex)
            {
                HandleVirtualDesktopFailure(ex, $"Virtual desktop move for {DescribeWindow(target)}");
                AppLogger.Warn(
                    $"Virtual desktop move failed for {DescribeWindow(target)}: api=MoveToDesktop, hwnd={DescribeHwnd(hwnd)}, targetDesktop={desktop.Name} ({desktop.Id}), error={DescribeException(ex)}");
                return false;
            }
        }

        /// <summary>
        /// Gets the current desktop entry, or the fallback desktop when enumeration is unavailable.
        /// </summary>
        public static VirtualDesktopEntry GetCurrentDesktop(FloatingWindow? window = null)
        {
            IReadOnlyList<VirtualDesktopEntry> desktops = GetDesktops(window);
            return desktops.FirstOrDefault(static desktop => desktop.IsCurrent)
                   ?? desktops.First();
        }

        /// <summary>
        /// Creates a readable description for logging.
        /// </summary>
        private static string DescribeWindow(FloatingWindow? window)
        {
            if (window?.NoteData == null)
            {
                return "window";
            }

            return $"noteId={window.NoteData.Id}, title='{window.NoteData.Title}'";
        }

        /// <summary>
        /// Creates a readable virtual desktop description for diagnostics.
        /// </summary>
        private static string DescribeDesktop(VirtualDesktop? desktop)
        {
            if (desktop == null)
            {
                return "none";
            }

            string name = string.IsNullOrWhiteSpace(desktop.Name) ? desktop.Id.ToString() : desktop.Name;
            return $"{name} ({desktop.Id})";
        }

        /// <summary>
        /// Gets the current HWND for a floating window when available.
        /// </summary>
        private static IntPtr GetWindowHandle(FloatingWindow? window)
        {
            if (window == null)
            {
                return IntPtr.Zero;
            }

            return new System.Windows.Interop.WindowInteropHelper(window).Handle;
        }

        /// <summary>
        /// Formats a window handle for diagnostics.
        /// </summary>
        private static string DescribeHwnd(IntPtr hwnd)
        {
            return hwnd == IntPtr.Zero ? "0x0" : $"0x{hwnd.ToInt64():X}";
        }

        /// <summary>
        /// Disables integration only for persistent failures and keeps transient COM state errors recoverable.
        /// </summary>
        private static void HandleVirtualDesktopFailure(Exception ex, string operation)
        {
            if (IsTransientVirtualDesktopFailure(ex))
            {
                AppLogger.Warn($"{operation} hit a transient virtual desktop failure and will be retried later: {FormatException(ex)}");
                return;
            }

            DisableVirtualDesktopIntegration(ex);
        }

        /// <summary>
        /// Returns whether a virtual desktop failure is a transient Windows shell state issue.
        /// </summary>
        private static bool IsTransientVirtualDesktopFailure(Exception ex)
        {
            return ex.HResult is GroupNotInCorrectStateHResult or RpcDisconnectedHResult or RpcServerUnavailableHResult
                   || ex is COMException { HResult: GroupNotInCorrectStateHResult or RpcDisconnectedHResult or RpcServerUnavailableHResult };
        }

        /// <summary>
        /// Formats an exception with HRESULT details for logs.
        /// </summary>
        private static string FormatException(Exception ex)
        {
            return $"{ex.Message} (0x{ex.HResult:X8})";
        }

        /// <summary>
        /// Formats an exception with type and HRESULT details for structured diagnostics.
        /// </summary>
        private static string DescribeException(Exception ex)
        {
            return $"{ex.GetType().Name}: {FormatException(ex)}";
        }

        /// <summary>
        /// Permanently disables integration for the current session after a non-recoverable failure.
        /// </summary>
        private static void DisableVirtualDesktopIntegration(Exception ex)
        {
            _virtualDesktopUnavailable = true;
            _virtualDesktopUnavailableReason = FormatException(ex);
            AppLogger.Warn($"Virtual desktop integration disabled after failure: {FormatException(ex)}");
        }

        /// <summary>
        /// Creates a single current-desktop placeholder when virtual desktop APIs are unavailable.
        /// </summary>
        private static IReadOnlyList<VirtualDesktopEntry> CreateFallbackDesktopEntries()
        {
            return
            [
                new VirtualDesktopEntry(null, Guid.Empty, "Current Desktop", true, true)
            ];
        }
    }
}
