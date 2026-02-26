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
            var raw = settingsStore.GetValue(AutoSyncEnabledKey, shouldSync: false, DefaultAutoSyncEnabled.ToString());
            return !bool.TryParse(raw, out var value) || value;
        }

        private static int GetAutoSyncThresholdMb(SettingsStore settingsStore)
        {
            var raw = settingsStore.GetValue(AutoSyncThresholdMbKey, shouldSync: false, DefaultAutoSyncThresholdMb.ToString());
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
            if (int.TryParse(value, out var mb))
            {
                if (mb < minMb)
                {
                    return minMb;
                }

                if (mb > maxMb)
                {
                    return maxMb;
                }

                return mb;
            }

            return DefaultAutoSyncThresholdMb;
        }
    }
}
