using Avalonia.Input;
using YASN.Core;

namespace YASN.Hotkeys
{
    /// <summary>
    /// Matches key events against the editor-scoped keybindings and invokes the bound action. Each
    /// note window owns one controller wired to that window's action callbacks.
    /// </summary>
    public sealed class EditorHotkeyController
    {
        private readonly KeybindingRegistry registry;
        private readonly IReadOnlyDictionary<HotkeyAction, Action> handlers;

        /// <summary>
        /// Initializes the controller.
        /// </summary>
        /// <param name="registry">The shared keybinding registry.</param>
        /// <param name="handlers">A map of editor action to the callback that performs it.</param>
        public EditorHotkeyController(KeybindingRegistry registry, IReadOnlyDictionary<HotkeyAction, Action> handlers)
        {
            this.registry = registry;
            this.handlers = handlers;
        }

        /// <summary>
        /// Dispatches a key event to its bound editor action, if any.
        /// </summary>
        /// <param name="e">The key event raised on the focused note window.</param>
        /// <returns><see langword="true"/> when an action handled the event.</returns>
        public bool Handle(KeyEventArgs e)
        {
            // Avalonia surfaces a standalone modifier press as Key.LeftCtrl etc.; a gesture always
            // carries a non-modifier key, so KeyGesture matching ignores those naturally.
            KeyGesture gesture = new KeyGesture(e.Key, e.KeyModifiers);
            if (registry.Match(HotkeyScope.Editor, gesture) is not { } action)
            {
                return false;
            }

            if (!handlers.TryGetValue(action, out Action? handler))
            {
                return false;
            }

            handler();
            return true;
        }
    }
}
