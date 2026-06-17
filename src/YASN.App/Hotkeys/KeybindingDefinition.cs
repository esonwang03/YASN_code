using Avalonia.Input;
using YASN.Core;

namespace YASN.Hotkeys
{
    /// <summary>
    /// Describes one rebindable hotkey: its action, scope, default gesture, and label key. The
    /// current gesture is mutable so the registry can update it from persisted settings or capture.
    /// </summary>
    public sealed class KeybindingDefinition
    {
        /// <summary>
        /// Initializes a keybinding definition.
        /// </summary>
        /// <param name="action">The action the gesture triggers.</param>
        /// <param name="scope">The scope in which the gesture is dispatched.</param>
        /// <param name="defaultGesture">The factory-default gesture.</param>
        /// <param name="labelKey">The localization key for the action's display label.</param>
        public KeybindingDefinition(HotkeyAction action, HotkeyScope scope, KeyGesture defaultGesture, string labelKey)
        {
            Action = action;
            Scope = scope;
            DefaultGesture = defaultGesture;
            Gesture = defaultGesture;
            LabelKey = labelKey;
        }

        /// <summary>Gets the action this binding triggers.</summary>
        public HotkeyAction Action { get; }

        /// <summary>Gets the dispatch scope.</summary>
        public HotkeyScope Scope { get; }

        /// <summary>Gets the factory-default gesture used by reset.</summary>
        public KeyGesture DefaultGesture { get; }

        /// <summary>Gets the localization key for the action label.</summary>
        public string LabelKey { get; }

        /// <summary>
        /// Gets or sets the active gesture, or <see langword="null"/> when the action is unbound.
        /// </summary>
        public KeyGesture? Gesture { get; set; }

        /// <summary>Gets the local settings key under which the gesture is persisted.</summary>
        public string SettingKey => "hotkey." + Action;
    }
}
