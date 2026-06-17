using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Threading;
using YASN.Core;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Registers system-wide hotkeys on Windows using a message-only window and the Win32
    /// <c>RegisterHotKey</c> API. <c>WM_HOTKEY</c> messages arrive on the UI thread's message loop
    /// (the window is created there) and are marshaled to the supplied callback via the dispatcher.
    /// </summary>
    public sealed partial class WindowsGlobalHotkeyService : IGlobalHotkeyService
    {
        private const int WmHotkey = 0x0312;

        private readonly Dictionary<int, HotkeyAction> registeredIds = new();
        private readonly WndProcDelegate wndProcDelegate;
        private readonly ushort classAtom;
        private readonly IntPtr hwnd;
        private Action<HotkeyAction>? onTriggered;
        private int nextId = 1;
        private bool disposed;

        /// <summary>
        /// Creates the hidden message window. Must be called on the UI thread so hotkey messages are
        /// pumped by Avalonia's loop.
        /// </summary>
        public WindowsGlobalHotkeyService()
        {
            wndProcDelegate = WndProc;
            string className = "YASN_GlobalHotkeyWindow_" + Environment.ProcessId;

            WNDCLASSEX wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProcDelegate),
                hInstance = GetModuleHandle(null),
                lpszClassName = className
            };

            classAtom = RegisterClassEx(ref wc);
            hwnd = CreateWindowEx(0, className, className, 0, 0, 0, 0, 0,
                HwndMessage, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        }

        /// <inheritdoc/>
        public bool IsSupported => hwnd != IntPtr.Zero;

        /// <inheritdoc/>
        public void Register(IReadOnlyDictionary<HotkeyAction, KeyGesture> bindings, Action<HotkeyAction> onTriggered)
        {
            if (!IsSupported)
            {
                return;
            }

            UnregisterAll();
            this.onTriggered = onTriggered;

            foreach (KeyValuePair<HotkeyAction, KeyGesture> binding in bindings)
            {
                if (!Win32KeyGestureConverter.TryConvert(binding.Value, out uint modifiers, out uint virtualKey))
                {
                    AppLogger.Debug($"Skipped unsupported global hotkey for {binding.Key}: {binding.Value}.");
                    continue;
                }

                int id = nextId++;
                if (RegisterHotKey(hwnd, id, modifiers, virtualKey))
                {
                    registeredIds[id] = binding.Key;
                }
                else
                {
                    AppLogger.Warn($"Failed to register global hotkey {binding.Value} for {binding.Key}; it may be in use by another app.");
                }
            }
        }

        private void UnregisterAll()
        {
            foreach (int id in registeredIds.Keys)
            {
                UnregisterHotKey(hwnd, id);
            }

            registeredIds.Clear();
        }

        private IntPtr WndProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == WmHotkey && registeredIds.TryGetValue(wParam.ToInt32(), out HotkeyAction action))
            {
                Action<HotkeyAction>? callback = onTriggered;
                if (callback is not null)
                {
                    // Already on the UI thread (the window lives there), but post to keep the WndProc
                    // return fast and avoid re-entrancy with dialogs the callback may open.
                    Dispatcher.UIThread.Post(() => callback(action));
                }

                return IntPtr.Zero;
            }

            return DefWindowProc(handle, message, wParam, lParam);
        }

        /// <summary>
        /// Unregisters all hotkeys and destroys the message window.
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (IsSupported)
            {
                UnregisterAll();
                DestroyWindow(hwnd);
            }

            if (classAtom != 0)
            {
                UnregisterClass(classAtom, GetModuleHandle(null));
            }
        }
    }
}
