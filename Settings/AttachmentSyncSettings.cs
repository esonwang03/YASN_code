using System.Globalization;

namespace YASN.Settings
{
    internal static class AttachmentSyncSettings
    {
        internal const string AutoSyncEnabledKey = "attachment.autoSyncEnabled";
        internal const string AutoSyncThresholdMbKey = "attachment.autoSyncThresholdMb";
        internal const bool DefaultAutoSyncEnabled = true;
        internal const int DefaultAutoSyncThresholdMb = 5;

        internal static bool GetAutoSyncEnabled(SettingsStore settingsStore)
        {
            string raw = settingsStore.GetValue(AutoSyncEnabledKey, shouldSync: false, DefaultAutoSyncEnabled.ToString(CultureInfo.InvariantCulture));
            return !bool.TryParse(raw, out bool value) || value;
        }

        private static int GetAutoSyncThresholdMb(SettingsStore settingsStore)
        {
            string raw = settingsStore.GetValue(AutoSyncThresholdMbKey, shouldSync: false, DefaultAutoSyncThresholdMb.ToString(CultureInfo.InvariantCulture));
            return ParseThresholdMb(raw);
        }

        internal static long GetAutoSyncThresholdBytes(SettingsStore settingsStore)
        {
            return (long)GetAutoSyncThresholdMb(settingsStore) * 1024 * 1024;
        }

        internal static int ParseThresholdMb(string? value)
        {
            const int minMb = 1;
            const int maxMb = 1024;
            if (!int.TryParse(value, out int mb)) return DefaultAutoSyncThresholdMb;
            return mb switch
            {
                < minMb => minMb,
                > maxMb => maxMb,
                _ => mb
            };
        }
    }
}
