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
                    ["Main.OpenCacheFolder"] = "Open cache folder",
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
                    ["Settings.Toggle.On"] = "On",
                    ["Settings.Toggle.Off"] = "Off",
                    ["Settings.Tutorial.Show"] = "Show tutorial note",
                    ["Settings.Tutorial.Added"] = "Tutorial note added.",
                    ["Settings.Migration.RunIds"] = "Migrate note IDs to GUID",
                    ["Settings.Migration.Running"] = "Migrating note IDs…",
                    ["Settings.Migration.Ok"] = "Note IDs migrated.",
                    ["Settings.Migration.NothingToDo"] = "Note IDs are already up to date.",
                    ["Settings.Migration.Failed"] = "Note ID migration failed. See the log for details.",
                    ["Settings.Reminder.ActivateOnFire"] = "Activate note and scroll to reminder when it fires",
                    ["Settings.RestoreOpenNotes"] = "Reopen notes that were open last time",
                    ["Settings.Data.DeleteAll"] = "Delete all data and quit",
                    ["Settings.Data.DeleteAll.Confirm.Title"] = "Delete all data",
                    ["Settings.Data.DeleteAll.Confirm.Body"] = "Permanently delete all notes, settings, and cached data on this computer? This cannot be undone.",
                    ["Settings.Data.DeleteAll.Cancelled"] = "Deletion cancelled.",
                    ["Settings.Theme"] = "Theme",
                    ["Settings.Theme.System"] = "Follow system",
                    ["Settings.Theme.Light"] = "Light",
                    ["Settings.Theme.Dark"] = "Dark",
                    ["Settings.Unrecognized.Title"] = "Some settings were not recognized",
                    ["Settings.Unrecognized.Body"] = "Old configuration from a previous version was found and is being ignored. Review your settings to re-apply them.",
                    ["Settings.DataDir.Restart"] = "The data folder change takes effect after restarting the app.",
                    ["Settings.DataDir.Invalid"] = "That folder path is not valid.",
                    ["Settings.DataDir.Description"] = "Takes effect after restart.",
                    ["Sync.Now"] = "Sync now",
                    ["Sync.Confirm.Title"] = "Confirm sync deletions",
                    ["Sync.Confirm.Body"] = "This sync will delete the following notes. Review before continuing.",
                    ["Sync.Confirm.DeleteLocal"] = "Delete here (removed on another device)",
                    ["Sync.Confirm.DeleteRemote"] = "Delete on server (removed here)",
                    ["Sync.Confirm.Proceed"] = "Apply deletions",
                    ["Sync.Confirm.Cancel"] = "Cancel sync",
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
                    ["Settings.Sync.DeleteGate"] = "Confirm deletions at",
                    ["Settings.Sync.DeleteGate.Description"] = "Ask before a sync deletes this many notes or more. Set to 1 to confirm every deletion.",
                    ["Settings.Sync.Test"] = "Test connection",
                    ["Settings.Sync.Test.Ok"] = "Connection succeeded.",
                    ["Settings.Sync.Test.Fail"] = "Connection failed. Check the URL and credentials.",
                    ["Settings.Sync.Test.BadCredentials"] = "Authentication failed. Check the username and password.",
                    ["Settings.Sync.Test.WebDavDisabled"] = "The server reached, but WebDAV is not enabled for this account.",
                    ["Settings.Sync.Test.EndpointNotFound"] = "No WebDAV endpoint at this URL. Check the address (it may need a path like /webdav).",
                    ["Settings.Sync.Test.DirectoryUnavailable"] = "Connected, but the remote folder could not be created. Check the path and permissions.",
                    ["Settings.Sync.Test.ReadWriteFailed"] = "Connected, but a test file could not be written and read back. Check write permissions and quota.",
                    ["Settings.Sync.Test.Unreachable"] = "Could not reach the server. Check the URL, network, and certificate.",
                    ["Settings.Sync.Test.NoETag"] = "Connected, but the server does not return ETags. Switch change detection to Last-Modified.",
                    ["Settings.Sync.ChangeDetection"] = "Change detection",
                    ["Settings.Sync.ChangeDetection.Description"] = "How remote edits are detected. Use ETag when supported; switch to Last-Modified for servers that omit ETags.",
                    ["Settings.Sync.ChangeDetection.ETag"] = "ETag (recommended)",
                    ["Settings.Sync.ChangeDetection.LastModified"] = "Last-Modified",
                    ["Sync.ETag.Unsupported.Title"] = "Sync change detection",
                    ["Sync.ETag.Unsupported.Body"] = "The server does not return ETags. Open Settings and switch change detection to Last-Modified.",
                    ["Settings.Shortcuts.Module"] = "Shortcuts",
                    ["Settings.Shortcuts.Reset"] = "Reset",
                    ["Settings.Shortcuts.Conflict"] = "The shortcut {0} is already used by '{1}'. Change it before saving '{2}'.",
                    ["Settings.Shortcuts.ConflictInline"] = "Conflicts with '{0}'.",
                    ["Hotkey.RaiseMainWindow"] = "Open note manager (global)",
                    ["Hotkey.RaiseSettingsWindow"] = "Open settings (global)",
                    ["Hotkey.CreateNote"] = "New note (global)",
                    ["Hotkey.InsertImage"] = "Insert image",
                    ["Hotkey.InsertAttachment"] = "Insert attachment",
                    ["Hotkey.CycleEditorMode"] = "Switch editor view",
                    ["Hotkey.CycleWindowLevel"] = "Cycle window level",
                    ["Hotkey.QuickLayout"] = "Quick layout",
                    ["Hotkey.ToggleChrome"] = "Show/hide title bar",
                    ["Window.ToggleChrome"] = "Show/hide title bar",
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
