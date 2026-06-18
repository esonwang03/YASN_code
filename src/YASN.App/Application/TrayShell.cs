using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform;
using YASN.AvaloniaNotes;
using YASN.Core;
using YASN.Hotkeys;
using YASN.Infrastructure.Settings;
using YASN.Localization;
using YASN.PlatformServices;
using YASN.Views;

namespace YASN.Application
{
    /// <summary>
    /// Owns the phase-one tray icon and its minimal application menu.
    /// </summary>
    public sealed class TrayShell
    {
        private readonly IClassicDesktopStyleApplicationLifetime desktopLifetime;
        private readonly NoteRepository repository;
        private readonly PlatformServiceBundle platformServices;
        private readonly LocalizationService localization;
        private readonly NoteWindowManager noteWindows;
        private readonly KeybindingRegistry keybindings;
        private readonly TutorialNoteSeeder tutorial;
        private readonly SettingsStore settings;
        private readonly SyncComposition? sync;
        private TrayIcon? trayIcon;
        private SettingsWindow? settingsWindow;
        private MainWindow? mainWindow;

        /// <summary>
        /// Initializes a new instance of the <see cref="TrayShell"/> class.
        /// </summary>
        /// <param name="desktopLifetime">The desktop lifetime that is shut down from the tray menu.</param>
        /// <param name="repository">The note repository.</param>
        /// <param name="platformServices">The platform service bundle.</param>
        /// <param name="localization">The localization service.</param>
        /// <param name="noteWindows">The shared note window manager.</param>
        /// <param name="keybindings">The shared keybinding registry for global hotkeys.</param>
        /// <param name="tutorial">The tutorial note seeder used on first run and from settings.</param>
        /// <param name="settings">The shared settings store passed to the settings and manager windows.</param>
        /// <param name="sync">The sync composition, or null when sync is unavailable.</param>
        public TrayShell(
            IClassicDesktopStyleApplicationLifetime desktopLifetime,
            NoteRepository repository,
            PlatformServiceBundle platformServices,
            LocalizationService localization,
            NoteWindowManager noteWindows,
            KeybindingRegistry keybindings,
            TutorialNoteSeeder tutorial,
            SettingsStore settings,
            SyncComposition? sync = null)
        {
            this.desktopLifetime = desktopLifetime;
            this.repository = repository;
            this.platformServices = platformServices;
            this.localization = localization;
            this.noteWindows = noteWindows;
            this.keybindings = keybindings;
            this.tutorial = tutorial;
            this.settings = settings;
            this.sync = sync;
        }

        /// <summary>
        /// Creates the tray icon and the phase-one exit command.
        /// </summary>
        public void Initialize()
        {
            NativeMenu menu = [];
            NativeMenuItem newNoteItem = new NativeMenuItem(localization["Menu.NewNote"]);
            newNoteItem.Click += (_, _) => noteWindows.Open(repository.CreateNote());
            menu.Items.Add(newNoteItem);

            NativeMenuItem openNoteItem = new NativeMenuItem(localization["Menu.OpenNote"]);
            openNoteItem.Click += (_, _) => OpenNoteWindow();
            menu.Items.Add(openNoteItem);

            NativeMenuItem manageNotesItem = new NativeMenuItem(localization["Menu.ManageNotes"]);
            manageNotesItem.Click += (_, _) => OpenMainWindow();
            menu.Items.Add(manageNotesItem);

            NativeMenuItem settingsItem = new NativeMenuItem(localization["Menu.Settings"]);
            settingsItem.Click += (_, _) => OpenSettingsWindow();
            menu.Items.Add(settingsItem);

            NativeMenuItem exitItem = new NativeMenuItem(localization["Menu.Exit"]);
            exitItem.Click += (_, _) => desktopLifetime.Shutdown();
            menu.Items.Add(exitItem);

            trayIcon = new TrayIcon
            {
                ToolTipText = "YASN",
                Icon = LoadIcon(),
                Menu = menu,
                IsVisible = true,
            };

            noteWindows.SetOpenMainWindowAction(OpenMainWindow);
            tutorial.SeedOnFirstRun();
            noteWindows.RestoreOpenNotes();
            RegisterGlobalHotkeys();
        }

        /// <summary>
        /// Registers the current global keybindings with the platform hotkey service, replacing any
        /// previous registration. Safe to call repeatedly (e.g. after settings change).
        /// </summary>
        private void RegisterGlobalHotkeys()
        {
            Dictionary<HotkeyAction, KeyGesture> bindings = new();
            foreach (KeybindingDefinition definition in keybindings.InScope(HotkeyScope.Global))
            {
                if (definition.Gesture is { } gesture)
                {
                    bindings[definition.Action] = gesture;
                }
            }

            platformServices.GlobalHotkeys.Register(bindings, DispatchGlobalAction);
        }

        private void DispatchGlobalAction(HotkeyAction action)
        {
            switch (action)
            {
                case HotkeyAction.RaiseMainWindow:
                    OpenMainWindow();
                    break;
                case HotkeyAction.RaiseSettingsWindow:
                    OpenSettingsWindow();
                    break;
                case HotkeyAction.CreateNote:
                    noteWindows.Open(repository.CreateNote());
                    break;
                default:
                    break;
            }
        }

        private void OpenNoteWindow()
        {
            AvaloniaNoteDocument note = repository.LoadAll().FirstOrDefault() ?? repository.CreateNote();
            noteWindows.Open(note);
        }

        private void OpenMainWindow()
        {
            if (mainWindow is not null)
            {
                mainWindow.Show();
                mainWindow.Activate();
                return;
            }

            mainWindow = new MainWindow(repository, noteWindows, platformServices, localization, keybindings, settings, OnSettingsSaved, sync?.Engine, ShowTutorialNote);
            mainWindow.Closed += (_, _) => mainWindow = null;
            mainWindow.Show();
        }

        private void OpenSettingsWindow()
        {
            if (settingsWindow is not null)
            {
                settingsWindow.Activate();
                return;
            }

            settingsWindow = new SettingsWindow(settings, localization, platformServices.AutoStart, keybindings, OnSettingsSaved, ShowTutorialNote);
            settingsWindow.Closed += (_, _) => settingsWindow = null;
            settingsWindow.Show();
        }

        /// <summary>
        /// Settings-action handler: creates a fresh tutorial note, opens it, and reports the result.
        /// </summary>
        private Task<string> ShowTutorialNote()
        {
            tutorial.CreateAndOpen();
            return Task.FromResult(localization["Settings.Tutorial.Added"]);
        }

        private void OnSettingsSaved()
        {
            noteWindows.RefreshTaskbarVisibilityForAll();
            RegisterGlobalHotkeys();
            sync?.ApplyConfiguration(settings);
        }

        private static WindowIcon LoadIcon()
        {
            Uri uri = new("avares://YASN/Resources/YASN.ico");
            Stream stream = AssetLoader.Open(uri);

            return new WindowIcon(stream);
        }
    }
}
