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
using YASN.PlatformServices;
using YASN.Reminders;

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

                LocalizationService localization = new LocalizationService(new SettingsStore());
                LocalizationService.Current = localization;
                MigrateLegacyStorage();
                NoteRepository repository = new NoteRepository();
                ReminderStateStore reminderState = new ReminderStateStore(AppPaths.ReminderStatePath);
                ReminderScheduler reminders = new ReminderScheduler(platformServices.Notifications, reminderState);
                KeybindingRegistry keybindings = new KeybindingRegistry(new SettingsStore());
                NoteWindowManager noteWindows = new NoteWindowManager(repository, platformServices, reminders, keybindings);

                // Resolve the scheduler ↔ window-manager cycle: the writer needs the manager, the
                // manager needs the scheduler. Wire the in-app presenter and the once-rule writer now.
                reminders.Presenter = new AvaloniaReminderPresenter();
                reminders.ContentWriter = new ReminderContentWriter(repository, noteWindows);
                TutorialNoteSeeder tutorial = new TutorialNoteSeeder(repository, noteWindows, new SettingsStore());
                sync = new SyncComposition(repository);
                trayShell = new TrayShell(desktopLifetime, repository, platformServices, localization, noteWindows, keybindings, tutorial, sync);
                trayShell.Initialize();
                sync.ApplyConfiguration();
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
