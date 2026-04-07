using System.Globalization;

namespace YASN.App.Settings
{
    internal static class NoteWindowUiSettings
    {
        internal const string SettingKey = "note.autoCollapseChrome";
        internal const bool DefaultValue = true;

        internal static bool IsAutoCollapseChromeEnabled(SettingsStore settingsStore)
        {
            string raw = settingsStore.GetValue(
                SettingKey,
                shouldSync: false,
                DefaultValue.ToString(CultureInfo.InvariantCulture));

            return !bool.TryParse(raw, out bool enabled) || enabled;
        }
    }
}
