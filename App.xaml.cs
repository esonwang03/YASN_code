namespace YASN
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private System.Windows.Forms.NotifyIcon? _notifyIcon;

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);

            // Hide main window, only show tray icon
            MainWindow = new MainWindow();
            MainWindow.Hide();

            // Create tray icon
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Icon = YASN.Properties.Resources.bitbug_favicon;
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "YASN - Window Manager";

            // Create context menu
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            
            var newWindowMenuItem = new System.Windows.Forms.ToolStripMenuItem("NewNote(&N)");
            newWindowMenuItem.DropDownItems.Add("Normal(&P)", null, CreateNormalWindowMenuItem_Click);
            newWindowMenuItem.DropDownItems.Add("TopMost(&T)", null, CreateTopWindowMenuItem_Click);
            newWindowMenuItem.DropDownItems.Add("BottomMost(&B)", null, CreateBottomWindowMenuItem_Click);
            
            var separatorMenuItem = new System.Windows.Forms.ToolStripSeparator();
            
            var showMainMenuItem = new System.Windows.Forms.ToolStripMenuItem("Main(&S)");
            showMainMenuItem.Click += (s, args) => ShowMainWindow();
            
            var exitMenuItem = new System.Windows.Forms.ToolStripMenuItem("Exit(&X)");
            exitMenuItem.Click += (s, args) => ExitApplication();

            contextMenu.Items.Add(newWindowMenuItem);
            contextMenu.Items.Add(separatorMenuItem);
            contextMenu.Items.Add(showMainMenuItem);
            contextMenu.Items.Add(exitMenuItem);

            _notifyIcon.ContextMenuStrip = contextMenu;

            // Double-click to show main window
            _notifyIcon.DoubleClick += (s, args) => ShowMainWindow();
        }

        private void CreateTopWindowMenuItem_Click(object sender, System.EventArgs e)
        {
            var noteData = NoteManager.Instance.CreateNote(WindowLevel.TopMost);
            OpenNote(noteData);
        }

        private void CreateNormalWindowMenuItem_Click(object sender, System.EventArgs e)
        {
            var noteData = NoteManager.Instance.CreateNote(WindowLevel.Normal);
            OpenNote(noteData);
        }

        private void CreateBottomWindowMenuItem_Click(object sender, System.EventArgs e)
        {
            var noteData = NoteManager.Instance.CreateNote(WindowLevel.BottomMost);
            OpenNote(noteData);
        }

        private void OpenNote(NoteData noteData)
        {
            if (!noteData.IsOpen || noteData.Window == null)
            {
                var window = new FloatingWindow(noteData);
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
            MainWindow.WindowState = System.Windows.WindowState.Normal;
            MainWindow.Activate();
        }

        private void ExitApplication()
        {
            _notifyIcon?.Dispose();
            Current.Shutdown();
        }

        protected override void OnExit(System.Windows.ExitEventArgs e)
        {
            _notifyIcon?.Dispose();
            base.OnExit(e);
        }
    }
}
