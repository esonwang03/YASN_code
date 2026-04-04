using System.IO;
using System.Windows;
using System.Windows.Threading;
using YASN.Logging;
using YASN.Sync;
using MessageBox = ModernWpf.MessageBox;

namespace YASN
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        private NotifyIcon? _notifyIcon;
        public static Sync.SyncManager? SyncManager { get; private set; }
        private static System.Threading.Mutex? _singleInstanceMutex;
        private const string MutexName = "Global\\YASN_SingleInstance";

        protected override void OnStartup(StartupEventArgs e)
        {
            if (!EnsureSingleInstance())
            {
                MessageBox.Show("YASN 已在运行，无法启动多个实例。", "已在运行", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);

            // Initialize WebDAV sync manager
            SyncManager = new SyncManager();
            PreviewStyleManager.EnsureInitialized();
            AppLogger.Info("YASN Started");

            // Hide main window, only show tray icon
            MainWindow = new MainWindow();
            MainWindow.Hide();

            // Create tray icon
            _notifyIcon = new NotifyIcon();

            // Load icon from the same icon file used by the EXE
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "amy.ico");
                _notifyIcon.Icon = File.Exists(iconPath) ? new Icon(iconPath) :
                    // Fallback to embedded resource
                    YASN.Properties.Resources.bitbug_favicon;
            }
            catch (IOException ex)
            {
                AppLogger.Warn($"Failed to load tray icon from disk: {ex.Message}");
                _notifyIcon.Icon = YASN.Properties.Resources.bitbug_favicon;
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Warn($"Failed to load tray icon from disk: {ex.Message}");
                _notifyIcon.Icon = YASN.Properties.Resources.bitbug_favicon;
            }
            catch (ArgumentException ex)
            {
                AppLogger.Warn($"Failed to load tray icon from disk: {ex.Message}");
                _notifyIcon.Icon = YASN.Properties.Resources.bitbug_favicon;
            }

            _notifyIcon.Visible = true;
            _notifyIcon.Text = YASN.Properties.Resources.MainTitle;

            // Create context menu
            ContextMenuStrip contextMenu = new ContextMenuStrip();

            ToolStripMenuItem newWindowMenuItem = new ToolStripMenuItem("NewNote(&N)");
            newWindowMenuItem.DropDownItems.Add("Normal(&P)", null, CreateNormalWindowMenuItem_Click);
            newWindowMenuItem.DropDownItems.Add("TopMost(&T)", null, CreateTopWindowMenuItem_Click);
            newWindowMenuItem.DropDownItems.Add("BottomMost(&B)", null, CreateBottomWindowMenuItem_Click);

            ToolStripSeparator separatorMenuItem = new ToolStripSeparator();

            ToolStripMenuItem showMainMenuItem = new ToolStripMenuItem("Main(&S)");
            showMainMenuItem.Click += (s, args) => ShowMainWindow();

            ToolStripMenuItem exitMenuItem = new ToolStripMenuItem("Exit(&X)");
            exitMenuItem.Click += (s, args) => ExitApplication();

            contextMenu.Items.Add(newWindowMenuItem);
            contextMenu.Items.Add(separatorMenuItem);
            contextMenu.Items.Add(showMainMenuItem);
            contextMenu.Items.Add(exitMenuItem);

            _notifyIcon.ContextMenuStrip = contextMenu;

            // Double-click to show main window
            _notifyIcon.DoubleClick += (s, args) => ShowMainWindow();

            // Restore previously opened notes after a short delay to ensure everything is initialized
            Dispatcher.BeginInvoke(new Action(() =>
            {
                AppLogger.Debug("App startup: calling RestoreOpenNotes");
                NoteManager.Instance.RestoreOpenNotes();
            }), DispatcherPriority.ApplicationIdle);
        }

        protected override void OnExit(System.Windows.ExitEventArgs e)
        {
            // Ensure flag is set even if ExitApplication wasn't called
            FloatingWindow.SetApplicationShuttingDown();

            // Dispose sync manager
            SyncManager?.Dispose();

            _notifyIcon?.Dispose();
            base.OnExit(e);
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
                _singleInstanceMutex = null;
            }
            catch (ApplicationException ex)
            {
                AppLogger.Debug($"Failed to release single-instance mutex: {ex.Message}");
            }
            catch (ObjectDisposedException ex)
            {
                AppLogger.Debug($"Failed to release single-instance mutex: {ex.Message}");
            }
        }

        private bool EnsureSingleInstance()
        {
            try
            {
                bool createdNew;
                _singleInstanceMutex = new System.Threading.Mutex(true, MutexName, out createdNew);
                return createdNew;
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Warn($"Failed to create single-instance mutex: {ex.Message}");
                return true; // fail open to avoid blocking launch if mutex creation fails
            }
            catch (IOException ex)
            {
                AppLogger.Warn($"Failed to create single-instance mutex: {ex.Message}");
                return true; // fail open to avoid blocking launch if mutex creation fails
            }
            catch (WaitHandleCannotBeOpenedException ex)
            {
                AppLogger.Warn($"Failed to create single-instance mutex: {ex.Message}");
                return true; // fail open to avoid blocking launch if mutex creation fails
            }
        }

        private void CreateTopWindowMenuItem_Click(object sender, System.EventArgs e)
        {
            NoteData noteData = NoteManager.Instance.CreateNote(WindowLevel.TopMost);
            OpenNote(noteData);
        }

        private void CreateNormalWindowMenuItem_Click(object sender, System.EventArgs e)
        {
            NoteData noteData = NoteManager.Instance.CreateNote(WindowLevel.Normal);
            OpenNote(noteData);
        }

        private void CreateBottomWindowMenuItem_Click(object sender, System.EventArgs e)
        {
            NoteData noteData = NoteManager.Instance.CreateNote(WindowLevel.BottomMost);
            OpenNote(noteData);
        }

        private void OpenNote(NoteData noteData)
        {
            if (!noteData.IsOpen || noteData.Window == null)
            {
                FloatingWindow window = new (noteData);
                window.Show();
            }
            else
            {
                noteData.Window.Activate();
            }
        }

        private void ShowMainWindow()
        {
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        }

        private void ExitApplication()
        {
            // Notify all floating windows that application is shutting down
            FloatingWindow.SetApplicationShuttingDown();

            _notifyIcon?.Dispose();
            Current.Shutdown();
        }
    }
}

