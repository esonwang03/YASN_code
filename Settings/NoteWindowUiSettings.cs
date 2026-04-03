namespace YASN.Settings
{
    internal static class NoteWindowUiSettings
    {
        internal const string SettingKey = "note.autoCollapseChrome";
        internal const bool DefaultValue = true;

        internal static bool IsAutoCollapseChromeEnabled(SettingsStore settingsStore)
        {
            var raw = settingsStore.GetValue(
                SettingKey,
                shouldSync: false,
                DefaultValue.ToString());

            return !bool.TryParse(raw, out var enabled) || enabled;
        }
    }
}
