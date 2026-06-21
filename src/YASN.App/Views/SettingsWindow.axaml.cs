using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using YASN.Core;
using YASN.Hotkeys;
using YASN.Infrastructure;
using YASN.Infrastructure.Settings;
using YASN.Localization;
using YASN.PlatformServices;
using YASN.SettingsUi;

namespace YASN.Views
{
    /// <summary>
    /// Schema-driven settings window that renders one editor per field type and persists changes.
    /// </summary>
    public sealed partial class SettingsWindow : Window
    {
        private readonly SettingsStore store;
        private readonly SettingsViewModel viewModel;
        private readonly LocalizationService localization;
        private readonly IAutoStartService autoStart;
        private readonly KeybindingRegistry keybindings;
        private readonly Action? onSaved;

        /// <summary>
        /// Initializes the settings window over a store and the active localization service.
        /// </summary>
        /// <param name="store">The settings store used to load and persist values.</param>
        /// <param name="localization">The localization service updated when the language changes.</param>
        /// <param name="autoStart">The auto-start service toggled by the launch-at-sign-in field.</param>
        /// <param name="keybindings">The registry backing the shortcuts module.</param>
        /// <param name="onSaved">An optional callback invoked after settings are persisted.</param>
        /// <param name="showTutorial">An optional handler that adds the tutorial note, backing the General-module action.</param>
        /// <param name="deleteAllData">An optional handler that wipes all app data, backing the General-module action.</param>
        public SettingsWindow(SettingsStore store, LocalizationService localization, IAutoStartService autoStart, KeybindingRegistry keybindings, Action? onSaved = null, Func<Task<string>>? showTutorial = null, Func<Task<string>>? deleteAllData = null)
        {
            this.store = store;
            this.localization = localization;
            this.autoStart = autoStart;
            this.keybindings = keybindings;
            this.onSaved = onSaved;
            InitializeComponent();

            viewModel = SettingsSchemaBuilder.Build(store, autoStart, keybindings, showTutorial, deleteAllData);
            DataContext = viewModel;

            foreach (SettingField field in EnumerateFields())
            {
                if (field.FieldType == SettingFieldType.Hotkey)
                {
                    field.OnChanged = _ => RevalidateHotkeys();
                }
            }

            RevalidateHotkeys();
        }

        /// <summary>
        /// Initializes an empty window for the XAML designer.
        /// </summary>
        public SettingsWindow()
            : this(new SettingsStore(), LocalizationService.Current, new UnsupportedAutoStartService(), new KeybindingRegistry(new SettingsStore()))
        {
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async void HandleSaveClick(object? sender, RoutedEventArgs e)
        {
            if (FindHotkeyConflict() is { } conflictMessage)
            {
                await ShowInfoAsync(conflictMessage).ConfigureAwait(true);
                return;
            }

            bool dataDirChanged = false;
            string? dataDirError = null;

            foreach (SettingModule module in viewModel.Modules)
            {
                foreach (SettingField field in module.Fields)
                {
                    if (field.FieldType == SettingFieldType.Hotkey)
                    {
                        // Hotkey fields persist through the registry below, not the store.
                        continue;
                    }

                    if (field.Key == SettingsSchemaBuilder.AutoStartKey)
                    {
                        ApplyAutoStart(field.BoolValue);
                        continue;
                    }

                    if (field.Key == AppPaths.DataDirectorySettingKey)
                    {
                        if (!TryPersistDataDirectory(field, out dataDirChanged))
                        {
                            dataDirError = localization["Settings.DataDir.Invalid"];
                        }

                        continue;
                    }

                    store.PersistField(field);

                    if (field.Key == LocalizationSettings.LanguageKey && !string.IsNullOrWhiteSpace(field.Value))
                    {
                        localization.SetCulture(field.Value);
                    }
                }
            }

            PersistHotkeys();
            onSaved?.Invoke();

            if (dataDirError is not null)
            {
                await ShowInfoAsync(dataDirError).ConfigureAwait(true);
                return;
            }

            if (dataDirChanged)
            {
                await ShowInfoAsync(localization["Settings.DataDir.Restart"]).ConfigureAwait(true);
            }

            Close();
        }

        /// <summary>
        /// Pushes every hotkey field's value into the registry and persists it. Done in one pass so
        /// the registry's in-memory state stays consistent with the global re-registration that
        /// follows in the save callback.
        /// </summary>
        private void PersistHotkeys()
        {
            Dictionary<string, string> values = new();
            foreach (SettingModule module in viewModel.Modules)
            {
                foreach (SettingField field in module.Fields)
                {
                    if (field.FieldType == SettingFieldType.Hotkey && !string.IsNullOrEmpty(field.Key))
                    {
                        values[field.Key] = field.Value;
                    }
                }
            }

            keybindings.ApplyAndPersist(values);
        }

        /// <summary>
        /// Returns a conflict message when two hotkey fields in the same scope share a gesture, or
        /// <see langword="null"/> when there is no conflict.
        /// </summary>
        private string? FindHotkeyConflict()
        {
            Dictionary<HotkeyScope, Dictionary<string, string>> seen = new();
            foreach (KeybindingDefinition definition in keybindings.Definitions)
            {
                SettingField? field = FindField(definition.SettingKey);
                if (field is null || string.IsNullOrWhiteSpace(field.Value))
                {
                    continue;
                }

                Dictionary<string, string> scopeMap = seen.TryGetValue(definition.Scope, out Dictionary<string, string>? map)
                    ? map
                    : seen[definition.Scope] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (scopeMap.TryGetValue(field.Value, out string? otherLabel))
                {
                    string template = localization["Settings.Shortcuts.Conflict"];
                    return string.Format(System.Globalization.CultureInfo.CurrentCulture, template,
                        field.Value, otherLabel, localization[definition.LabelKey]);
                }

                scopeMap[field.Value] = localization[definition.LabelKey];
            }

            return null;
        }

        /// <summary>
        /// Recomputes inline conflict messages for every hotkey field. Within each scope, the first
        /// field to claim a gesture is the "owner"; any later field with the same gesture is flagged
        /// with a message naming the owner. Fields without a conflict are cleared. Runs live on every
        /// capture so the user sees the conflict before pressing Save, where <see cref="FindHotkeyConflict"/>
        /// still blocks the persist as the final gate.
        /// </summary>
        private void RevalidateHotkeys()
        {
            Dictionary<HotkeyScope, Dictionary<string, string>> seen = new();
            foreach (KeybindingDefinition definition in keybindings.Definitions)
            {
                SettingField? field = FindField(definition.SettingKey);
                if (field is null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(field.Value))
                {
                    field.Error = string.Empty;
                    continue;
                }

                Dictionary<string, string> scopeMap = seen.TryGetValue(definition.Scope, out Dictionary<string, string>? map)
                    ? map
                    : seen[definition.Scope] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (scopeMap.TryGetValue(field.Value, out string? otherLabel))
                {
                    string template = localization["Settings.Shortcuts.ConflictInline"];
                    field.Error = string.Format(System.Globalization.CultureInfo.CurrentCulture, template, otherLabel);
                }
                else
                {
                    field.Error = string.Empty;
                    scopeMap[field.Value] = localization[definition.LabelKey];
                }
            }
        }

        private IEnumerable<SettingField> EnumerateFields()
        {
            foreach (SettingModule module in viewModel.Modules)
            {
                foreach (SettingField field in module.Fields)
                {
                    yield return field;
                }
            }
        }

        private SettingField? FindField(string key)
        {
            foreach (SettingField field in EnumerateFields())
            {
                if (field.Key == key)
                {
                    return field;
                }
            }

            return null;
        }

        private bool TryPersistDataDirectory(SettingField field, out bool changed)
        {
            changed = false;
            string previous = AppPaths.DataDirectory;

            if (!AppPaths.TryNormalizeDataDirectory(field.Value, out string normalized, out _))
            {
                return false;
            }

            field.Value = normalized;
            store.PersistField(field);
            changed = !string.Equals(normalized, previous, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private async Task ShowInfoAsync(string message)
        {
            await MsBox.Avalonia.MessageBoxManager
                .GetMessageBoxStandard(localization["Settings.Title"], message, MsBox.Avalonia.Enums.ButtonEnum.Ok)
                .ShowWindowDialogAsync(this)
                .ConfigureAwait(true);
        }

        private async void HandleActionClick(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.Tag is not SettingAction action || action.ExecuteAsync is null)
            {
                return;
            }

            SettingModule? module = viewModel.Modules.FirstOrDefault(m => m.Actions.Contains(action));
            if (module is not null)
            {
                module.Status = "…";
            }

            string result = await action.ExecuteAsync().ConfigureAwait(true);
            if (module is not null)
            {
                module.Status = result;
            }
        }

        private async void HandleBrowseFolderClick(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not SettingField field)
            {
                return;
            }

            IStorageProvider storage = StorageProvider;
            if (storage is null || !storage.CanPickFolder)
            {
                return;
            }

            IReadOnlyList<IStorageFolder> folders =
                await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    AllowMultiple = false,
                    Title = field.Title
                }).ConfigureAwait(true);

            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } localPath)
            {
                field.Value = localPath;
            }
        }

        private void HandleResetHotkeyClick(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is SettingField field)
            {
                field.Value = field.DefaultValue;
            }
        }

        private void ApplyAutoStart(bool enabled)
        {
            if (!autoStart.IsSupported)
            {
                return;
            }

            if (enabled)
            {
                autoStart.Enable();
            }
            else
            {
                autoStart.Disable();
            }
        }
    }
}
