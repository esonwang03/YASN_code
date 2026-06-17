using YASN.AvaloniaNotes;
using YASN.Infrastructure;
using YASN.Infrastructure.Settings;
using YASN.Infrastructure.Sync;
using YASN.Infrastructure.Sync.WebDav;

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

        /// <summary>
        /// Builds the sync engine over the given repository.
        /// </summary>
        /// <param name="repository">The local note repository.</param>
        public SyncComposition(NoteRepository repository)
        {
            stateStore = new SyncStateStore(AppPaths.SyncDatabasePath);
            Engine = new ThreeWaySyncEngine(repository, stateStore);
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
        public void ApplyConfiguration()
        {
            SettingsStore settingsStore = new SettingsStore();
            if (!SyncSettings.IsEnabled(settingsStore) || !SyncSettings.HasServer(settingsStore))
            {
                Engine.Reconfigure(clientFactory: null, remoteRoot: string.Empty, interval: TimeSpan.Zero);
                return;
            }

            WebDavOptions options = SyncSettings.BuildOptions(settingsStore);
            Engine.Reconfigure(
                () => new WebDavSyncClient(options),
                SyncSettings.RemoteDir(settingsStore),
                SyncSettings.Interval(settingsStore));

            _ = Engine.SyncNowAsync();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Engine.Dispose();
            stateStore.Dispose();
        }
    }
}
