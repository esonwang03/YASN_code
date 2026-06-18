using YASN.AvaloniaNotes;
using YASN.Infrastructure;
using YASN.Infrastructure.Settings;
using YASN.Infrastructure.Sync;
using YASN.Infrastructure.Sync.WebDav;
using YASN.Notifications;

namespace YASN.Application
{
    /// <summary>
    /// Owns the sync engine and its state store for the app lifetime, and (re)applies the WebDAV
    /// configuration from the settings store. Created once at startup; <see cref="ApplyConfiguration"/>
    /// is re-invoked whenever settings are saved.
    /// </summary>
    public sealed class SyncComposition : IDisposable
    {
        private readonly SyncStateStore stateStore;
        private readonly INotificationService? notifications;

        /// <summary>
        /// Builds the sync engine over the given repository.
        /// </summary>
        /// <param name="repository">The local note repository.</param>
        /// <param name="notifications">Optional service used to toast capability warnings (e.g. missing ETags).</param>
        public SyncComposition(NoteRepository repository, INotificationService? notifications = null)
        {
            stateStore = new SyncStateStore(AppPaths.SyncDatabasePath);
            Engine = new ThreeWaySyncEngine(repository, stateStore);
            this.notifications = notifications;
        }

        /// <summary>
        /// Gets the sync engine shared across the UI.
        /// </summary>
        public ThreeWaySyncEngine Engine { get; }

        /// <summary>
        /// Reads the current settings and reconfigures the engine: enables the periodic timer when
        /// sync is on and a server is set, otherwise disables sync. A fresh store is read each call so
        /// changes saved in the settings window take effect immediately.
        /// </summary>
        public void ApplyConfiguration(SettingsStore settingsStore)
        {
            if (!SyncSettings.IsEnabled(settingsStore) || !SyncSettings.HasServer(settingsStore))
            {
                Engine.Reconfigure(clientFactory: null, remoteRoot: string.Empty, interval: TimeSpan.Zero);
                return;
            }

            WebDavOptions options = SyncSettings.BuildOptions(settingsStore);
            Engine.BulkDeleteThreshold = SyncSettings.DeleteGateThreshold(settingsStore);
            Engine.ChangeDetection = SyncSettings.ChangeDetection(settingsStore);
            Engine.Reconfigure(
                () => new WebDavSyncClient(options),
                SyncSettings.RemoteDir(settingsStore),
                SyncSettings.Interval(settingsStore));

            // Warn (log + toast) if ETag detection is selected but the server does not actually return
            // ETags, since that silently masks remote edits. Fire-and-forget: never block config apply.
            if (Engine.ChangeDetection == ChangeDetectionMode.ETag)
            {
                _ = WarnIfETagsUnsupportedAsync(options, SyncSettings.RemoteDir(settingsStore));
            }

            _ = Engine.SyncNowAsync();
        }

        private async Task WarnIfETagsUnsupportedAsync(WebDavOptions options, string remoteDir)
        {
            try
            {
                using WebDavSyncClient client = new WebDavSyncClient(options);
                bool? supported = await client.SupportsETagsAsync(remoteDir).ConfigureAwait(false);
                if (supported == false && notifications is not null)
                {
                    AppLogger.Warn("Sync: server did not return an ETag for the probe file; recommend switching change detection to Last-Modified.");
                    await notifications.SendAsync(new Notifications.NotificationRequest(
                        Localization.LocalizationService.Current["Sync.ETag.Unsupported.Title"],
                        Localization.LocalizationService.Current["Sync.ETag.Unsupported.Body"],
                        "sync:etag-unsupported")).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
            {
                // Probing is best-effort; a failure here must not disrupt startup or saving settings.
                AppLogger.Debug($"Sync: ETag capability probe failed: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Engine.Dispose();
            stateStore.Dispose();
        }
    }
}
