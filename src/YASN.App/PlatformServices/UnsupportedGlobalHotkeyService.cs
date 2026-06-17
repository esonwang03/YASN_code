using Avalonia.Input;
using YASN.Core;

namespace YASN.PlatformServices
{
    /// <summary>
    /// A no-op global hotkey service for platforms without a system-wide registration path.
    /// </summary>
    public sealed class UnsupportedGlobalHotkeyService : IGlobalHotkeyService
    {
        /// <inheritdoc/>
        public bool IsSupported => false;

        /// <inheritdoc/>
        public void Register(IReadOnlyDictionary<HotkeyAction, KeyGesture> bindings, Action<HotkeyAction> onTriggered)
        {
        }

        /// <inheritdoc/>
        public void Dispose()
        {
        }
    }
}
