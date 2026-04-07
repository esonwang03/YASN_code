using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using YASN.Infrastructure.Logging;
using Point = System.Windows.Point;
using Screen = System.Windows.Forms.Screen;

namespace YASN.App.WindowLayout
{
    internal static class FloatingWindowQuickActions
    {
        private const double FallbackMinWidth = 320;
        private const double FallbackMinHeight = 220;

        public static void ShowQuickMove(FloatingWindow target)
        {
            ShowPicker(target, QuickWindowLayoutMode.Move);
        }

        public static void ShowQuickMoveAndResize(FloatingWindow target)
        {
            ShowPicker(target, QuickWindowLayoutMode.MoveAndResize);
        }

        public static void ShowQuickResize(FloatingWindow target)
        {
            ShowPicker(target, QuickWindowLayoutMode.ResizeOnly);
        }

        public static void ApplyWindowPosition(FloatingWindow target, double left, double top, VirtualDesktopEntry? desktop = null)
        {
            if (target == null)
            {
                return;
            }

            AppLogger.Debug($"Quick layout apply position for {DescribeTarget(target)} -> left={left:F1}, top={top:F1}, desktop={DescribeDesktop(desktop)}");
            ApplyWindowBounds(target, new Rect(left, top, target.Width, target.Height), desktop);
        }

        public static void ApplyWindowSize(FloatingWindow target, double width, double height)
        {
            if (target == null)
            {
                return;
            }

            AppLogger.Debug($"Quick layout apply size for {DescribeTarget(target)} -> width={width:F1}, height={height:F1}");
            ApplyWindowBounds(target, new Rect(target.Left, target.Top, width, height));
        }

        public static void ApplyWindowBounds(FloatingWindow target, Rect bounds, VirtualDesktopEntry? desktop = null)
        {
            if (target == null)
            {
                return;
            }

            double minWidth = target.MinWidth > 0 ? target.MinWidth : FallbackMinWidth;
            double minHeight = target.MinHeight > 0 ? target.MinHeight : FallbackMinHeight;

            Rect normalizedBounds = new Rect(
                bounds.Left,
                bounds.Top,
                Math.Max(minWidth, bounds.Width),
                Math.Max(minHeight, bounds.Height));

            AppLogger.Debug(
                $"Quick layout requested bounds for {DescribeTarget(target)}: requested={DescribeRect(bounds)}, normalized={DescribeRect(normalizedBounds)}, min=({minWidth:F1},{minHeight:F1}), desktop={DescribeDesktop(desktop)}");

            if (target.WindowState != WindowState.Normal)
            {
                AppLogger.Debug($"Quick layout restoring window state to Normal for {DescribeTarget(target)}");
                target.WindowState = WindowState.Normal;
            }

            if (desktop != null)
            {
                bool moved = VirtualDesktopCatalog.TryMoveWindowToDesktop(target, desktop);
                AppLogger.Debug($"Quick layout desktop move result for {DescribeTarget(target)} -> {DescribeDesktop(desktop)} success={moved}");
            }

            target.Left = normalizedBounds.Left;
            target.Top = normalizedBounds.Top;
            target.Width = normalizedBounds.Width;
            target.Height = normalizedBounds.Height;

            if (target.NoteData != null)
            {
                target.NoteData.Left = target.Left;
                target.NoteData.Top = target.Top;
                target.NoteData.Width = target.Width;
                target.NoteData.Height = target.Height;
                NoteManager.Instance.UpdateNote(target.NoteData);
            }

            AppLogger.Debug($"Quick layout applied bounds for {DescribeTarget(target)}: actual={DescribeRect(new Rect(target.Left, target.Top, target.Width, target.Height))}");
            target.ReapplyWindowLevelAfterQuickLayout();
            target.Activate();
        }

        public static void RestoreDefaultSize(FloatingWindow target)
        {
            if (target == null)
            {
                return;
            }

            AppLogger.Debug($"Quick layout restore default size for {DescribeTarget(target)} -> {NoteManager.DefaultNoteWidth:F1}x{NoteManager.DefaultNoteHeight:F1}");
            ApplyWindowBounds(target, new Rect(target.Left, target.Top, NoteManager.DefaultNoteWidth, NoteManager.DefaultNoteHeight));
        }

        public static void MoveToMouseMonitor(FloatingWindow target)
        {
            if (target == null)
            {
                return;
            }

            Screen screen = Screen.FromPoint(Control.MousePosition);
            Rect workingArea = new(
                screen.WorkingArea.Left,
                screen.WorkingArea.Top,
                screen.WorkingArea.Width,
                screen.WorkingArea.Height);

            double width = Math.Min(target.Width, workingArea.Width);
            double height = Math.Min(target.Height, workingArea.Height);
            double left = workingArea.Left + Math.Max(0, (workingArea.Width - width) / 2);
            double top = workingArea.Top + Math.Max(0, (workingArea.Height - height) / 2);
            AppLogger.Debug(
                $"Quick layout move-to-mouse-monitor for {DescribeTarget(target)} -> screen='{screen.DeviceName}', workingArea={DescribeRect(workingArea)}, newBounds={DescribeRect(new Rect(left, top, width, height))}");
            ApplyWindowBounds(target, new Rect(left, top, width, height));
        }

        private static void ShowPicker(FloatingWindow target, QuickWindowLayoutMode mode)
        {
            if (target == null)
            {
                return;
            }

            AppLogger.Debug($"Opening quick layout picker: mode={mode}, target={DescribeTarget(target)}");
            WindowLayoutPickerWindow picker = new(target, mode)
            {
                Owner = target
            };

            picker.ShowDialog();
            AppLogger.Debug($"Quick layout picker closed: mode={mode}, target={DescribeTarget(target)}");
        }

        private static string DescribeTarget(FloatingWindow target)
        {
            if (target.NoteData == null)
            {
                return $"windowTitle='{target.Title}'";
            }

            return $"noteId={target.NoteData.Id}, title='{target.NoteData.Title}'";
        }

        private static string DescribeDesktop(VirtualDesktopEntry? desktop)
        {
            if (desktop == null)
            {
                return "current";
            }

            return $"{desktop.Name} ({desktop.Id})";
        }

        private static string DescribeRect(Rect rect)
        {
            return $"[{rect.Left:F1},{rect.Top:F1},{rect.Width:F1},{rect.Height:F1}]";
        }
    }
}
