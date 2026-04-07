using System.Globalization;
using YASN.Infrastructure.Logging;
using YASN.App.Settings;

namespace YASN.Infrastructure.Sync.WebDav
{
    /// <summary>
    /// Restores WebDAV sync configuration from persisted settings during application startup.
    /// </summary>
    internal static class WebDavSyncBootstrapper
    {
        private const string ServerUrlKey = "webdav.server";
        private const string UserKey = "webdav.user";
        private const string PasswordKey = "webdav.password";
        private const string RemoteDirectoryKey = "webdav.remote";
        private const string AutoSyncKey = "webdav.autoSync";
        private const string IntervalKey = "webdav.syncIntervalSeconds";
        private const int DefaultIntervalSeconds = 300;

        /// <summary>
        /// Attempts to configure the sync manager from saved WebDAV settings.
        /// </summary>
        /// <param name="syncManager">The sync manager to configure.</param>
        /// <returns><c>true</c> when a valid saved configuration is applied.</returns>
        internal static async Task<bool> TryConfigureFromSavedSettingsAsync(SyncManager syncManager)
        {
            ArgumentNullException.ThrowIfNull(syncManager);

            SettingsStore settingsStore = new SettingsStore();
            string serverUrl = settingsStore.GetValue(ServerUrlKey, shouldSync: false).Trim();
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                AppLogger.Debug("WebDAV sync bootstrap skipped because no server URL is configured.");
                return false;
            }

            WebDavOptions options = new WebDavOptions
            {
                ServerUrl = serverUrl,
                Username = settingsStore.GetValue(UserKey, shouldSync: false).Trim(),
                Password = settingsStore.GetValue(PasswordKey, shouldSync: false)
            };

            string remoteDirectory = NormalizeRemoteDirectory(settingsStore.GetValue(RemoteDirectoryKey, shouldSync: false));
            bool enableAutoSync = ParseAutoSync(settingsStore.GetValue(AutoSyncKey, shouldSync: false));
            int intervalSeconds = ParseIntervalSeconds(settingsStore.GetValue(IntervalKey, shouldSync: true));

            try
            {
                WebDavSyncClient client = new WebDavSyncClient(options);
                bool configured = await syncManager
                    .ConfigureAsync(client, remoteDirectory, enableAutoSync, intervalSeconds)
                    .ConfigureAwait(false);

                if (!configured)
                {
                    AppLogger.Warn("Failed to restore WebDAV sync from saved settings.");
                }

                return configured;
            }
            catch (ArgumentException ex)
            {
                AppLogger.Warn($"WebDAV sync bootstrap failed: {ex.Message}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Warn($"WebDAV sync bootstrap failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Normalizes a saved remote directory by trimming whitespace and slashes.
        /// </summary>
        /// <param name="value">The persisted directory value.</param>
        /// <returns>A normalized relative remote directory.</returns>
        private static string NormalizeRemoteDirectory(string value)
        {
            return (value ?? string.Empty).Trim().Trim('/');
        }

        /// <summary>
        /// Parses the saved auto-sync toggle and defaults to disabled when invalid.
        /// </summary>
        /// <param name="value">The persisted toggle value.</param>
        /// <returns><c>true</c> when auto-sync should be enabled.</returns>
        private static bool ParseAutoSync(string value)
        {
            return bool.TryParse(value, out bool enabled) && enabled;
        }

        /// <summary>
        /// Parses the saved sync interval and clamps it to the supported minimum.
        /// </summary>
        /// <param name="value">The persisted interval text.</param>
        /// <returns>A valid interval in seconds.</returns>
        private static int ParseIntervalSeconds(string value)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) && seconds > 0)
            {
                return Math.Max(10, seconds);
            }

            return DefaultIntervalSeconds;
        }
    }
}
