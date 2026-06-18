using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using YASN.Application;
using YASN.AvaloniaNotes;
using YASN.Hotkeys;
using YASN.Infrastructure;
using YASN.Infrastructure.Reminders;
using YASN.Infrastructure.Settings;
using YASN.Localization;
using YASN.Migration;
using YASN.Notifications;
using YASN.PlatformServices;
using YASN.Reminders;
using YASN.SettingsUi;

namespace YASN
{
    /// <summary>
    /// Provides the Avalonia application root for the cross-platform desktop shell.
    /// </summary>
    public sealed partial class YasnApplication : Avalonia.Application
    {
        private TrayShell? trayShell;
        private PlatformServiceBundle? platformServices;
        private SyncComposition? sync;

        /// <summary>
        /// Loads Avalonia XAML resources for the application.
        /// </summary>
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Initializes the empty tray shell when the classic desktop lifetime is available.
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
            {
                platformServices = PlatformServiceFactory.Create();
                if (!platformServices.SingleInstance.HasPrimaryInstance)
                {
                    desktopLifetime.Shutdown();
                    return;
                }

                SettingsStore settings = new();
                LocalizationService localization = new(settings);
                LocalizationService.Current = localization;
                MigrateLegacyStorage();
                NoteRepository repository = new();
                ReminderStateStore reminderState = new(AppPaths.ReminderStatePath);
                ReminderScheduler reminders = new(platformServices.Notifications, reminderState);
                KeybindingRegistry keybindings = new(settings);
                NoteWindowManager noteWindows = new(repository, platformServices, reminders, keybindings, settings);

                // Resolve the scheduler ↔ window-manager cycle: the writer needs the manager, the
                // manager needs the scheduler. Wire the in-app activator and the once-rule writer now.
                reminders.Activator = new NoteWindowReminderActivator(noteWindows, settings);
                reminders.ContentWriter = new ReminderContentWriter(repository, noteWindows);
                TutorialNoteSeeder tutorial = new(repository, noteWindows, settings);
                sync = new SyncComposition(repository);
                trayShell = new TrayShell(desktopLifetime, repository, platformServices, localization, noteWindows, keybindings, tutorial, settings, sync);
                trayShell.Initialize();
                sync.ApplyConfiguration(settings);
                WarnAboutUnrecognizedSettings(settings, platformServices, keybindings);
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Upgrades a legacy WPF note store to the current schema before the repository loads it.
        /// Idempotent and failure-tolerant: any error is logged and startup continues, since a
        /// missing migration only means the user sees an empty list rather than a crash.
        /// </summary>
        private static void MigrateLegacyStorage()
        {
            MigrationReport report = WpfNoteStorageMigrator.Migrate(AppPaths.DataDirectory);
            if (report.Status == MigrationStatus.Migrated)
            {
                AppLogger.Info($"Migrated legacy note storage: {report.NotesMigrated} notes, backup at {report.BackupPath}.");
            }
            else if (report.Status == MigrationStatus.Failed)
            {
                AppLogger.Warn($"Legacy note storage migration failed: {string.Join("; ", report.Messages)}");
            }
        }

        /// <summary>
        /// Logs and notifies the user when the settings store holds keys the current schema no longer
        /// recognizes (typically old configuration from a previous version). The keys are ignored
        /// either way; this only surfaces that old config is not being applied. Failure-tolerant.
        /// </summary>
        private void WarnAboutUnrecognizedSettings(SettingsStore store, PlatformServiceBundle services, KeybindingRegistry keybindings)
        {
            try
            {
                SettingsViewModel schema = SettingsSchemaBuilder.Build(store, services.AutoStart, keybindings);
                IReadOnlyList<string> unrecognized = SettingsCompatibilityChecker.LogUnrecognizedKeys(store, schema, keybindings);
                if (unrecognized.Count == 0)
                {
                    return;
                }

                string body = LocalizationService.Current["Settings.Unrecognized.Body"];
                NotificationRequest request = new NotificationRequest(
                    LocalizationService.Current["Settings.Unrecognized.Title"], body, "settings:unrecognized");
                _ = services.Notifications.SendAsync(request);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                AppLogger.Warn($"Could not check settings for unrecognized keys: {ex.Message}");
            }
        }

        /// <summary>
        /// Releases platform resources owned by the application.
        /// </summary>
        public override void RegisterServices()
        {
            base.RegisterServices();
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                sync?.Dispose();
                platformServices?.Dispose();
            };
        }
    }
}
