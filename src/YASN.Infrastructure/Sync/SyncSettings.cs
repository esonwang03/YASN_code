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

        /// <summary>
        /// Local key: the deletion count at or above which a sync pass asks the user to confirm before
        /// applying deletions. <c>1</c> confirms every deletion; higher values let routine single-note
        /// deletions sync silently.
        /// </summary>
        public const string DeleteGateThresholdKey = "sync.deleteGateThreshold";

        /// <summary>
        /// Local key: how the engine detects remote changes (<see cref="ChangeDetection.ETagValue"/> or
        /// <see cref="ChangeDetection.LastModifiedValue"/>). Defaults to ETag; switch to Last-Modified
        /// for servers that omit ETags.
        /// </summary>
        public const string ChangeDetectionKey = "sync.changeDetection";

        /// <summary>The default remote directory when none is configured.</summary>
        public const string DefaultRemoteDir = "yasn";

        /// <summary>The default sync interval in seconds.</summary>
        public const int DefaultIntervalSeconds = 120;

        /// <summary>The minimum allowed sync interval in seconds.</summary>
        public const int MinIntervalSeconds = 15;

        /// <summary>The default delete-gate threshold: confirm when two or more notes would be deleted.</summary>
        public const int DefaultDeleteGateThreshold = 2;

        /// <summary>The minimum delete-gate threshold: confirm every deletion.</summary>
        public const int MinDeleteGateThreshold = 1;

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

        /// <summary>Reads the delete-gate threshold, clamped to the minimum.</summary>
        public static int DeleteGateThreshold(SettingsStore store)
        {
            string raw = store.GetValue(DeleteGateThresholdKey, shouldSync: false, DefaultDeleteGateThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int threshold))
            {
                threshold = DefaultDeleteGateThreshold;
            }

            return Math.Max(MinDeleteGateThreshold, threshold);
        }

        /// <summary>Reads the configured change-detection mode (default ETag).</summary>
        public static ChangeDetectionMode ChangeDetection(SettingsStore store) =>
            Sync.ChangeDetection.Parse(store.GetValue(ChangeDetectionKey, shouldSync: false, Sync.ChangeDetection.ETagValue));

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
