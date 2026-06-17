using System.Globalization;
using YASN.Infrastructure.Settings;

namespace YASN.Localization
{
    /// <summary>
    /// Provides runtime-switchable localized strings for Avalonia UI surfaces.
    /// </summary>
    public sealed class LocalizationService
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Catalog =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["App.Title"] = "YASN",
                    ["Menu.NewNote"] = "New note",
                    ["Menu.OpenNote"] = "Open note",
                    ["Menu.ManageNotes"] = "Manage notes",
                    ["Menu.Exit"] = "Exit",
                    ["Window.Close"] = "Close",
                    ["Window.Taskbar"] = "Taskbar",
                    ["Window.SetReminder"] = "Set reminder",
                    ["Window.EditorMode.Hint"] = "Switch editor view",
                    ["Window.EditorMode.PreviewOnly"] = "Preview",
                    ["Window.EditorMode.TextOnly"] = "Edit",
                    ["Window.EditorMode.TextAndPreview"] = "Split",
                    ["Window.QuickLayout"] = "Quick layout",
                    ["Window.QuickLayout.Hint"] = "Click to move · drag to resize · Esc to cancel",
                    ["Window.Resize.Grip"] = "Drag to resize",
                    ["Editor.InsertImage"] = "Insert image",
                    ["Editor.InsertAttachment"] = "Insert attachment",
                    ["Window.Level.Normal"] = "Normal",
                    ["Window.Level.TopMost"] = "Topmost",
                    ["Window.Level.BottomMost"] = "Bottom",
                    ["Window.Menu.Manage"] = "Open Manage Window",
                    ["Reminder.Save"] = "Save",
                    ["Reminder.Clear"] = "Clear",
                    ["Reminder.Window.Title"] = "Reminder",
                    ["Reminder.Window.Dismiss"] = "Dismiss",
                    ["Settings.Title"] = "Settings",
                    ["Menu.Settings"] = "Settings",
                    ["Settings.Save"] = "Save",
                    ["Settings.Language"] = "Language",
                    ["Main.Title"] = "Notes",
                    ["Main.Empty"] = "No notes yet. Create one to get started.",
                    ["Main.Create.Normal"] = "New (normal)",
                    ["Main.Create.TopMost"] = "New (topmost)",
                    ["Main.Create.BottomMost"] = "New (bottom)",
                    ["Main.Refresh"] = "Refresh",
                    ["Main.Settings"] = "Settings",
                    ["Main.OpenDataFolder"] = "Open data folder",
                    ["Main.HideToTray"] = "Hide to tray",
                    ["Main.Open"] = "Open",
                    ["Main.Close"] = "Close",
                    ["Main.Delete"] = "Delete",
                    ["Main.QuickLayout"] = "Quick layout",
                    ["Main.Status.Open"] = "Open",
                    ["Main.Status.Closed"] = "Closed",
                    ["Main.Delete.Confirm.Title"] = "Delete note",
                    ["Main.Delete.Confirm.Body"] = "Delete this note permanently? This cannot be undone.",
                    ["Rename.MenuItem"] = "Rename",
                    ["Rename.Title"] = "Rename note",
                    ["Rename.Save"] = "Save",
                    ["Rename.Cancel"] = "Cancel",
                    ["Rename.Empty"] = "Title can't be empty.",
                    ["Rename.Duplicate"] = "A note with that title already exists.",
                    ["Settings.Taskbar"] = "Show notes in taskbar",
                    ["Taskbar.Mode.AlwaysShow"] = "Always show",
                    ["Taskbar.Mode.AlwaysHide"] = "Never show",
                    ["Taskbar.Mode.HideTopMost"] = "Hide when topmost",
                    ["Settings.Browse"] = "Browse…",
                    ["Settings.Tutorial.Show"] = "Show tutorial note",
                    ["Settings.Tutorial.Added"] = "Tutorial note added.",
                    ["Settings.DataDir.Restart"] = "The data folder change takes effect after restarting the app.",
                    ["Settings.DataDir.Invalid"] = "That folder path is not valid.",
                    ["Settings.DataDir.Description"] = "Takes effect after restart.",
                    ["Sync.Now"] = "Sync now",
                    ["Sync.Conflict.Row"] = "Sync conflict — keep one copy, then mark solved.",
                    ["Sync.Resolve.MenuItem"] = "Mark solved",
                    ["Sync.Resolve.Failed"] = "Could not resolve the conflict.",
                    ["Sync.Resolve.None"] = "No note found for this conflict.",
                    ["Sync.Resolve.Duplicates"] = "Delete the duplicate copies first, leaving one note.",
                    ["Settings.Sync.Module"] = "Sync",
                    ["Settings.Sync.Enabled"] = "Enable sync",
                    ["Settings.Sync.Url"] = "WebDAV server URL",
                    ["Settings.Sync.User"] = "Username",
                    ["Settings.Sync.Password"] = "Password / token",
                    ["Settings.Sync.RemoteDir"] = "Remote directory",
                    ["Settings.Sync.Interval"] = "Sync interval (seconds)",
                    ["Settings.Sync.Test"] = "Test connection",
                    ["Settings.Sync.Test.Ok"] = "Connection succeeded.",
                    ["Settings.Sync.Test.Fail"] = "Connection failed. Check the URL and credentials.",
                    ["Settings.Shortcuts.Module"] = "Shortcuts",
                    ["Settings.Shortcuts.Reset"] = "Reset",
                    ["Settings.Shortcuts.Conflict"] = "The shortcut {0} is already used by '{1}'. Change it before saving '{2}'.",
                    ["Hotkey.RaiseMainWindow"] = "Open note manager (global)",
                    ["Hotkey.RaiseSettingsWindow"] = "Open settings (global)",
                    ["Hotkey.CreateNote"] = "New note (global)",
                    ["Hotkey.InsertImage"] = "Insert image",
                    ["Hotkey.InsertAttachment"] = "Insert attachment",
                    ["Hotkey.CycleEditorMode"] = "Switch editor view",
                    ["Hotkey.CycleWindowLevel"] = "Cycle window level",
                    ["Hotkey.QuickLayout"] = "Quick layout"
                }
            };

        private string currentCulture = "en";
        private readonly SettingsStore? settingsStore;

        /// <summary>
        /// Initializes a localization service without persistence.
        /// </summary>
        public LocalizationService()
        {
            Strings = new LocalizedStrings(this);
        }

        /// <summary>
        /// Initializes a localization service with persisted synced language selection.
        /// </summary>
        /// <param name="settingsStore">The settings store used for language persistence.</param>
        public LocalizationService(SettingsStore settingsStore)
        {
            this.settingsStore = settingsStore;
            currentCulture = settingsStore.GetValue(LocalizationSettings.LanguageKey, shouldSync: true, LocalizationSettings.DefaultLanguage);
            if (!Catalog.ContainsKey(currentCulture))
            {
                currentCulture = LocalizationSettings.DefaultLanguage;
            }

            Strings = new LocalizedStrings(this);
        }

        /// <summary>
        /// Gets or sets the shared service used by the <c>{l:Tr}</c> markup extension.
        /// </summary>
        public static LocalizationService Current { get; set; } = new LocalizationService();

        /// <summary>
        /// Raised when the active culture changes.
        /// </summary>
        public event EventHandler? CultureChanged;

        /// <summary>
        /// Gets the change-notifying string indexer bound by views.
        /// </summary>
        public LocalizedStrings Strings { get; }

        /// <summary>
        /// Gets the active culture name.
        /// </summary>
        public string CurrentCulture => currentCulture;

        /// <summary>
        /// Gets a localized string by key.
        /// </summary>
        /// <param name="key">The localization key.</param>
        public string this[string key] => Catalog[currentCulture].TryGetValue(key, out string? value) ? value : key;

        /// <summary>
        /// Changes the active culture.
        /// </summary>
        /// <param name="cultureName">The culture to activate.</param>
        public void SetCulture(string cultureName)
        {
            string normalized = string.IsNullOrWhiteSpace(cultureName) ? "en" : cultureName.Trim();
            if (!Catalog.ContainsKey(normalized))
            {
                throw new CultureNotFoundException($"Culture '{cultureName}' is not available.");
            }

            currentCulture = normalized;
            settingsStore?.SetValue(LocalizationSettings.LanguageKey, shouldSync: true, normalized);
            CultureChanged?.Invoke(this, EventArgs.Empty);
            Strings.RaiseAllChanged();
        }
    }
}
