using WindowsDesktop;
using YASN.Logging;

namespace YASN.WindowLayout
{
    internal sealed record VirtualDesktopEntry(VirtualDesktop? Desktop, Guid Id, string Name, bool IsCurrent, bool IsWindowDesktop);

    internal static class VirtualDesktopCatalog
    {
        private static bool _virtualDesktopUnavailable;
        private static string? _virtualDesktopUnavailableReason;

        public static IReadOnlyList<VirtualDesktopEntry> GetDesktops(FloatingWindow? window = null)
        {
            if (_virtualDesktopUnavailable)
            {
                AppLogger.Debug($"Virtual desktop catalog disabled, fallback for {DescribeWindow(window)}: {_virtualDesktopUnavailableReason}");
                return CreateFallbackDesktopEntries();
            }

            try
            {
                VirtualDesktop? currentDesktop = VirtualDesktop.Current;
                Guid currentId = currentDesktop?.Id ?? Guid.Empty;
                VirtualDesktop? windowDesktop = null;
                Guid windowDesktopId = Guid.Empty;
                bool? windowIsCurrentDesktop = null;

                if (window != null)
                {
                    try
                    {
                        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                        if (hwnd != IntPtr.Zero)
                        {
                            windowDesktop = VirtualDesktop.FromHwnd(hwnd);
                            windowDesktopId = windowDesktop?.Id ?? Guid.Empty;
                            windowIsCurrentDesktop = windowDesktopId != Guid.Empty && windowDesktopId == currentId;
                        }

                        AppLogger.Debug(
                            $"Virtual desktop window probe for {DescribeWindow(window)}: hwnd=0x{hwnd.ToInt64():X}, windowDesktop={DescribeDesktop(windowDesktop)}, currentDesktop={DescribeDesktop(currentDesktop)}, isCurrent={windowIsCurrentDesktop}");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"Virtual desktop window probe failed for {DescribeWindow(window)}: {ex.Message}");
                    }
                }

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
                DisableVirtualDesktopIntegration(ex);
                AppLogger.Warn($"Virtual desktop catalog fallback for {DescribeWindow(window)}: {ex.Message}");
                return CreateFallbackDesktopEntries();
            }
        }

        public static bool TryMoveWindowToDesktop(FloatingWindow target, VirtualDesktopEntry desktop)
        {
            if (target == null || desktop.Desktop == null || _virtualDesktopUnavailable)
            {
                return false;
            }

            try
            {
                AppLogger.Debug($"Moving {DescribeWindow(target)} to virtual desktop {desktop.Name} ({desktop.Id})");
                target.MoveToDesktop(desktop.Desktop);
                AppLogger.Debug($"Moved {DescribeWindow(target)} to virtual desktop {desktop.Name} ({desktop.Id})");
                return true;
            }
            catch (Exception ex)
            {
                DisableVirtualDesktopIntegration(ex);
                AppLogger.Warn($"Failed to move {DescribeWindow(target)} to virtual desktop {desktop.Name} ({desktop.Id}): {ex.Message}");
                return false;
            }
        }

        public static VirtualDesktopEntry GetCurrentDesktop(FloatingWindow? window = null)
        {
            IReadOnlyList<VirtualDesktopEntry> desktops = GetDesktops(window);
            return desktops.FirstOrDefault(static desktop => desktop.IsCurrent)
                   ?? desktops.First();
        }

        private static string DescribeWindow(FloatingWindow? window)
        {
            if (window?.NoteData == null)
            {
                return "window";
            }

            return $"noteId={window.NoteData.Id}, title='{window.NoteData.Title}'";
        }

        private static string DescribeDesktop(VirtualDesktop? desktop)
        {
            if (desktop == null)
            {
                return "none";
            }

            string name = string.IsNullOrWhiteSpace(desktop.Name) ? desktop.Id.ToString() : desktop.Name;
            return $"{name} ({desktop.Id})";
        }

        private static void DisableVirtualDesktopIntegration(Exception ex)
        {
            _virtualDesktopUnavailable = true;
            _virtualDesktopUnavailableReason = ex.Message;
            AppLogger.Warn($"Virtual desktop integration disabled after failure: {ex.Message}");
        }

        private static IReadOnlyList<VirtualDesktopEntry> CreateFallbackDesktopEntries()
        {
            return
            [
                new VirtualDesktopEntry(null, Guid.Empty, "Current Desktop", true, true)
            ];
        }
    }
}
