using YASN.Infrastructure.Settings;
using YASN.Infrastructure.Sync.WebDav;

namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// Setting keys and a typed reader for the WebDAV sync configuration. All sync settings are
    /// machine-local (never replicated), so credentials and the chosen server stay on this device.
    /// </summary>
    public static class SyncSettings
    {
        /// <summary>Local key: whether sync is enabled.</summary>
        public const string EnabledKey = "sync.enabled";

        /// <summary>Local key: the WebDAV server URL.</summary>
        public const string UrlKey = "sync.url";

        /// <summary>Local key: the WebDAV username.</summary>
        public const string UserKey = "sync.user";

        /// <summary>Local key: the WebDAV password or token (stored plaintext — see plan risks).</summary>
        public const string PasswordKey = "sync.password";

        /// <summary>Local key: the remote root directory for note documents.</summary>
        public const string RemoteDirKey = "sync.remoteDir";

        /// <summary>Local key: the periodic sync interval in seconds.</summary>
        public const string IntervalSecondsKey = "sync.intervalSeconds";

        /// <summary>The default remote directory when none is configured.</summary>
        public const string DefaultRemoteDir = "yasn";

        /// <summary>The default sync interval in seconds.</summary>
        public const int DefaultIntervalSeconds = 120;

        /// <summary>The minimum allowed sync interval in seconds.</summary>
        public const int MinIntervalSeconds = 15;

        /// <summary>Gets whether sync is enabled.</summary>
        public static bool IsEnabled(SettingsStore store) =>
            string.Equals(store.GetValue(EnabledKey, shouldSync: false, "false"), "true", StringComparison.OrdinalIgnoreCase);

        /// <summary>Reads the remote root directory (trimmed, default applied).</summary>
        public static string RemoteDir(SettingsStore store)
        {
            string value = store.GetValue(RemoteDirKey, shouldSync: false, DefaultRemoteDir).Trim().Trim('/');
            return value.Length == 0 ? DefaultRemoteDir : value;
        }

        /// <summary>Reads the configured sync interval, clamped to the minimum.</summary>
        public static TimeSpan Interval(SettingsStore store)
        {
            string raw = store.GetValue(IntervalSecondsKey, shouldSync: false, DefaultIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int seconds))
            {
                seconds = DefaultIntervalSeconds;
            }

            return TimeSpan.FromSeconds(Math.Max(MinIntervalSeconds, seconds));
        }

        /// <summary>Builds WebDAV options from the stored credentials.</summary>
        public static WebDavOptions BuildOptions(SettingsStore store) => new WebDavOptions
        {
            ServerUrl = store.GetValue(UrlKey, shouldSync: false),
            Username = store.GetValue(UserKey, shouldSync: false),
            Password = store.GetValue(PasswordKey, shouldSync: false)
        };

        /// <summary>Gets whether a server URL has been configured.</summary>
        public static bool HasServer(SettingsStore store) =>
            !string.IsNullOrWhiteSpace(store.GetValue(UrlKey, shouldSync: false));
    }
}
