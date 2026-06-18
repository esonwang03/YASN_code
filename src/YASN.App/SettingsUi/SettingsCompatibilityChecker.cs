using YASN.App.Settings;
using YASN.Hotkeys;
using YASN.Infrastructure;
using YASN.Infrastructure.Settings;

namespace YASN.SettingsUi
{
    /// <summary>
    /// Detects persisted settings keys that the current schema no longer recognizes — typically left
    /// behind by an older (e.g. WPF-era) build whose keys were renamed. These keys are otherwise
    /// silently ignored on load; this surfaces them so the user knows old configuration is not being
    /// applied.
    /// </summary>
    public static class SettingsCompatibilityChecker
    {
        // Keys persisted outside the visible settings schema that are still valid for the current
        // build, so they must not be reported as unrecognized.
        private static readonly string[] KnownInternalKeys =
        {
            "tutorial.seeded",
            "note.previewStyle"
        };

        /// <summary>
        /// Returns the stored keys that are not part of the current schema, hotkeys, or known internal
        /// keys, in sorted order. An empty result means every stored key is recognized.
        /// </summary>
        /// <param name="store">The settings store whose persisted keys are inspected.</param>
        /// <param name="schema">The built settings view model declaring the recognized field keys.</param>
        /// <param name="keybindings">The keybinding registry declaring the recognized hotkey keys.</param>
        /// <returns>The sorted unrecognized keys.</returns>
        public static IReadOnlyList<string> FindUnrecognizedKeys(
            SettingsStore store,
            SettingsViewModel schema,
            KeybindingRegistry keybindings)
        {
            HashSet<string> recognized = new(StringComparer.Ordinal);
            recognized.UnionWith(KnownInternalKeys);

            foreach (SettingModule module in schema.Modules)
            {
                foreach (SettingField field in module.Fields)
                {
                    if (!string.IsNullOrEmpty(field.Key))
                    {
                        recognized.Add(field.Key);
                    }
                }
            }

            foreach (KeybindingDefinition definition in keybindings.Definitions)
            {
                recognized.Add(definition.SettingKey);
            }

            List<string> unrecognized = store.GetAllStoredKeys()
                .Where(key => !recognized.Contains(key))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();

            return unrecognized;
        }

        /// <summary>
        /// Logs a warning for each unrecognized stored key and returns them. Call once at startup so the
        /// caller can decide whether to notify the user.
        /// </summary>
        /// <param name="store">The settings store whose persisted keys are inspected.</param>
        /// <param name="schema">The built settings view model declaring the recognized field keys.</param>
        /// <param name="keybindings">The keybinding registry declaring the recognized hotkey keys.</param>
        /// <returns>The sorted unrecognized keys (empty when all are recognized).</returns>
        public static IReadOnlyList<string> LogUnrecognizedKeys(
            SettingsStore store,
            SettingsViewModel schema,
            KeybindingRegistry keybindings)
        {
            IReadOnlyList<string> unrecognized = FindUnrecognizedKeys(store, schema, keybindings);
            foreach (string key in unrecognized)
            {
                AppLogger.Warn($"Ignoring unrecognized setting '{key}'; it may be old configuration from a previous version and will not be applied.");
            }

            return unrecognized;
        }
    }
}
