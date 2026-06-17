using Avalonia.Input;
using YASN.Core;
using YASN.Infrastructure.Settings;

namespace YASN.Hotkeys
{
    /// <summary>
    /// Owns the catalog of rebindable hotkeys, loads and persists their gestures through the local
    /// settings store, and answers conflict and lookup queries used by the global and editor
    /// dispatchers.
    /// </summary>
    public sealed class KeybindingRegistry
    {
        private readonly SettingsStore store;
        private readonly List<KeybindingDefinition> definitions;

        /// <summary>
        /// Initializes the registry with factory defaults and overlays persisted gestures.
        /// </summary>
        /// <param name="store">The settings store used to load and persist gestures.</param>
        public KeybindingRegistry(SettingsStore store)
        {
            this.store = store;
            definitions = CreateDefaults();
            LoadPersisted();
        }

        /// <summary>Gets all keybinding definitions.</summary>
        public IReadOnlyList<KeybindingDefinition> Definitions => definitions;

        /// <summary>Gets the definitions in a given scope.</summary>
        /// <param name="scope">The scope to filter by.</param>
        public IEnumerable<KeybindingDefinition> InScope(HotkeyScope scope) =>
            definitions.Where(d => d.Scope == scope);

        /// <summary>
        /// Finds the action bound to a gesture within a scope, or <see langword="null"/> if none.
        /// </summary>
        /// <param name="scope">The scope to search.</param>
        /// <param name="gesture">The gesture to match.</param>
        public HotkeyAction? Match(HotkeyScope scope, KeyGesture gesture)
        {
            foreach (KeybindingDefinition definition in definitions)
            {
                if (definition.Scope == scope && definition.Gesture is { } bound && bound.Equals(gesture))
                {
                    return definition.Action;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the action that already uses a gesture in the same scope, excluding the action
        /// being edited. Used to surface conflicts before persisting a capture.
        /// </summary>
        /// <param name="scope">The scope to check within.</param>
        /// <param name="gesture">The candidate gesture.</param>
        /// <param name="excluding">The action being assigned, excluded from the check.</param>
        public HotkeyAction? FindConflict(HotkeyScope scope, KeyGesture gesture, HotkeyAction excluding)
        {
            foreach (KeybindingDefinition definition in definitions)
            {
                if (definition.Scope == scope
                    && definition.Action != excluding
                    && definition.Gesture is { } bound
                    && bound.Equals(gesture))
                {
                    return definition.Action;
                }
            }

            return null;
        }

        /// <summary>
        /// Parses a serialized gesture, returning <see langword="null"/> for blank or invalid input.
        /// </summary>
        /// <param name="text">The serialized gesture (e.g. <c>Ctrl+Shift+N</c>), or blank to unbind.</param>
        public static KeyGesture? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                return KeyGesture.Parse(text);
            }
            catch (FormatException ex)
            {
                AppLogger.Debug($"Ignored invalid hotkey gesture '{text}': {ex.Message}");
                return null;
            }
            catch (ArgumentException ex)
            {
                AppLogger.Debug($"Ignored invalid hotkey gesture '{text}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reapplies gestures from a settings-window field set (keyed by <see cref="KeybindingDefinition.SettingKey"/>)
        /// and persists each to the local store.
        /// </summary>
        /// <param name="values">A map of setting key to serialized gesture.</param>
        public void ApplyAndPersist(IReadOnlyDictionary<string, string> values)
        {
            foreach (KeybindingDefinition definition in definitions)
            {
                if (!values.TryGetValue(definition.SettingKey, out string? raw))
                {
                    continue;
                }

                definition.Gesture = Parse(raw);
                store.SetValue(definition.SettingKey, shouldSync: false, definition.Gesture?.ToString() ?? string.Empty);
            }
        }

        private void LoadPersisted()
        {
            foreach (KeybindingDefinition definition in definitions)
            {
                string raw = store.GetValue(definition.SettingKey, shouldSync: false, string.Empty);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    definition.Gesture = Parse(raw) ?? definition.DefaultGesture;
                }
            }
        }

        private static List<KeybindingDefinition> CreateDefaults()
        {
            return new List<KeybindingDefinition>
            {
                new(HotkeyAction.RaiseMainWindow, HotkeyScope.Global,
                    new KeyGesture(Key.M, KeyModifiers.Control | KeyModifiers.Alt), "Hotkey.RaiseMainWindow"),
                new(HotkeyAction.RaiseSettingsWindow, HotkeyScope.Global,
                    new KeyGesture(Key.OemComma, KeyModifiers.Control | KeyModifiers.Alt), "Hotkey.RaiseSettingsWindow"),
                new(HotkeyAction.CreateNote, HotkeyScope.Global,
                    new KeyGesture(Key.N, KeyModifiers.Control | KeyModifiers.Alt), "Hotkey.CreateNote"),
                new(HotkeyAction.InsertImage, HotkeyScope.Editor,
                    new KeyGesture(Key.I, KeyModifiers.Control | KeyModifiers.Shift), "Hotkey.InsertImage"),
                new(HotkeyAction.InsertAttachment, HotkeyScope.Editor,
                    new KeyGesture(Key.K, KeyModifiers.Control | KeyModifiers.Shift), "Hotkey.InsertAttachment"),
                new(HotkeyAction.CycleEditorMode, HotkeyScope.Editor,
                    new KeyGesture(Key.E, KeyModifiers.Control | KeyModifiers.Shift), "Hotkey.CycleEditorMode"),
                new(HotkeyAction.CycleWindowLevel, HotkeyScope.Editor,
                    new KeyGesture(Key.L, KeyModifiers.Control | KeyModifiers.Shift), "Hotkey.CycleWindowLevel"),
                new(HotkeyAction.QuickLayout, HotkeyScope.Editor,
                    new KeyGesture(Key.G, KeyModifiers.Control | KeyModifiers.Shift), "Hotkey.QuickLayout")
            };
        }
    }
}
