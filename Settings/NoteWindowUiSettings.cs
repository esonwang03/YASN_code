namespace YASN.Settings
{
    internal static class NoteWindowUiSettings
    {
        internal const string AutoCollapseChromeKey = "note.autoCollapseChrome";
        internal const bool DefaultAutoCollapseChrome = true;

        internal static bool IsAutoCollapseChromeEnabled(SettingsStore settingsStore)
        {
            var raw = settingsStore.GetValue(
                AutoCollapseChromeKey,
                shouldSync: false,
                DefaultAutoCollapseChrome.ToString());

            return !bool.TryParse(raw, out var enabled) || enabled;
        }
    }
}
