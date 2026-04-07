using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using YASN.App.Notes;
using YASN.Core;
using YASN.Infrastructure;
using YASN.Infrastructure.Logging;
using YASN.Infrastructure.Sync;
using YASN.App.WindowLayout;
using MessageBox = ModernWpf.MessageBox;

namespace YASN
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Converters are already defined in MainWindow.xaml, no need to add them here
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshWindowList();
        }

        private void RefreshWindowList()
        {
            WindowListView.ItemsSource = null;
            WindowListView.ItemsSource = NoteManager.Instance.Notes;

            NoWindowsText.Visibility = NoteManager.Instance.Notes.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void CreateTopWindow_Click(object sender, RoutedEventArgs e)
        {
            NoteData noteData = NoteManager.Instance.CreateNote(WindowLevel.TopMost);
            OpenNote(noteData);
            RefreshWindowList();
        }

        private void CreateBottomWindow_Click(object sender, RoutedEventArgs e)
        {
            NoteData noteData = NoteManager.Instance.CreateNote(WindowLevel.BottomMost);
            OpenNote(noteData);
            RefreshWindowList();
        }

        private void CreateNormalWindow_Click(object sender, RoutedEventArgs e)
        {
            NoteData noteData = NoteManager.Instance.CreateNote(WindowLevel.Normal);
            OpenNote(noteData);
            RefreshWindowList();
        }

        private void ToggleWindow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is NoteData noteData)
            {
                if (noteData.IsOpen && noteData.Window != null)
                {
                    noteData.Window.Close();
                }
                else
                {
                    OpenNote(noteData);
                }
            }
        }

        private void DeleteNote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button || button.Tag is not NoteData noteData) return;
            MessageBoxResult? result = MessageBox.Show(
                $"Are you sure you want to delete '{noteData.Title}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;
            if (noteData.IsOpen && noteData.Window != null)
            {
                noteData.Window.Close();
            }
            NoteManager.Instance.DeleteNote(noteData);
            RefreshWindowList();
        }

        private void ChangeNoteLevel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem menuItem || menuItem.Tag is not NoteData noteData)
            {
                return;
            }

            if (!TryParseWindowLevel(menuItem.CommandParameter as string, out var targetLevel))
            {
                return;
            }

            if (noteData.Level == targetLevel)
            {
                return;
            }

            if (noteData.IsOpen && noteData.Window != null)
            {
                noteData.Window.ChangeWindowLevel(targetLevel);
                return;
            }

            noteData.Level = targetLevel;
            NoteManager.Instance.UpdateNote(noteData);
        }

        private static bool TryParseWindowLevel(string? levelText, out WindowLevel level)
        {
            level = WindowLevel.Normal;
            return Enum.TryParse(levelText, ignoreCase: true, out level);
        }

        private void OpenNote(NoteData noteData)
        {
            if (!noteData.IsOpen || noteData.Window == null)
            {
                FloatingWindow window = new FloatingWindow(noteData);
                window.Show();
            }
            else
            {
                noteData.Window.Activate();
            }
        }

        private FloatingWindow EnsureNoteWindow(NoteData noteData)
        {
            OpenNote(noteData);
            return noteData.Window!;
        }

        private static void RestoreDefaultSize(NoteData noteData)
        {
            if (noteData == null)
            {
                return;
            }

            noteData.Width = NoteManager.DefaultNoteWidth;
            noteData.Height = NoteManager.DefaultNoteHeight;
            NoteManager.Instance.UpdateNote(noteData);

            if (noteData.Window != null)
            {
                FloatingWindowQuickActions.RestoreDefaultSize(noteData.Window);
            }
        }

        private void QuickMoveNote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem menuItem || menuItem.Tag is not NoteData noteData)
            {
                return;
            }

            FloatingWindowQuickActions.ShowQuickMove(EnsureNoteWindow(noteData));
        }

        private void QuickMoveAndResizeNote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem menuItem || menuItem.Tag is not NoteData noteData)
            {
                return;
            }

            FloatingWindowQuickActions.ShowQuickMoveAndResize(EnsureNoteWindow(noteData));
        }

        private void QuickResizeNote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem menuItem || menuItem.Tag is not NoteData noteData)
            {
                return;
            }

            FloatingWindowQuickActions.ShowQuickResize(EnsureNoteWindow(noteData));
        }

        private void MoveNoteToMouseMonitor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem menuItem || menuItem.Tag is not NoteData noteData)
            {
                return;
            }

            FloatingWindowQuickActions.MoveToMouseMonitor(EnsureNoteWindow(noteData));
        }

        private void RestoreDefaultSizeForNote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem menuItem || menuItem.Tag is not NoteData noteData)
            {
                return;
            }

            RestoreDefaultSize(noteData);
        }

        private void RestoreSelectedDefaultSize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowListView.SelectedItem is not NoteData noteData)
            {
                MessageBox.Show(
                    "Select a note first.",
                    "Restore Default Size",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            RestoreDefaultSize(noteData);
        }

        private void HideToTray_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        private void RefreshList_Click(object sender, RoutedEventArgs e)
        {
            RefreshWindowList();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow settingsWindow = new SettingsWindow
            {
                Owner = this
            };
            settingsWindow.ShowDialog();
        }

        /// <summary>
        /// Runs one sync pass immediately and reports the result to the user.
        /// </summary>
        private async void SyncNow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button)
            {
                return;
            }

            if (global::YASN.App.App.SyncManager == null)
            {
                AppLogger.Warn("同步管理器未初始化。");
                return;
            }

            if (!global::YASN.App.App.SyncManager.IsConfigured)
            {
                AppLogger.Warn("请先在 Settings 中配置并应用 WebDAV。");
                return;
            }

            button.IsEnabled = false;
            SyncProgressToast progressToast = new SyncProgressToast();
            progressToast.Show();
            Progress<SyncProgressInfo> progress = new Progress<SyncProgressInfo>(progressToast.Report);

            try
            {
                SyncResult result = await global::YASN.App.App.SyncManager.RunSyncNowAsync(progress).ConfigureAwait(true);
                progressToast.Complete(result);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                progressToast.Fail(ex.Message);
                AppLogger.Warn($"立即同步失败: {ex.Message}");
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dataDirectory = AppPaths.DataDirectory;
                Process.Start(new ProcessStartInfo
                {
                    FileName = dataDirectory,
                    UseShellExecute = true
                });
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Warn($"Failed to open data folder '{AppPaths.DataDirectory}': {ex.Message}");
                MessageBox.Show(
                    $"Failed to open data folder: {ex.Message}",
                    "Open Folder Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                AppLogger.Warn($"Failed to open data folder '{AppPaths.DataDirectory}': {ex.Message}");
                MessageBox.Show(
                    $"Failed to open data folder: {ex.Message}",
                    "Open Folder Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }
    }

}
