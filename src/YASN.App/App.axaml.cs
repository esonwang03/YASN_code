using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using YASN.Application;
using YASN.AvaloniaNotes;
using YASN.Diagnostics;
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
using YASN.Theming;

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
        private Cli.CliIpcServer? cliServer;

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

                // Catch UI-thread faults now that the dispatcher and notification service exist, so a
                // bad operation during startup or later is logged and surfaced rather than killing the
                // tray. Process-wide handlers were already registered in Program.Main.
                GlobalExceptionHandler.RegisterUiThread(NotifyUnhandledException);

                SettingsStore settings = new();
                ApplyDiagnoseMode(settings);
                LocalizationService localization = new(settings);
                LocalizationService.Current = localization;
                ApplyTheme(settings);
                MigrateLegacyStorage();
                PreviewStyleManager.EnsureInitialized();
                NoteRepository repository = new();
                ReminderStateStore reminderState = new(AppPaths.ReminderStatePath);
                ReminderScheduler reminders = new(platformServices.Notifications, reminderState);
                KeybindingRegistry keybindings = new(settings);
                NoteWindowManager noteWindows = new(repository, platformServices, reminders, keybindings, settings);

                // Resolve the scheduler ↔ window-manager cycle: the writer needs the manager, the
                // manager needs the scheduler. Wire the in-app activator and the once-rule writer now.
                reminders.Activator = new NoteWindowReminderActivator(noteWindows, settings);
                reminders.ContentWriter = new NoteContentWriter(repository, noteWindows);
                TutorialNoteSeeder tutorial = new(repository, noteWindows, settings);
                sync = new SyncComposition(repository, platformServices.Notifications);
                sync.Engine.ConfirmBulkChanges = plan => ConfirmSyncDeletionsAsync(plan, localization, desktopLifetime);
                trayShell = new TrayShell(desktopLifetime, repository, platformServices, localization, noteWindows, keybindings, tutorial, settings, sync);
                trayShell.Initialize();
                sync.ApplyConfiguration(settings);
                WarnAboutUnrecognizedSettings(settings, platformServices, keybindings);

                // Host the CLI inter-process channel so `yasn <verb>` invocations route window,
                // delete, and sync commands into this live instance. Disposed on ProcessExit.
                Cli.CliCommandRouter router = new Cli.CliCommandRouter(
                    repository, noteWindows, sync, trayShell.RaiseMainWindow, trayShell.RaiseSettingsWindow);
                cliServer = new Cli.CliIpcServer(router);
                cliServer.Start();
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Applies the persisted diagnose-mode preference, raising the log console and enabling preview
        /// developer tools when set. Read from local (machine-specific) settings; safe to call again
        /// after a settings save to pick up a live toggle.
        /// </summary>
        /// <param name="settings">The settings store holding the diagnose preference.</param>
        public static void ApplyDiagnoseMode(SettingsStore settings)
        {
            string value = settings.GetValue(SettingsSchemaBuilder.DiagnoseKey, shouldSync: false, "false");
            DiagnoseMode.SetEnabled(string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Applies the persisted theme preference to the application's requested theme variant.
        /// A null variant (the "system" preference) leaves the requested variant unset so Avalonia
        /// follows the operating-system theme. Safe to call repeatedly, including after a settings save.
        /// </summary>
        /// <param name="settings">The settings store holding the theme preference.</param>
        public static void ApplyTheme(SettingsStore settings)
        {
            string value = settings.GetValue(ThemePreference.SettingKey, shouldSync: true, ThemePreference.DefaultValue);
            if (Avalonia.Application.Current is { } app)
            {
                app.RequestedThemeVariant = ThemePreference.ToVariant(value);
            }
        }

        /// <summary>
        /// Shows the bulk-deletion confirmation dialog on the UI thread and returns whether the user
        /// approved applying the sync's pending deletions. The dialog is parented to the active window
        /// when one is open, otherwise shown ownerless. Any UI failure denies the deletions, since the
        /// safe default is to keep notes rather than delete them unconfirmed.
        /// </summary>
        private static async Task<bool> ConfirmSyncDeletionsAsync(
            Infrastructure.Sync.SyncChangePlan plan,
            LocalizationService localization,
            IClassicDesktopStyleApplicationLifetime lifetime)
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    Views.BulkChangeDialog dialog = new(plan, localization);
                    Window? owner = lifetime.Windows.FirstOrDefault(w => w.IsActive)
                        ?? lifetime.Windows.FirstOrDefault(w => w.IsVisible);
                    if (owner is not null)
                    {
                        return await dialog.ShowDialog<bool>(owner).ConfigureAwait(true);
                    }

                    return await ShowOwnerlessAsync(dialog).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or ObjectDisposedException)
                {
                    // Any failure to display the dialog (a closing window, or a shutting-down dispatcher
                    // raising OperationCanceledException/ObjectDisposedException) must deny the deletions:
                    // the safe default is to keep notes rather than delete them unconfirmed. Swallowing
                    // here also stops the exception from faulting the fire-and-forget sync pass that
                    // awaited this callback.
                    AppLogger.Warn($"Sync deletion confirmation failed to display: {ex.Message}");
                    return false;
                }
            }).ConfigureAwait(false);
        }

        private static async Task<bool> ShowOwnerlessAsync(Views.BulkChangeDialog dialog)
        {
            TaskCompletionSource<bool> tcs = new();
            dialog.Closed += (_, _) => tcs.TrySetResult(dialog.GetResult());
            dialog.Show();
            return await tcs.Task.ConfigureAwait(true);
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
        /// Surfaces a handled UI-thread exception as a tray notification so the user knows something
        /// failed and was logged. The exception detail is kept out of the toast (it is already in the
        /// log); failure to notify is non-fatal and intentionally ignored by the caller.
        /// </summary>
        private void NotifyUnhandledException(Exception ex)
        {
            if (platformServices is null)
            {
                return;
            }

            NotificationRequest request = new NotificationRequest(
                LocalizationService.Current["App.Unhandled.Title"],
                LocalizationService.Current["App.Unhandled.Body"],
                "app:unhandled");
            _ = platformServices.Notifications.SendAsync(request);
        }

        /// <summary>
        /// Releases platform resources owned by the application.
        /// </summary>
        public override void RegisterServices()
        {
            base.RegisterServices();
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                cliServer?.Dispose();
                sync?.Dispose();
                platformServices?.Dispose();
            };
        }
    }
}
