using Avalonia.Input;
using YASN.Core;

namespace YASN.PlatformServices
{
    /// <summary>
    /// Registers system-wide hotkeys that fire even when no application window has focus.
    /// Implementations are platform-specific; unsupported platforms use a no-op.
    /// </summary>
    public interface IGlobalHotkeyService : IDisposable
    {
        /// <summary>Gets whether the current platform supports global hotkeys.</summary>
        bool IsSupported { get; }

        /// <summary>
        /// Replaces all currently registered global hotkeys with the supplied set. Bindings whose
        /// gesture is unsupported or already taken by another process are skipped.
        /// </summary>
        /// <param name="bindings">A map of action to gesture to register.</param>
        /// <param name="onTriggered">The callback invoked on the UI thread when a hotkey fires.</param>
        void Register(IReadOnlyDictionary<HotkeyAction, KeyGesture> bindings, Action<HotkeyAction> onTriggered);
    }
}
