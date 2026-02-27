using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Markdig;
using Microsoft.Web.WebView2.Core;
using YASN.Logging;
using YASN.Settings;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ContextMenu = System.Windows.Controls.ContextMenu;
using DataFormats = System.Windows.DataFormats;
using DrawingColor = System.Drawing.Color;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDeltaEventArgs = System.Windows.Controls.Primitives.DragDeltaEventArgs;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = ModernWpf.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Point = System.Windows.Point;

namespace YASN
{
    public enum WindowLevel
    {
        Normal,
        TopMost,
        BottomMost
    }

    public partial class FloatingWindow : Window
    {
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const string IconPin = "\uE718";
        private const string IconUnpin = "\uE77A";
        private const string IconArrowDown = "\uE74B";
        private const string IconSun = "\uE706";
        private const string IconMoon = "\uE708";
        private const string PreviewRightClickBridgeScript = """
                                                         (() => {
                                                           const thresholdMs = 900;
                                                           let lastRightClickAt = 0;
                                                           document.addEventListener('contextmenu', (event) => {
                                                             const now = Date.now();
                                                             const isDouble = now - lastRightClickAt <= thresholdMs;
                                                             lastRightClickAt = now;
                                                             if (window.chrome && window.chrome.webview) {
                                                               window.chrome.webview.postMessage(isDouble ? 'preview-right-double-click' : 'preview-right-click');
                                                             }
                                                             event.preventDefault();
                                                           }, true);
                                                         })();
                                                         """;

        
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetCursorPos(out NativePoint lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        private IntPtr _hwnd;
        private readonly DispatcherTimer _bottomMostTimer = new();
        private readonly DispatcherTimer _previewDebounceTimer = new();
        private readonly DispatcherTimer _chromeHoverTimer = new();
        private readonly MarkdownPipeline _markdownPipeline;
        private Storyboard _collapseEditBar;
        private Storyboard _expandEditBar;

        private readonly string _imageDirectory;
        private readonly string _attachmentDirectory;
        private readonly string _backgroundImageDirectory;
        private readonly string _htmlCachePath;

        private bool _previewReady;
        private bool _isPreviewInitInProgress;
        private bool _isChromeExpanded = true;
        private bool _autoCollapseChromeEnabled = NoteWindowUiSettings.DefaultAutoCollapseChrome;
        private DateTime _lastPreviewRightClickUtc = DateTime.MinValue;
        private DateTime _lastPreviewSurfaceRightClickUtc = DateTime.MinValue;

        private static FloatingWindow _currentBottomMostWindow;
        private static readonly object _bottomMostLock = new object();
        private static bool _isApplicationShuttingDown;

        public NoteData NoteData { get; private set; }

        public static void SetApplicationShuttingDown()
        {
            _isApplicationShuttingDown = true;
        }

        public FloatingWindow(NoteData noteData)
        {
            InitializeComponent();

            NoteData = noteData;
            NoteData.Window = this;
            NoteData.IsOpen = true;
            RefreshTaskbarVisibilityFromSettings();

            _imageDirectory = AppPaths.GetNoteAssetsDirectory(noteData.Id);
            _attachmentDirectory = AppPaths.GetNoteAttachmentsDirectory(noteData.Id);
            _backgroundImageDirectory = AppPaths.GetNoteBackgroundDirectory(noteData.Id);
            _htmlCachePath = AppPaths.GetNoteHtmlCachePath(noteData.Id);

            if (noteData.Left > 0 && noteData.Top > 0)
            {
                Left = noteData.Left;
                Top = noteData.Top;
            }

            if (noteData.Width > 0 && noteData.Height > 0)
            {
                Width = noteData.Width;
                Height = noteData.Height;
            }

            UpdateStatusText();
            UpdatePinButton();
            UpdateTitleBarButtons();
            ApplyTheme(noteData.IsDarkMode);
            UpdateThemeToggleButton();
            ApplyTitleBarColor(noteData.TitleBarColor);
            ApplyBackgroundImage(noteData.BackgroundImagePath);
            BackgroundImageBorder.Opacity = noteData.BackgroundImageOpacity;
            LoadContent(noteData.Content);

            _markdownPipeline = MarkdownPipelineConfig.Create();

            _bottomMostTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _bottomMostTimer.Tick += Timer_Tick;

            _previewDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _previewDebounceTimer.Tick += async (_, _) =>
            {
                _previewDebounceTimer.Stop();
                await RenderPreviewAsync();
            };

            _chromeHoverTimer.Interval = TimeSpan.FromMilliseconds(120);
            _chromeHoverTimer.Tick += (_, _) => UpdateChromeBarsByMouseState();
            _chromeHoverTimer.Start();
            RefreshChromeBehaviorFromSettings();

            _collapseEditBar = (Storyboard)FindResource("CollapseEditBar");
            _expandEditBar = (Storyboard)FindResource("ExpandEditBar");
            ApplyInitialDisplayMode();

            LocationChanged += (_, _) => SavePosition();
            SizeChanged += (_, _) => SaveSize();
            PreviewKeyDown += FloatingWindow_PreviewKeyDown;
        }

        private void ApplyInitialDisplayMode()
        {
            var hasContent = !string.IsNullOrWhiteSpace(GetContent());
            SetEditMode(!hasContent);
        }

        private void SetEditMode(bool isEditMode, bool focusEditor = false)
        {
            NoteData.IsEditMode = isEditMode;

            MarkdownToolbar.Visibility = isEditMode ? Visibility.Visible : Visibility.Collapsed;
            ContentTextBox.Visibility = isEditMode ? Visibility.Visible : Visibility.Collapsed;
            EditorPreviewSplitter.Visibility = isEditMode ? Visibility.Visible : Visibility.Collapsed;

            EditorColumn.Width = isEditMode
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
            SplitterColumn.Width = isEditMode
                ? new GridLength(5)
                : new GridLength(0);
            PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
            UpdatePreviewContainerAppearance(isEditMode);

            if (isEditMode)
            {
                SetChromeExpanded(true);
                UpdateChromeBarsByMouseState();
            }
            else
            {
                UpdateChromeBarsByMouseState();
            }

            if (isEditMode && focusEditor)
            {
                ContentTextBox.Focus();
                ContentTextBox.CaretIndex = ContentTextBox.Text?.Length ?? 0;
            }
        }

        private void UpdateChromeBarsByMouseState()
        {
            if (!_autoCollapseChromeEnabled)
            {
                SetChromeExpanded(true);
                return;
            }

            var keepExpandedForEditor = NoteData.IsEditMode &&
                                        (ContentTextBox.IsKeyboardFocusWithin || ContentTextBox.IsMouseOver);
            SetChromeExpanded(keepExpandedForEditor || IsMouseInsideWindow());
        }

        public void RefreshChromeBehaviorFromSettings()
        {
            var settingsStore = new SettingsStore();
            _autoCollapseChromeEnabled = NoteWindowUiSettings.IsAutoCollapseChromeEnabled(settingsStore);
            UpdateChromeBarsByMouseState();
        }

        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            const double fallbackMinWidth = 320;
            const double fallbackMinHeight = 220;
            var minWidth = MinWidth > 0 ? MinWidth : fallbackMinWidth;
            var minHeight = MinHeight > 0 ? MinHeight : fallbackMinHeight;

            Width = Math.Max(minWidth, Width + e.HorizontalChange);
            Height = Math.Max(minHeight, Height + e.VerticalChange);
        }

        private void UpdatePreviewContainerAppearance(bool isEditMode)
        {
            if (isEditMode)
            {
                PreviewContainer.Margin = new Thickness(0);
                PreviewContainer.CornerRadius = new CornerRadius(4);
                PreviewContainer.BorderThickness = new Thickness(1);
                PreviewContainer.BorderBrush = new SolidColorBrush(Color.FromArgb(0x25, 0x00, 0x00, 0x00));
                PreviewContainer.Background = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
            }
            else
            {
                // Slightly overdraw vertically in preview mode to avoid revealing underlying windows during chrome collapse.
                PreviewContainer.Margin = new Thickness(0, 0, 0, -40);
                PreviewContainer.CornerRadius = new CornerRadius(8);
                PreviewContainer.BorderThickness = new Thickness(0);
                PreviewContainer.BorderBrush = Brushes.Transparent;
                PreviewContainer.Background = Brushes.Transparent;
            }

            ApplyPreviewClip();
        }

        private void ApplyPreviewClip()
        {
            if (PreviewWebView.ActualWidth <= 0 || PreviewWebView.ActualHeight <= 0)
            {
                return;
            }

            var radius = Math.Max(0, PreviewContainer.CornerRadius.TopLeft);
            PreviewWebView.Clip = new RectangleGeometry(
                new Rect(0, 0, PreviewWebView.ActualWidth, PreviewWebView.ActualHeight),
                radius,
                radius);
        }

        private void PreviewContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyPreviewClip();
        }

        private void PreviewWebView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyPreviewClip();
        }

        private bool IsMouseInsideWindow()
        {
            if (!IsVisible || WindowState == WindowState.Minimized)
            {
                return false;
            }

            if (!GetCursorPos(out var nativePoint))
            {
                return false;
            }

            var dipPoint = PointFromScreen(new Point(nativePoint.X, nativePoint.Y));
            return dipPoint is { X: >= 0, Y: >= 0 } &&
                   dipPoint.X <= ActualWidth &&
                   dipPoint.Y <= ActualHeight;
        }

        private void SetChromeExpanded(bool expanded)
        {
            if (_isChromeExpanded == expanded)
            {
                return;
            }

            _isChromeExpanded = expanded;
            if (expanded)
            {
                _expandEditBar?.Begin();
                return;
            }

            _collapseEditBar?.Begin();
        }

        private void LoadContent(string content)
        {
            ContentTextBox.Text = content ?? string.Empty;
        }

        private string GetContent()
        {
            return ContentTextBox.Text ?? string.Empty;
        }

        private void ContentTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SaveContent();
            SchedulePreviewRender();
        }

        private void SchedulePreviewRender()
        {
            _previewDebounceTimer.Stop();
            _previewDebounceTimer.Start();
        }

        private async Task InitializePreviewAsync()
        {
            if (_previewReady || _isPreviewInitInProgress)
            {
                return;
            }

            _isPreviewInitInProgress = true;
            try
            {
                PreviewWebView.DefaultBackgroundColor = DrawingColor.Transparent;
                await PreviewWebView.EnsureCoreWebView2Async();
                PreviewWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                PreviewWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                PreviewWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                await PreviewWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(PreviewRightClickBridgeScript);
                PreviewWebView.CoreWebView2.ContextMenuRequested += PreviewCoreWebView2_ContextMenuRequested;
                PreviewWebView.CoreWebView2.NavigationStarting += PreviewCoreWebView2_NavigationStarting;
                PreviewWebView.CoreWebView2.WebMessageReceived += PreviewCoreWebView2_WebMessageReceived;
                PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "yasn.local",
                    AppPaths.DataDirectory,
                    CoreWebView2HostResourceAccessKind.Allow);

                _previewReady = true;
                ApplyPreviewClip();
                await RenderPreviewAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to initialize WebView2: {ex.Message}");
            }
            finally
            {
                _isPreviewInitInProgress = false;
            }
        }

        private async Task RenderPreviewAsync()
        {
            if (!_previewReady)
            {
                return;
            }

            try
            {
                var markdown = GetContent();
                var htmlBody = global::Markdig.Markdown.ToHtml(markdown ?? string.Empty, _markdownPipeline);
                var html = BuildHtmlPage(htmlBody, NoteData.IsDarkMode);

                var cacheDir = Path.GetDirectoryName(_htmlCachePath);
                if (!string.IsNullOrEmpty(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                }

                File.WriteAllText(_htmlCachePath, html);
                PreviewWebView.NavigateToString(html);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to render markdown preview: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private static string BuildHtmlPage(string htmlBody, bool darkMode)
        {
            var bg = darkMode ? "#15191D" : "#FAFBFC";
            var fg = darkMode ? "#E9EEF2" : "#253341";
            var muted = darkMode ? "#8FA1B5" : "#6A7D90";
            var border = darkMode ? "#2A343D" : "#D8DEE6";
            var codeBg = darkMode ? "#1F252B" : "#F1F4F7";
            var link = darkMode ? "#7BC6FF" : "#0067C0";

            return $@"<!doctype html>
<html>
<head>
<meta charset='utf-8' />
<meta http-equiv='Content-Security-Policy' content=""default-src 'self' https://yasn.local data:; img-src 'self' https://yasn.local data: file:; style-src 'unsafe-inline';"" />
<base href='https://yasn.local/' />
<style>
:root {{ color-scheme: {(darkMode ? "dark" : "light")}; }}
html, body {{ margin: 0; padding: 0; width: 100%; height: 100%; background: transparent; color: {fg}; font-family: Segoe UI, Microsoft YaHei UI, sans-serif; line-height: 1.6; }}
body {{ overflow: hidden; }}
#page {{ height: 100%; box-sizing: border-box; overflow: auto; padding: 16px 20px; background: {bg}; border-radius: 8px; }}
h1, h2, h3, h4, h5, h6 {{ margin: 0.8em 0 0.4em; }}
p {{ margin: 0.4em 0 0.8em; }}
ul, ol {{ margin: 0.4em 0 0.9em 1.4em; }}
hr {{ border: none; border-top: 1px solid {border}; margin: 1em 0; }}
blockquote {{ margin: 0.8em 0; padding: 0.4em 0.8em; border-left: 4px solid {border}; color: {muted}; background: {(darkMode ? "#1A2026" : "#F4F7FA")}; }}
code {{ background: {codeBg}; border: 1px solid {border}; border-radius: 4px; padding: 0.1em 0.35em; font-family: Consolas, monospace; }}
pre {{ background: {codeBg}; border: 1px solid {border}; border-radius: 6px; padding: 10px; overflow: auto; }}
pre code {{ border: none; padding: 0; background: transparent; }}
a {{ color: {link}; text-decoration: none; }}
a:hover {{ text-decoration: underline; }}
table {{ border-collapse: collapse; width: 100%; margin: 0.8em 0; }}
th, td {{ border: 1px solid {border}; padding: 6px 8px; text-align: left; }}
img {{ max-width: 100%; height: auto; border-radius: 4px; }}
</style>
</head>
<body>
<div id='page'>
{htmlBody}
</div>
</body>
</html>";
        }

        private void InsertImage_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image File|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
                Title = "Select Image to Insert"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                InsertImage(openFileDialog.FileName);
            }
        }

        private void InsertAttachment_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "All Files|*.*",
                Title = "Select Attachment to Insert"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                InsertAttachment(openFileDialog.FileName);
            }
        }

        private void ContentTextBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && File.Exists(files[0]))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }

            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void ContentTextBox_PreviewDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && File.Exists(files[0]))
                {
                    if (IsImageFile(files[0]))
                    {
                        InsertImage(files[0]);
                    }
                    else
                    {
                        InsertAttachment(files[0]);
                    }

                    e.Handled = true;
                }
            }
        }

        private static bool IsImageFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg" ||
                   extension == ".gif" || extension == ".bmp" || extension == ".webp";
        }

        private void InsertImage(string sourceFilePath)
        {
            string destPath = null;
            try
            {
                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(sourceFilePath)}";
                destPath = Path.Combine(_imageDirectory, fileName);
                File.Copy(sourceFilePath, destPath, true);

                var relativePath = $"note-assets/{NoteData.Id}/{fileName}";
                var altText = Path.GetFileNameWithoutExtension(sourceFilePath);
                var markdown = $"![{altText}]({relativePath}){Environment.NewLine}";

                InsertTextAtCaret(markdown);
            }
            catch (Exception ex)
            {
                if (destPath != null && File.Exists(destPath))
                {
                    try
                    {
                        File.Delete(destPath);
                    }
                    catch
                    {
                    }
                }

                MessageBox.Show($"Fail to insert image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InsertAttachment(string sourceFilePath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                MessageBox.Show("Attachment file does not exist.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var fileInfo = new FileInfo(sourceFilePath);
                var displayName = Path.GetFileName(sourceFilePath);
                string linkTarget;
                var settingsStore = new SettingsStore();
                var autoSyncEnabled = AttachmentSyncSettings.GetAutoSyncEnabled(settingsStore);
                var autoSyncMaxBytes = AttachmentSyncSettings.GetAutoSyncThresholdBytes(settingsStore);

                if (autoSyncEnabled && fileInfo.Length <= autoSyncMaxBytes)
                {
                    var fileName = $"{Guid.NewGuid()}{Path.GetExtension(sourceFilePath)}";
                    var destPath = Path.Combine(_attachmentDirectory, fileName);
                    File.Copy(sourceFilePath, destPath, true);
                    linkTarget = $"note-assets/attachments/{NoteData.Id}/{fileName}";
                }
                else
                {
                    linkTarget = new Uri(sourceFilePath, UriKind.Absolute).AbsoluteUri;
                }

                var markdown = $"[{displayName}]({linkTarget}){Environment.NewLine}";
                InsertTextAtCaret(markdown);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fail to insert attachment: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InsertTextAtCaret(string text)
        {
            var index = ContentTextBox.CaretIndex;
            var current = ContentTextBox.Text ?? string.Empty;
            ContentTextBox.Text = current.Insert(index, text);
            ContentTextBox.CaretIndex = index + text.Length;
            ContentTextBox.Focus();
        }

        private void SavePosition()
        {
            if (NoteData != null && WindowState == WindowState.Normal)
            {
                NoteData.Left = Left;
                NoteData.Top = Top;
                NoteManager.Instance.UpdateNote(NoteData);
            }
        }

        private void SaveSize()
        {
            if (NoteData != null && WindowState == WindowState.Normal)
            {
                NoteData.Width = Width;
                NoteData.Height = Height;
                NoteManager.Instance.UpdateNote(NoteData);
            }
        }

        private void SaveContent()
        {
            if (NoteData != null)
            {
                NoteData.Content = GetContent();
                NoteManager.Instance.UpdateNote(NoteData);
            }
        }

        public void RefreshTaskbarVisibilityFromSettings()
        {
            var settingsStore = new SettingsStore();
            var modeValue = settingsStore.GetValue(
                FloatingWindowTaskbarVisibility.SettingKey,
                shouldSync: true,
                defaultValue: FloatingWindowTaskbarVisibility.DefaultValue);

            ShowInTaskbar = FloatingWindowTaskbarVisibility.ShouldShowInTaskbar(NoteData.Level, modeValue);
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (NoteData.Level == WindowLevel.BottomMost && _hwnd != IntPtr.Zero && _currentBottomMostWindow == this)
            {
                SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            RefreshTaskbarVisibilityFromSettings();
            ApplyWindowLevel();
            await InitializePreviewAsync();
            SchedulePreviewRender();
            UpdateChromeBarsByMouseState();
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            UpdateChromeBarsByMouseState();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            UpdateChromeBarsByMouseState();
        }

        private void MainBorder_MouseEnter(object sender, MouseEventArgs mouseEventArgs)
        {
            UpdateChromeBarsByMouseState();
        }

        private void MainBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            UpdateChromeBarsByMouseState();
        }

        private void FloatingWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!NoteData.IsEditMode)
            {
                return;
            }

            if (e.Key != Key.Escape)
            {
                return;
            }

            SetEditMode(false);
            PreviewWebView.Focus();
            e.Handled = true;
        }

        private void PreviewSurface_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (NoteData.IsEditMode)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var threshold = TimeSpan.FromMilliseconds(900);
            var isDoubleRightClick = now - _lastPreviewSurfaceRightClickUtc <= threshold;
            _lastPreviewSurfaceRightClickUtc = now;
            if (!isDoubleRightClick)
            {
                return;
            }

            _lastPreviewSurfaceRightClickUtc = DateTime.MinValue;
            SetEditMode(true, focusEditor: true);
            e.Handled = true;
        }

        private void PreviewCoreWebView2_ContextMenuRequested(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            if (NoteData.IsEditMode)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var threshold = TimeSpan.FromMilliseconds(900);
            var isDoubleRightClick = now - _lastPreviewRightClickUtc <= threshold;
            _lastPreviewRightClickUtc = now;

            e.Handled = true;
            if (!isDoubleRightClick)
            {
                return;
            }

            _lastPreviewRightClickUtc = DateTime.MinValue;
            Dispatcher.BeginInvoke(new Action(() => SetEditMode(true, focusEditor: true)));
        }

        private void PreviewCoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (NoteData.IsEditMode)
            {
                return;
            }

            string? message;
            try
            {
                message = e.TryGetWebMessageAsString();
            }
            catch
            {
                return;
            }

            if (!string.Equals(message, "preview-right-double-click", StringComparison.Ordinal))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => SetEditMode(true, focusEditor: true)));
        }

        private void PreviewCoreWebView2_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!e.IsUserInitiated)
            {
                return;
            }

            if (!TryResolveOpenTarget(e.Uri, out var openTarget))
            {
                return;
            }

            e.Cancel = true;
            TryOpenWithSystemViewer(openTarget);
        }

        private static bool TryResolveOpenTarget(string rawUri, out string openTarget)
        {
            openTarget = string.Empty;
            if (string.IsNullOrWhiteSpace(rawUri))
            {
                return false;
            }

            if (Uri.TryCreate(rawUri, UriKind.Absolute, out var uri))
            {
                if (uri.IsFile)
                {
                    var localPath = uri.LocalPath;
                    if (File.Exists(localPath))
                    {
                        openTarget = localPath;
                        return true;
                    }
                }
                else if (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(uri.Host, "yasn.local", StringComparison.OrdinalIgnoreCase))
                {
                    var localRelative = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))
                        .Replace('/', Path.DirectorySeparatorChar);
                    var localPath = Path.Combine(AppPaths.DataDirectory, localRelative);
                    if (File.Exists(localPath))
                    {
                        openTarget = localPath;
                        return true;
                    }
                }
                else if (string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                {
                    openTarget = rawUri;
                    return true;
                }
            }

            if (File.Exists(rawUri))
            {
                openTarget = rawUri;
                return true;
            }

            return false;
        }

        private static void TryOpenWithSystemViewer(string target)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fail to open attachment: {ex.Message}", "Open Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                try
                {
                    DragMove();
                }
                catch
                {
                }

                if (NoteData.Level == WindowLevel.BottomMost)
                {
                    ApplyWindowLevel();
                }
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (NoteData.Level != WindowLevel.Normal)
            {
                return;
            }

            WindowState = WindowState.Minimized;
        }

        private void MoreOptions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            var contextMenu = new ContextMenu();

            var renameTitleItem = new MenuItem { Header = "Edit Title" };
            renameTitleItem.Click += (_, _) => PromptRenameTitle();

            var showMainWindowItem = new MenuItem { Header = "Open MainWindow" };
            showMainWindowItem.Click += (_, _) =>
            {
                if (Application.Current is App app && app.MainWindow != null)
                {
                    app.MainWindow.Show();
                    app.MainWindow.WindowState = WindowState.Normal;
                    app.MainWindow.Activate();
                }
            };

            var createNoteItem = new MenuItem { Header = "Create New Note" };
            createNoteItem.Click += (_, _) =>
            {
                var newNote = NoteManager.Instance.CreateNote();
                new FloatingWindow(newNote).Show();
            };

            var createTopMostNoteItem = new MenuItem { Header = "Create TopMost Note" };
            createTopMostNoteItem.Click += (_, _) =>
            {
                var newNote = NoteManager.Instance.CreateNote(WindowLevel.TopMost);
                new FloatingWindow(newNote).Show();
            };

            var deleteNoteItem = new MenuItem { Header = "Delete Note" };
            deleteNoteItem.Click += (_, _) =>
            {
                var result = MessageBox.Show(
                    "Are you sure you want to delete this note?",
                    "Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    NoteManager.Instance.DeleteNote(NoteData);
                    Close();
                }
            };

            var clearContentItem = new MenuItem { Header = "Clear Content" };
            clearContentItem.Click += (_, _) =>
            {
                var result = MessageBox.Show(
                    "Are you sure you want to clear the current content?",
                    "Clear Content",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ContentTextBox.Text = string.Empty;
                }
            };

            var changeTitleBarColorItem = new MenuItem { Header = "Change Title Bar Color" };
            changeTitleBarColorItem.Click += (_, _) => ShowColorPicker();

            var backgroundImageItem = new MenuItem { Header = "Background Image" };
            backgroundImageItem.Click += (_, _) => ShowBackgroundImageMenu(button);

            var aboutItem = new MenuItem { Header = "About" };
            aboutItem.Click += (_, _) =>
            {
                MessageBox.Show(
                    "YASN - Yet Another Sticky Notes\nv1.0\n\nMarkdown mode enabled.",
                    "About YASN",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            };

            contextMenu.Items.Add(renameTitleItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(showMainWindowItem);
            contextMenu.Items.Add(createNoteItem);
            contextMenu.Items.Add(createTopMostNoteItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(deleteNoteItem);
            contextMenu.Items.Add(clearContentItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(changeTitleBarColorItem);
            contextMenu.Items.Add(backgroundImageItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(aboutItem);

            contextMenu.PlacementTarget = button;
            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            contextMenu.IsOpen = true;
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            SetWindowLevel(NoteData.Level == WindowLevel.TopMost ? WindowLevel.Normal : WindowLevel.TopMost);
        }

        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            ToggleTheme();
        }

        private void SendToBottom_Click(object sender, RoutedEventArgs e)
        {
            SetWindowLevel(NoteData.Level == WindowLevel.BottomMost ? WindowLevel.Normal : WindowLevel.BottomMost);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _bottomMostTimer?.Stop();
            Close();
        }

        private void SetWindowLevel(WindowLevel level)
        {
            NoteData.Level = level;
            RefreshTaskbarVisibilityFromSettings();

            UpdateStatusText();
            UpdatePinButton();
            UpdateTitleBarButtons();
            NoteManager.Instance.UpdateNote(NoteData);

            if (_hwnd != IntPtr.Zero)
            {
                ApplyWindowLevel();
            }
        }

        private void UpdatePinButton()
        {
            switch (NoteData.Level)
            {
                case WindowLevel.TopMost:
                    PinTopButton.Content = IconUnpin;
                    PinTopButton.ToolTip = "Unpin from Top";
                    PinBottomButton.Content = IconArrowDown;
                    PinBottomButton.ToolTip = "Pin to Bottom";
                    break;
                case WindowLevel.Normal:
                    PinTopButton.Content = IconPin;
                    PinTopButton.ToolTip = "Pin to Top";
                    PinBottomButton.Content = IconArrowDown;
                    PinBottomButton.ToolTip = "Pin to Bottom";
                    break;
                case WindowLevel.BottomMost:
                    PinTopButton.Content = IconPin;
                    PinTopButton.ToolTip = "Pin to Top";
                    PinBottomButton.Content = IconUnpin;
                    PinBottomButton.ToolTip = "Unpin from Bottom";
                    break;
                default: // try to make linter happy
                    break;
            }
        }

        private void UpdateTitleBarButtons()
        {
            if (MinimizeButton == null)
            {
                return;
            }

            MinimizeButton.Visibility = NoteData.Level == WindowLevel.Normal
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void PromptRenameTitle()
        {
            var input = ShowTitleInputDialog(NoteData.Title);
            if (input == null)
            {
                return;
            }

            var newTitle = input.Trim();
            if (string.IsNullOrEmpty(newTitle))
            {
                MessageBox.Show(
                    "Title can't be empty。",
                    "Edit Title",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (string.Equals(NoteData.Title, newTitle, StringComparison.Ordinal))
            {
                return;
            }

            var hasDuplicate = NoteManager.Instance.Notes.Any(n =>
                n.Id != NoteData.Id &&
                string.Equals((n.Title ?? string.Empty).Trim(), newTitle, StringComparison.OrdinalIgnoreCase));
            if (hasDuplicate)
            {
                MessageBox.Show(
                    "Already had Note with same title",
                    "Edit Title",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            NoteData.Title = newTitle;
            NoteManager.Instance.UpdateNote(NoteData);
            UpdateStatusText();
        }

        private string? ShowTitleInputDialog(string initialValue)
        {
            var dialog = new Window
            {
                Title = "Edit Title",
                Width = 360,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var grid = new Grid { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var prompt = new TextBlock
            {
                Text = "New Title",
                Margin = new Thickness(0, 0, 0, 8)
            };

            var inputBox = new System.Windows.Controls.TextBox
            {
                Text = initialValue ?? string.Empty,
                Margin = new Thickness(0, 0, 0, 12)
            };

            var buttonPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okButton = new Button
            {
                Content = "Confirm",
                Width = 72,
                IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 72,
                IsCancel = true
            };

            string? result = null;
            okButton.Click += (_, _) =>
            {
                result = inputBox.Text;
                dialog.DialogResult = true;
                dialog.Close();
            };
            cancelButton.Click += (_, _) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(prompt, 0);
            Grid.SetRow(inputBox, 1);
            Grid.SetRow(buttonPanel, 2);
            grid.Children.Add(prompt);
            grid.Children.Add(inputBox);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;
            dialog.Loaded += (_, _) =>
            {
                inputBox.Focus();
                inputBox.SelectAll();
            };

            return dialog.ShowDialog() == true ? result : null;
        }

        private void UpdateStatusText()
        {
            var levelPrefix = NoteData.Level switch
            {
                WindowLevel.TopMost => "[T] ",
                WindowLevel.BottomMost => "[B] ",
                _ => string.Empty
            };

            StatusText.Text = $"{levelPrefix}{NoteData.Title}";
            Title = NoteData.Title;
        }

        private void ApplyWindowLevel()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            _bottomMostTimer?.Stop();

            switch (NoteData.Level)
            {
                case WindowLevel.TopMost:
                    Topmost = true;
                    break;

                case WindowLevel.BottomMost:
                    Topmost = false;

                    lock (_bottomMostLock)
                    {
                        if (_currentBottomMostWindow != null && _currentBottomMostWindow != this)
                        {
                            var previousWindow = _currentBottomMostWindow;
                            _currentBottomMostWindow = null;
                            previousWindow.Dispatcher.Invoke(() => previousWindow.SetWindowLevel(WindowLevel.Normal));
                        }

                        _currentBottomMostWindow = this;
                    }

                    SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

                    _bottomMostTimer?.Start();
                    break;

                default:
                    Topmost = false;

                    lock (_bottomMostLock)
                    {
                        if (_currentBottomMostWindow == this)
                        {
                            _currentBottomMostWindow = null;
                        }
                    }

                    SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    break;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            if (NoteData.Level == WindowLevel.BottomMost && _hwnd != IntPtr.Zero && _currentBottomMostWindow == this)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _bottomMostTimer?.Stop();
            _previewDebounceTimer?.Stop();
            _chromeHoverTimer?.Stop();

            lock (_bottomMostLock)
            {
                if (_currentBottomMostWindow == this)
                {
                    _currentBottomMostWindow = null;
                }
            }

            if (!_isApplicationShuttingDown)
            {
                NoteData.IsOpen = false;
            }

            NoteData.Window = null;
            NoteManager.Instance.UpdateNote(NoteData);
            base.OnClosed(e);
        }

        private void ToggleTheme()
        {
            NoteData.IsDarkMode = !NoteData.IsDarkMode;
            ApplyTheme(NoteData.IsDarkMode);
            UpdateThemeToggleButton();
            NoteManager.Instance.UpdateNote(NoteData);
            SchedulePreviewRender();
        }

        private void UpdateThemeToggleButton()
        {
            if (ThemeToggleButton == null)
            {
                return;
            }

            ThemeToggleButton.Content = NoteData.IsDarkMode ? IconSun : IconMoon;
            ThemeToggleButton.ToolTip = NoteData.IsDarkMode
                ? "Switch to Day mode"
                : "Switch to Night mode";
        }

        private void ApplyTheme(bool isDarkMode)
        {
            if (isDarkMode)
            {
                MainBorder.Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x1E, 0x1E, 0x1E));
                MainBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80));
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xEC, 0xF0, 0xF1));
                MarkdownToolbar.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00));
                ContentTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0xEC, 0xF0, 0xF1));
                ContentTextBox.Background = new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0x00, 0x00));
            }
            else
            {
                MainBorder.Background = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xF0));
                MainBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xC0, 0xC0, 0xC0));
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
                MarkdownToolbar.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xE0, 0xE0, 0xE0));
                ContentTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
                ContentTextBox.Background = Brushes.Transparent;
            }
        }

        private void ApplyTitleBarColor(string colorHex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorHex);
                TitleBar.Background = new SolidColorBrush(color);
            }
            catch
            {
                TitleBar.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0xD4, 0xC5, 0xE0));
            }
        }
        private void ShowColorPicker()
        {
            var colorPickerWindow = new Window
            {
                Title = "Title Bar Color",
                Width = 390,
                Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var stackPanel = new StackPanel { Margin = new Thickness(18) };
            stackPanel.Children.Add(new TextBlock
            {
                Text = "Choose a preset color",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var colorsGrid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            colorsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            colorsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            colorsGrid.ColumnDefinitions.Add(new ColumnDefinition());

            var presetColors = new[]
            {
                ("#E6D4C5E0", "Lavender"),
                ("#E6FFB6C1", "Rose"),
                ("#E6B0E0E6", "Sky"),
                ("#E6C8E6C9", "Mint"),
                ("#E6FFE4B5", "Peach"),
                ("#E6F5DEB3", "Sand"),
                ("#E6E6E6FA", "Soft Indigo"),
                ("#E6FFE4E1", "Mist")
            };

            var row = 0;
            var col = 0;
            foreach (var (colorHex, colorName) in presetColors)
            {
                if (col == 0)
                {
                    colorsGrid.RowDefinitions.Add(new RowDefinition());
                }

                var button = new Button
                {
                    Width = 108,
                    Height = 56,
                    Margin = new Thickness(4),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
                    Content = colorName,
                    Tag = colorHex
                };

                button.Click += (s, _) =>
                {
                    var selectedColor = (s as Button)?.Tag as string;
                    if (!string.IsNullOrEmpty(selectedColor))
                    {
                        NoteData.TitleBarColor = selectedColor;
                        ApplyTitleBarColor(selectedColor);
                        NoteManager.Instance.UpdateNote(NoteData);
                        colorPickerWindow.Close();
                    }
                };

                Grid.SetRow(button, row);
                Grid.SetColumn(button, col);
                colorsGrid.Children.Add(button);

                col++;
                if (col >= 3)
                {
                    col = 0;
                    row++;
                }
            }

            stackPanel.Children.Add(colorsGrid);
            colorPickerWindow.Content = stackPanel;
            colorPickerWindow.ShowDialog();
        }

        private void ShowBackgroundImageMenu(Button anchorButton)
        {
            var contextMenu = new ContextMenu();

            var selectImageItem = new MenuItem { Header = "Select Background Image" };
            selectImageItem.Click += (_, _) =>
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "Image File|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
                    Title = "Select Background Image"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    SetBackgroundImage(openFileDialog.FileName);
                }
            };

            var clearBackgroundItem = new MenuItem { Header = "Clear Background Image" };
            clearBackgroundItem.Click += (_, _) => ClearBackgroundImage();

            var adjustOpacityItem = new MenuItem { Header = "Adjust Background Opacity" };
            adjustOpacityItem.Click += (_, _) => ShowOpacityAdjuster();

            contextMenu.Items.Add(selectImageItem);
            contextMenu.Items.Add(clearBackgroundItem);
            contextMenu.Items.Add(adjustOpacityItem);

            contextMenu.PlacementTarget = anchorButton;
            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            contextMenu.IsOpen = true;
        }

        private void SetBackgroundImage(string sourceFilePath)
        {
            string destPath = null;
            try
            {
                var fileName = $"background{Path.GetExtension(sourceFilePath)}";
                destPath = Path.Combine(_backgroundImageDirectory, fileName);

                if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                }

                File.Copy(sourceFilePath, destPath, true);

                NoteData.BackgroundImagePath = destPath;
                ApplyBackgroundImage(destPath);
                NoteManager.Instance.UpdateNote(NoteData);
            }
            catch (Exception ex)
            {
                if (destPath != null && File.Exists(destPath))
                {
                    try
                    {
                        File.Delete(destPath);
                    }
                    catch
                    {
                    }
                }

                MessageBox.Show($"Fail to set background image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyBackgroundImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                BackgroundImageBrush.ImageSource = null;
                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                BackgroundImageBrush.ImageSource = bitmap;
            }
            catch
            {
                BackgroundImageBrush.ImageSource = null;
            }
        }

        private void ClearBackgroundImage()
        {
            NoteData.BackgroundImagePath = null;
            BackgroundImageBrush.ImageSource = null;
            NoteManager.Instance.UpdateNote(NoteData);

            if (!Directory.Exists(_backgroundImageDirectory))
            {
                return;
            }

            try
            {
                foreach (var file in Directory.GetFiles(_backgroundImageDirectory))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private void ShowOpacityAdjuster()
        {
            var opacityWindow = new Window
            {
                Title = "Background Opacity",
                Width = 300,
                Height = 190,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var stackPanel = new StackPanel { Margin = new Thickness(20) };
            stackPanel.Children.Add(new TextBlock
            {
                Text = "Background opacity",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var slider = new Slider
            {
                Minimum = 0.05,
                Maximum = 1.0,
                Value = BackgroundImageBorder.Opacity,
                TickFrequency = 0.05,
                IsSnapToTickEnabled = true,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var valueText = new TextBlock
            {
                Text = $"Current: {BackgroundImageBorder.Opacity:F2}",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };

            slider.ValueChanged += (_, _) =>
            {
                BackgroundImageBorder.Opacity = slider.Value;
                valueText.Text = $"Current: {slider.Value:F2}";

                NoteData.BackgroundImageOpacity = slider.Value;
                NoteManager.Instance.UpdateNote(NoteData);
            };

            stackPanel.Children.Add(slider);
            stackPanel.Children.Add(valueText);

            opacityWindow.Content = stackPanel;
            opacityWindow.ShowDialog();
        }

        private async void RefreshPreview_Click(object sender, RoutedEventArgs e)
        {
            await RenderPreviewAsync();
        }
    }
}


