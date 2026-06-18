using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using YASN.Application;
using YASN.AvaloniaNotes;
using YASN.Core;
using YASN.Hotkeys;
using YASN.Infrastructure;
using YASN.Infrastructure.Settings;
using YASN.Infrastructure.Sync;
using YASN.Localization;
using YASN.PlatformServices;
using YASN.ViewModels;

namespace YASN.Views
{
    /// <summary>
    /// The note manager window: lists notes and offers create, open/close, delete, level, and
    /// quick-layout actions. Closing hides to the tray rather than exiting the application.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly NoteRepository repository;
        private readonly INoteWindowManager windows;
        private readonly PlatformServiceBundle platformServices;
        private readonly LocalizationService localization;
        private readonly KeybindingRegistry keybindings;
        private readonly SettingsStore settings;
        private readonly Action? onSettingsSaved;
        private readonly ThreeWaySyncEngine? sync;
        private readonly Func<Task<string>>? showTutorial;
        private readonly MainWindowViewModel viewModel;
        private SettingsWindow? settingsWindow;

        /// <summary>
        /// Initializes the manager window with its collaborators.
        /// </summary>
        /// <param name="repository">The note repository.</param>
        /// <param name="windows">The shared note window manager.</param>
        /// <param name="platformServices">The platform service bundle.</param>
        /// <param name="localization">The localization service.</param>
        /// <param name="keybindings">The keybinding registry backing the settings shortcuts module.</param>
        /// <param name="settings">The shared settings store passed to the settings window.</param>
        /// <param name="onSettingsSaved">Callback invoked after settings persist (re-applies hotkeys and visibility).</param>
        /// <param name="sync">The sync engine, or null when sync is unavailable.</param>
        /// <param name="showTutorial">Optional handler backing the settings "show tutorial note" action.</param>
        public MainWindow(
            NoteRepository repository,
            INoteWindowManager windows,
            PlatformServiceBundle platformServices,
            LocalizationService localization,
            KeybindingRegistry keybindings,
            SettingsStore settings,
            Action? onSettingsSaved = null,
            ThreeWaySyncEngine? sync = null,
            Func<Task<string>>? showTutorial = null)
        {
            this.repository = repository;
            this.windows = windows;
            this.platformServices = platformServices;
            this.localization = localization;
            this.keybindings = keybindings;
            this.settings = settings;
            this.onSettingsSaved = onSettingsSaved;
            this.sync = sync;
            this.showTutorial = showTutorial;
            InitializeComponent();

            viewModel = new MainWindowViewModel(repository, windows, sync);
            DataContext = viewModel;

            Button bottomMostButton = this.FindControl<Button>("CreateBottomMostButton")
                ?? throw new InvalidOperationException("CreateBottomMostButton was not found.");
            bottomMostButton.IsVisible = platformServices.WindowLevels.SupportsBottomMost;

            Button? syncButton = this.FindControl<Button>("SyncNowButton");
            syncButton?.IsVisible = sync is not null;

            windows.NotesChanged += HandleNotesChanged;
            Closing += HandleClosing;
            Closed += (_, _) => windows.NotesChanged -= HandleNotesChanged;
        }

        /// <summary>
        /// Initializes an empty window for the XAML designer.
        /// </summary>
        public MainWindow()
            : this(new NoteRepository(), new DesignNoteWindowManager(), PlatformServiceFactory.Create(), LocalizationService.Current,
                new KeybindingRegistry(new SettingsStore()), new SettingsStore())
        {
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void HandleNotesChanged(object? sender, EventArgs e)
        {
            viewModel.Refresh();
        }

        private void HandleClosing(object? sender, WindowClosingEventArgs e)
        {
            // Tray-resident: closing the manager hides it; the app exits from the tray Exit item.
            e.Cancel = true;
            Hide();
        }

        private static NoteListItemViewModel? RowOf(object? sender)
        {
            return (sender as Button)?.Tag as NoteListItemViewModel;
        }

        private void HandleToggleClick(object? sender, RoutedEventArgs e)
        {
            if (RowOf(sender) is { } row)
            {
                viewModel.ToggleOpen(row);
            }
        }

        private void HandleQuickLayoutClick(object? sender, RoutedEventArgs e)
        {
            if (RowOf(sender) is { } row)
            {
                viewModel.QuickLayout(row);
            }
        }

        private async void HandleRenameClick(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not NoteListItemViewModel row)
            {
                return;
            }

            RenameDialog dialog = new RenameDialog(
                row.Title,
                proposed => viewModel.Rename(row, proposed, out string? errorKey) ? null : errorKey,
                localization);

            await dialog.ShowDialog<string?>(this).ConfigureAwait(true);
        }

        private async void HandleDeleteClick(object? sender, RoutedEventArgs e)
        {
            if (RowOf(sender) is not { } row)
            {
                return;
            }

            ButtonResult result = await MessageBoxManager.GetMessageBoxStandard(
                localization["Main.Delete.Confirm.Title"],
                localization["Main.Delete.Confirm.Body"],
                ButtonEnum.YesNo).ShowAsync().ConfigureAwait(true);

            if (result == ButtonResult.Yes)
            {
                viewModel.Delete(row);
            }
        }

        private void HandleCreateNormalClick(object? sender, RoutedEventArgs e)
        {
            viewModel.CreateNote(WindowLevel.Normal);
        }

        private void HandleCreateTopMostClick(object? sender, RoutedEventArgs e)
        {
            viewModel.CreateNote(WindowLevel.TopMost);
        }

        private void HandleCreateBottomMostClick(object? sender, RoutedEventArgs e)
        {
            viewModel.CreateNote(WindowLevel.BottomMost);
        }

        private void HandleRefreshClick(object? sender, RoutedEventArgs e)
        {
            viewModel.Refresh();
        }

        private void HandleSyncNowClick(object? sender, RoutedEventArgs e)
        {
            viewModel.SyncNow();
        }

        private async void HandleResolveConflictClick(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not NoteListItemViewModel row)
            {
                return;
            }

            if (viewModel.ResolveConflict(row, out string? errorKey))
            {
                return;
            }

            string message = errorKey is null ? localization["Sync.Resolve.Failed"] : localization[errorKey];
            await MessageBoxManager.GetMessageBoxStandard(
                localization["Sync.Resolve.MenuItem"],
                message,
                ButtonEnum.Ok).ShowAsync().ConfigureAwait(true);
        }

        private void HandleOpenDataFolderClick(object? sender, RoutedEventArgs e)
        {
            SystemShellLauncher.Open(AppPaths.DataDirectory);
        }

        private void HandleHideToTrayClick(object? sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void HandleSettingsClick(object? sender, RoutedEventArgs e)
        {
            if (settingsWindow is not null)
            {
                settingsWindow.Activate();
                return;
            }

            settingsWindow = new SettingsWindow(settings, localization, platformServices.AutoStart, keybindings, OnSettingsSaved, showTutorial);
            settingsWindow.Closed += (_, _) => settingsWindow = null;
            settingsWindow.Show();
        }

        private void OnSettingsSaved()
        {
            // When hosted by the tray shell, its callback performs the full refresh (taskbar
            // visibility, global hotkeys, sync). Standalone, fall back to a local taskbar refresh.
            if (onSettingsSaved is not null)
            {
                onSettingsSaved();
            }
            else
            {
                windows.RefreshTaskbarVisibilityForAll();
            }
        }
    }
}
