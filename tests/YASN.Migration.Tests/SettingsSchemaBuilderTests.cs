using YASN.App.Settings;
using YASN.Hotkeys;
using YASN.Infrastructure;
using YASN.Infrastructure.Settings;
using YASN.Localization;
using YASN.PlatformServices;
using YASN.SettingsUi;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the settings schema exposes the expected fields.
    /// </summary>
    public sealed class SettingsSchemaBuilderTests
    {
        /// <summary>
        /// Includes the synced language field with a selectable English option.
        /// </summary>
        [Fact]
        public void BuildIncludesLanguageSelectField()
        {
            SettingsViewModel viewModel = SettingsSchemaBuilder.Build(new SettingsStore(), new UnsupportedAutoStartService(), new KeybindingRegistry(new SettingsStore()));

            SettingField language = viewModel.Modules
                .SelectMany(module => module.Fields)
                .Single(field => field.Key == LocalizationSettings.LanguageKey);

            Assert.Equal(SettingFieldType.Select, language.FieldType);
            Assert.True(language.ShouldSync);
            Assert.Contains(language.Options, option => option.Value == "en");
        }

        /// <summary>
        /// Includes the synced data-directory field with folder browsing enabled.
        /// </summary>
        [Fact]
        public void BuildIncludesDataDirectoryField()
        {
            SettingsViewModel viewModel = SettingsSchemaBuilder.Build(new SettingsStore(), new UnsupportedAutoStartService(), new KeybindingRegistry(new SettingsStore()));

            SettingField dataDirectory = viewModel.Modules
                .SelectMany(module => module.Fields)
                .Single(field => field.Key == AppPaths.DataDirectorySettingKey);

            Assert.Equal(SettingFieldType.Text, dataDirectory.FieldType);
            Assert.True(dataDirectory.EnableFolderBrowse);
        }

        /// <summary>
        /// Adds the auto-start toggle only when the service supports it, seeded from current state.
        /// </summary>
        [Fact]
        public void BuildAddsAutoStartToggleWhenSupported()
        {
            SettingsViewModel unsupported = SettingsSchemaBuilder.Build(new SettingsStore(), new UnsupportedAutoStartService(), new KeybindingRegistry(new SettingsStore()));
            Assert.DoesNotContain(
                unsupported.Modules.SelectMany(module => module.Fields),
                field => field.Key == SettingsSchemaBuilder.AutoStartKey);

            SettingsViewModel supported = SettingsSchemaBuilder.Build(new SettingsStore(), new StubAutoStartService(), new KeybindingRegistry(new SettingsStore()));
            SettingField toggle = supported.Modules
                .SelectMany(module => module.Fields)
                .Single(field => field.Key == SettingsSchemaBuilder.AutoStartKey);

            Assert.Equal(SettingFieldType.Toggle, toggle.FieldType);
            Assert.True(toggle.BoolValue);
        }

        private sealed class StubAutoStartService : IAutoStartService
        {
            public bool IsSupported => true;

            public bool IsEnabled => true;

            public void Enable()
            {
            }

            public void Disable()
            {
            }
        }
    }

    /// <summary>
    /// Verifies detection of persisted keys the current schema no longer recognizes (old config).
    /// </summary>
    public sealed class SettingsCompatibilityCheckerTests
    {
        /// <summary>
        /// A stored key that is neither a schema field, a hotkey, nor a known internal key is flagged
        /// as unrecognized, while a current schema key (the data directory) is not.
        /// </summary>
        [Fact]
        public void FlagsUnknownKeyButNotSchemaKey()
        {
            SettingsStore store = new SettingsStore();
            string staleKey = "webdav.__legacy_" + Guid.NewGuid().ToString("N");
            store.SetValue(staleKey, shouldSync: false, "old value");

            KeybindingRegistry keybindings = new KeybindingRegistry(new SettingsStore());
            SettingsViewModel schema = SettingsSchemaBuilder.Build(store, new UnsupportedAutoStartService(), keybindings);

            IReadOnlyList<string> unrecognized = SettingsCompatibilityChecker.FindUnrecognizedKeys(store, schema, keybindings);

            Assert.Contains(staleKey, unrecognized);
            Assert.DoesNotContain(AppPaths.DataDirectorySettingKey, unrecognized);
        }

        /// <summary>
        /// A real keybinding key and a known internal key are recognized and never reported.
        /// </summary>
        [Fact]
        public void DoesNotFlagHotkeyOrInternalKeys()
        {
            SettingsStore store = new SettingsStore();
            store.SetValue("tutorial.seeded", shouldSync: false, "true");

            KeybindingRegistry keybindings = new KeybindingRegistry(new SettingsStore());
            string realHotkeyKey = keybindings.Definitions.First().SettingKey;
            store.SetValue(realHotkeyKey, shouldSync: false, "Ctrl+Alt+M");

            SettingsViewModel schema = SettingsSchemaBuilder.Build(store, new UnsupportedAutoStartService(), keybindings);

            IReadOnlyList<string> unrecognized = SettingsCompatibilityChecker.FindUnrecognizedKeys(store, schema, keybindings);

            Assert.DoesNotContain("tutorial.seeded", unrecognized);
            Assert.DoesNotContain(realHotkeyKey, unrecognized);
        }
    }

    /// <summary>
    /// Guards the store's handling of registry-owned hotkey fields.
    /// </summary>
    public sealed class SettingsStoreHotkeyTests
    {
        /// <summary>
        /// ApplyValues must leave Hotkey fields untouched: they are seeded from the keybinding
        /// registry, so a stale empty value in the store must not blank a registry-provided gesture
        /// (which a later save would otherwise persist as an unbind).
        /// </summary>
        [Fact]
        public void ApplyValuesLeavesHotkeyFieldsUntouched()
        {
            SettingsStore store = new SettingsStore();
            string key = "hotkey.__test_" + Guid.NewGuid().ToString("N");
            store.SetValue(key, shouldSync: false, string.Empty);

            SettingField hotkey = new SettingField
            {
                Key = key,
                FieldType = SettingFieldType.Hotkey,
                ShouldSync = false,
                Value = "Ctrl+Alt+M"
            };

            store.ApplyValues(new[] { hotkey });

            Assert.Equal("Ctrl+Alt+M", hotkey.Value);
        }

        /// <summary>
        /// Non-hotkey fields still receive persisted store values, confirming the skip is targeted.
        /// </summary>
        [Fact]
        public void ApplyValuesStillAppliesNonHotkeyFields()
        {
            SettingsStore store = new SettingsStore();
            string key = "text.__test_" + Guid.NewGuid().ToString("N");
            store.SetValue(key, shouldSync: false, "persisted");

            SettingField text = new SettingField
            {
                Key = key,
                FieldType = SettingFieldType.Text,
                ShouldSync = false,
                Value = "seed"
            };

            store.ApplyValues(new[] { text });

            Assert.Equal("persisted", text.Value);
        }
    }
}
