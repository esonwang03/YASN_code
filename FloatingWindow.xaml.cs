using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
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
using YASN.WindowLayout;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ContextMenu = System.Windows.Controls.ContextMenu;
using DataFormats = System.Windows.DataFormats;
using DragDeltaEventArgs = System.Windows.Controls.Primitives.DragDeltaEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using DrawingColor = System.Drawing.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Drawing.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = ModernWpf.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Point = System.Windows.Point;
using WinFormsClipboard = System.Windows.Forms.Clipboard;

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
        private const string IconModeTextOnly = "\uE8A5";
        private const string IconModeTextAndPreview = "\uE8A9";
        private const string IconModePreviewOnly = "\uE890";
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
        private FileSystemWatcher? _activeStyleWatcher;
        private FileSystemWatcher? _debugSourceStyleWatcher;

        private bool _previewReady;
        private bool _isPreviewInitInProgress;
        private bool _isPreviewDocumentReady;
        private bool _hasPendingPreviewScrollRestore;
        private bool _isChromeExpanded = true;
        private bool _autoCollapseChromeEnabled = NoteWindowUiSettings.DefaultValue;
        private string _previewStyleRelativePath = PreviewStyleManager.DefaultStyleRelativePath;
        private EditorDisplayMode _editorDisplayMode = EditorDisplayMode.PreviewOnly;
        private double _singleModeWidthBeforeSplit = double.NaN;
        private DateTime _lastPreviewRightClickUtc = DateTime.MinValue;
        private DateTime _lastPreviewSurfaceRightClickUtc = DateTime.MinValue;
        private double _pendingPreviewScrollRatio = -1;

        private static FloatingWindow _currentBottomMostWindow;
        private static readonly Lock _bottomMostLock = new();
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

            if (noteData is { Left: > 0, Top: > 0 })
            {
                Left = noteData.Left;
                Top = noteData.Top;
            }

            if (noteData is { Width: > 0, Height: > 0 })
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
            RefreshPreviewStyleFromSettings(forceRender: false);
            ConfigurePreviewStyleWatchers();

            _markdownPipeline = MarkdownPipelineConfig.Create();

            _bottomMostTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _bottomMostTimer.Tick += Timer_Tick;

            _previewDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _previewDebounceTimer.Tick += async (_, _) =>
            {
                _previewDebounceTimer.Stop();
                await RenderPreviewAsync();
            };

            RefreshChromeBehaviorFromSettings();
            _chromeHoverTimer.Interval = TimeSpan.FromMilliseconds(120);
            _chromeHoverTimer.Tick += (_, _) => UpdateChromeBarsByMouseState();
            _chromeHoverTimer.Start();

            _collapseEditBar = (Storyboard)FindResource("CollapseEditBar");
            _expandEditBar = (Storyboard)FindResource("ExpandEditBar");
            ApplyInitialDisplayMode();

            LocationChanged += (_, _) => SavePosition();
            SizeChanged += (_, _) => SaveSize();
            PreviewKeyDown += FloatingWindow_PreviewKeyDown;
        }

        private void ApplyInitialDisplayMode()
        {
            if (NoteData.LastEditorDisplayMode.HasValue)
            {
                SetDisplayMode(NoteData.LastEditorDisplayMode.Value, adjustWindowWidth: false);
                return;
            }

            bool hasContent = !string.IsNullOrWhiteSpace(GetContent());
            if (!hasContent)
            {
                SetDisplayMode(GetConfiguredEditorEnterMode(), adjustWindowWidth: false);
                return;
            }

            SetDisplayMode(EditorDisplayMode.PreviewOnly, adjustWindowWidth: false);
        }

        private void SetEditMode(bool isEditMode, bool focusEditor = false)
        {
            if (isEditMode)
            {
                EnterConfiguredEditMode(focusEditor);
                return;
            }

            SetDisplayMode(EditorDisplayMode.PreviewOnly);
        }

        private void EnterConfiguredEditMode(bool focusEditor = false)
        {
            EditorDisplayMode targetMode = GetConfiguredEditorEnterMode();
            SetDisplayMode(targetMode, focusEditor: focusEditor && targetMode != EditorDisplayMode.PreviewOnly);
        }

        private EditorDisplayMode GetConfiguredEditorEnterMode()
        {
            SettingsStore settingsStore = new SettingsStore();
            return EditorDisplayModeSettings.GetEnterMode(settingsStore);
        }

        private void SetDisplayMode(
            EditorDisplayMode mode,
            bool focusEditor = false,
            bool adjustWindowWidth = true)
        {
            EditorDisplayMode previousMode = _editorDisplayMode;
            _editorDisplayMode = mode;
            bool shouldPersistDisplayMode = NoteData.LastEditorDisplayMode != mode;
            if (shouldPersistDisplayMode)
            {
                NoteData.LastEditorDisplayMode = mode;
            }

            bool showEditor = mode is EditorDisplayMode.TextOnly or EditorDisplayMode.TextAndPreview;
            bool showPreview = mode is EditorDisplayMode.PreviewOnly or EditorDisplayMode.TextAndPreview;
            bool showSplitter = mode == EditorDisplayMode.TextAndPreview;

            NoteData.IsEditMode = showEditor;
            MarkdownToolbar.Visibility = showEditor ? Visibility.Visible : Visibility.Collapsed;
            ContentTextBox.Visibility = showEditor ? Visibility.Visible : Visibility.Collapsed;
            PreviewContainer.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
            EditorPreviewSplitter.Visibility = showSplitter ? Visibility.Visible : Visibility.Collapsed;

            EditorColumn.Width = showEditor
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
            SplitterColumn.Width = showSplitter
                ? new GridLength(5)
                : new GridLength(0);
            PreviewColumn.Width = showPreview
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
            UpdatePreviewContainerAppearance(mode);
            UpdateEditorModeButton();

            switch (adjustWindowWidth)
            {
                case true when
                    mode == EditorDisplayMode.TextAndPreview &&
                    previousMode != EditorDisplayMode.TextAndPreview:
                    ExpandWindowWidthForSplitMode();
                    break;
                case true when
                    previousMode == EditorDisplayMode.TextAndPreview &&
                    mode != EditorDisplayMode.TextAndPreview:
                    RestoreWindowWidthAfterSplitMode();
                    break;
            }

            if (showEditor)
            {
                SetChromeExpanded(true);
            }

            if (shouldPersistDisplayMode)
            {
                NoteManager.Instance.UpdateNote(NoteData);
            }

            UpdateChromeBarsByMouseState();

            if (!showEditor || !focusEditor)
            {
                return;
            }

            ContentTextBox.Focus();
            ContentTextBox.CaretIndex = ContentTextBox.Text?.Length ?? 0;
        }

        private void UpdateChromeBarsByMouseState()
        {
            if (!_autoCollapseChromeEnabled)
            {
                SetChromeExpanded(true);
                return;
            }

            bool keepExpandedForEditor = NoteData.IsEditMode &&
                                        (ContentTextBox.IsKeyboardFocusWithin || ContentTextBox.IsMouseOver);
            SetChromeExpanded(keepExpandedForEditor || IsMouseInsideWindow());
        }

        public void RefreshChromeBehaviorFromSettings()
        {
            SettingsStore settingsStore = new SettingsStore();
            _autoCollapseChromeEnabled = NoteWindowUiSettings.IsAutoCollapseChromeEnabled(settingsStore);
            AppLogger.Debug($"Load chrome behavior settings: {_autoCollapseChromeEnabled}");
            UpdateChromeBarsByMouseState();
        }

        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            const double fallbackMinWidth = 320;
            const double fallbackMinHeight = 220;
            double minWidth = MinWidth > 0 ? MinWidth : fallbackMinWidth;
            double minHeight = MinHeight > 0 ? MinHeight : fallbackMinHeight;

            Width = Math.Max(minWidth, Width + e.HorizontalChange);
            Height = Math.Max(minHeight, Height + e.VerticalChange);
        }

        private void UpdatePreviewContainerAppearance(EditorDisplayMode mode)
        {
            if (mode == EditorDisplayMode.TextAndPreview)
            {
                PreviewContainer.Margin = new Thickness(0);
                PreviewContainer.CornerRadius = new CornerRadius(4);
                PreviewContainer.BorderThickness = new Thickness(1);
                PreviewContainer.BorderBrush = new SolidColorBrush(Color.FromArgb(0x25, 0x00, 0x00, 0x00));
                PreviewContainer.Background = Brushes.Transparent;
            }
            else if (mode == EditorDisplayMode.PreviewOnly)
            {
                PreviewContainer.Margin = new Thickness(0);
                PreviewContainer.CornerRadius = new CornerRadius(8);
                PreviewContainer.BorderThickness = new Thickness(0);
                PreviewContainer.BorderBrush = Brushes.Transparent;
                PreviewContainer.Background = Brushes.Transparent;
            }
            else
            {
                PreviewContainer.Margin = new Thickness(0);
                PreviewContainer.CornerRadius = new CornerRadius(4);
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

            double radius = Math.Max(0, PreviewContainer.CornerRadius.TopLeft);
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

            Point dipPoint = PointFromScreen(new Point(nativePoint.X, nativePoint.Y));
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

        private void ContentTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool isPasteShortcut = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.V;
            if (!isPasteShortcut)
            {
                return;
            }

            if (!TryPasteClipboardAssets())
            {
                return;
            }

            e.Handled = true;
        }

        private bool TryPasteClipboardAssets()
        {
            try
            {
                if (WinFormsClipboard.ContainsImage())
                {
                    using Image? clipboardImage = WinFormsClipboard.GetImage();
                    if (clipboardImage != null)
                    {
                        InsertClipboardImage(clipboardImage);
                        return true;
                    }
                }

                if (System.Windows.Clipboard.ContainsImage())
                {
                    var clipboardImage = System.Windows.Clipboard.GetImage();
                    if (clipboardImage != null)
                    {
                        InsertClipboardImage(clipboardImage);
                        return true;
                    }
                }

                if (!System.Windows.Clipboard.ContainsFileDropList())
                {
                    return false;
                }

                StringCollection files = System.Windows.Clipboard.GetFileDropList();
                if (files.Count == 0)
                {
                    return false;
                }

                bool insertedAny = false;
                foreach (string path in files.Cast<string>())
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    if (IsImageFile(path))
                    {
                        InsertImage(path);
                    }
                    else
                    {
                        InsertAttachment(path);
                    }

                    insertedAny = true;
                }

                return insertedAny;
            }
            catch (ExternalException ex)
            {
                AppLogger.Warn($"Failed to paste clipboard content: {ex.Message}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Warn($"Failed to paste clipboard content: {ex.Message}");
                return false;
            }
        }

        private void InsertClipboardImage(System.Drawing.Image image)
        {
            string? destPath = null;
            try
            {
                string fileName = $"{Guid.NewGuid()}.png";
                destPath = Path.GetFullPath(Path.Combine(_imageDirectory, fileName));

                string? targetDirectory = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                using Bitmap bitmap = new System.Drawing.Bitmap(image);
                using (var stream = File.Create(destPath))
                {
                    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                }

                string markdown = $"![clipboard-image](note-assets/{NoteData.Id}/{fileName}){Environment.NewLine}";
                InsertTextAtCaret(markdown);
            }
            catch (ExternalException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (NotSupportedException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (ArgumentException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InsertClipboardImage(BitmapSource imageSource)
        {
            string? destPath = null;
            try
            {
                string fileName = $"{Guid.NewGuid()}.png";
                destPath = Path.GetFullPath(Path.Combine(_imageDirectory, fileName));

                string? targetDirectory = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(imageSource));
                using (var stream = File.Create(destPath))
                {
                    encoder.Save(stream);
                }

                string markdown = $"![clipboard-image](note-assets/{NoteData.Id}/{fileName}){Environment.NewLine}";
                InsertTextAtCaret(markdown);
            }
            catch (IOException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (NotSupportedException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (ArgumentException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (InvalidOperationException ex)
            {
                TryDeleteGeneratedFile(destPath, "pasting clipboard image");
                AppLogger.Warn($"Failed to paste image from clipboard: {ex.Message}");
                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
#if DEBUG
                PreviewWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
#else
                PreviewWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
#endif
                await PreviewWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(PreviewRightClickBridgeScript);
                PreviewWebView.CoreWebView2.ContextMenuRequested += PreviewCoreWebView2_ContextMenuRequested;
                PreviewWebView.CoreWebView2.NavigationStarting += PreviewCoreWebView2_NavigationStarting;
                PreviewWebView.CoreWebView2.WebMessageReceived += PreviewCoreWebView2_WebMessageReceived;
                PreviewWebView.NavigationCompleted += PreviewWebView_NavigationCompleted;
                PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "yasn.local",
                    AppPaths.DataDirectory,
                    CoreWebView2HostResourceAccessKind.Allow);

                _previewReady = true;
                ApplyPreviewClip();
                await RenderPreviewAsync();
            }
            catch (COMException ex)
            {
                AppLogger.Warn($"Failed to initialize WebView2: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Warn($"Failed to initialize WebView2: {ex.Message}");
            }
            catch (NotSupportedException ex)
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
                bool shouldTrackEditorCaret = ContentTextBox.Visibility == Visibility.Visible &&
                                             ContentTextBox.IsKeyboardFocusWithin;
                double scrollRatio = shouldTrackEditorCaret
                    ? GetEditorCaretScrollRatio()
                    : await CapturePreviewScrollRatioAsync();
                _hasPendingPreviewScrollRestore = scrollRatio >= 0;
                _pendingPreviewScrollRatio = scrollRatio;

                string markdown = GetContent();
                string htmlBody = global::Markdig.Markdown.ToHtml(markdown ?? string.Empty, _markdownPipeline);
                string stylePath = PreviewStyleManager.ToStyleAbsolutePath(_previewStyleRelativePath);
                long styleVersion = File.Exists(stylePath) ? GetStyleCacheToken(stylePath) : DateTime.UtcNow.Ticks;
                string styleHref = PreviewStyleManager.BuildStyleHref(_previewStyleRelativePath, styleVersion);

                if (_isPreviewDocumentReady)
                {
                    await UpdatePreviewDocumentAsync(htmlBody, NoteData.IsDarkMode, styleHref, scrollRatio);
                    _hasPendingPreviewScrollRestore = false;
                    return;
                }

                string html = BuildHtmlPage(htmlBody, NoteData.IsDarkMode, styleHref);

                string? cacheDir = Path.GetDirectoryName(_htmlCachePath);
                if (!string.IsNullOrEmpty(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                }

                File.WriteAllText(_htmlCachePath, html);
                PreviewContainer.Opacity = 0;
                PreviewWebView.NavigateToString(html);
            }
            catch (COMException ex)
            {
                PreviewContainer.Opacity = 1;
                AppLogger.Warn($"Failed to render markdown preview: {ex.Message}");
            }
            catch (IOException ex)
            {
                PreviewContainer.Opacity = 1;
                AppLogger.Warn($"Failed to render markdown preview: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                PreviewContainer.Opacity = 1;
                AppLogger.Warn($"Failed to render markdown preview: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                PreviewContainer.Opacity = 1;
                AppLogger.Warn($"Failed to render markdown preview: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private async Task UpdatePreviewDocumentAsync(string htmlBody, bool darkMode, string styleHref, double scrollRatio)
        {
            if (PreviewWebView.CoreWebView2 == null)
            {
                return;
            }

            string themeClass = darkMode ? "theme-dark" : "theme-light";
            string htmlJson = JsonSerializer.Serialize(htmlBody ?? string.Empty);
            string themeJson = JsonSerializer.Serialize(themeClass);
            string styleJson = JsonSerializer.Serialize(styleHref);
            string ratioLiteral = scrollRatio.ToString("0.########", CultureInfo.InvariantCulture);

            string script = $"(() => {{ const html = {htmlJson}; const theme = {themeJson}; const styleHref = {styleJson}; const ratio = {ratioLiteral}; const stickToBottom = ratio >= 0.999; const root = document.scrollingElement || document.documentElement || document.body; const page = document.getElementById('page'); if (!root || !page) return; document.body.className = theme; const style = document.getElementById('yasn-style'); if (style && style.getAttribute('href') !== styleHref) style.setAttribute('href', styleHref); page.innerHTML = html; const apply = () => {{ const max = Math.max(0, root.scrollHeight - root.clientHeight); const target = stickToBottom ? max : Math.max(0, Math.min(1, ratio)) * max; if (typeof root.scrollTo === 'function') {{ root.scrollTo({{ top: target, behavior: stickToBottom ? 'auto' : 'smooth' }}); }} else {{ root.scrollTop = target; }} }}; apply(); requestAnimationFrame(apply); setTimeout(apply, 80); }})();";
            await PreviewWebView.ExecuteScriptAsync(script);
        }

        private double GetEditorCaretScrollRatio()
        {
            try
            {
                int textLength = ContentTextBox.Text?.Length ?? 0;
                if (textLength <= 0)
                {
                    return 0;
                }

                int lineCount = Math.Max(1, ContentTextBox.LineCount);
                if (lineCount <= 1)
                {
                    return 0;
                }

                int caretLine = ContentTextBox.GetLineIndexFromCharacterIndex(ContentTextBox.CaretIndex);
                int lastLine = lineCount - 1;

                // When editing at document end (new appended lines), force preview to follow to bottom.
                if (ContentTextBox.CaretIndex >= textLength - 1 || caretLine >= lastLine - 1)
                {
                    return 1;
                }

                double ratio = caretLine / (double)(lineCount - 1);
                return Math.Clamp(ratio, 0, 1);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                AppLogger.Debug($"Failed to compute editor caret scroll ratio: {ex.Message}");
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Debug($"Failed to compute editor caret scroll ratio: {ex.Message}");
                return 0;
            }
        }

        private async Task<double> CapturePreviewScrollRatioAsync()
        {
            if (!_previewReady || PreviewWebView.CoreWebView2 == null)
            {
                return -1;
            }

            try
            {
                string script = "(() => { const root = document.scrollingElement || document.documentElement || document.body; if (!root) return -1; const max = Math.max(0, root.scrollHeight - root.clientHeight); if (max <= 0) return 0; const top = root.scrollTop || window.scrollY || 0; return top / max; })();";
                string raw = await PreviewWebView.ExecuteScriptAsync(script);
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double ratio))
                {
                    return -1;
                }

                return Math.Clamp(ratio, 0, 1);
            }
            catch (COMException ex)
            {
                AppLogger.Debug($"Failed to capture preview scroll ratio: {ex.Message}");
                return -1;
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Debug($"Failed to capture preview scroll ratio: {ex.Message}");
                return -1;
            }
            catch (TaskCanceledException ex)
            {
                AppLogger.Debug($"Failed to capture preview scroll ratio: {ex.Message}");
                return -1;
            }
        }

        private async void PreviewWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess || !_hasPendingPreviewScrollRestore || PreviewWebView.CoreWebView2 == null)
            {
                if (e.IsSuccess)
                {
                    _isPreviewDocumentReady = true;
                }
                PreviewContainer.Opacity = 1;
                return;
            }

            _isPreviewDocumentReady = true;

            _hasPendingPreviewScrollRestore = false;
            if (_pendingPreviewScrollRatio < 0)
            {
                return;
            }

            try
            {
                string ratioLiteral = _pendingPreviewScrollRatio.ToString("0.########", CultureInfo.InvariantCulture);
                string script = $"(() => {{ const ratio = {ratioLiteral}; const stickToBottom = ratio >= 0.999; const root = document.scrollingElement || document.documentElement || document.body; if (!root) return; const apply = () => {{ const max = Math.max(0, root.scrollHeight - root.clientHeight); const target = stickToBottom ? max : max * Math.max(0, Math.min(1, ratio)); if (typeof root.scrollTo === 'function') {{ root.scrollTo({{ top: target, behavior: stickToBottom ? 'auto' : 'smooth' }}); }} else {{ root.scrollTop = target; }} }}; apply(); requestAnimationFrame(apply); setTimeout(apply, 80); }})();";
                await PreviewWebView.ExecuteScriptAsync(script);
            }
            catch (COMException ex)
            {
                AppLogger.Debug($"Failed to restore preview scroll position: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Debug($"Failed to restore preview scroll position: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                AppLogger.Debug($"Failed to restore preview scroll position: {ex.Message}");
            }
            finally
            {
                PreviewContainer.Opacity = 1;
            }
        }

        private static string BuildHtmlPage(string htmlBody, bool darkMode, string styleHref)
        {
            string themeClass = darkMode ? "theme-dark" : "theme-light";

            return $@"<!doctype html>
<html>
<head>
<meta charset='utf-8' />
<meta http-equiv='Content-Security-Policy' content=""default-src 'self' https://yasn.local data:; img-src 'self' https://yasn.local data: file:; style-src 'self' https://yasn.local 'unsafe-inline';"" />
<base href='https://yasn.local/' />
<link id='yasn-style' rel='stylesheet' href='{styleHref}' />
</head>
<body class='{themeClass}'>
<div id='page'>
{htmlBody}
</div>
</body>
</html>";
        }

        private static long GetStyleCacheToken(string stylePath)
        {
            try
            {
                FileInfo info = new FileInfo(stylePath);
                return info.LastWriteTimeUtc.Ticks ^ info.Length;
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to compute style cache token for '{stylePath}': {ex.Message}");
                return DateTime.UtcNow.Ticks;
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Debug($"Failed to compute style cache token for '{stylePath}': {ex.Message}");
                return DateTime.UtcNow.Ticks;
            }
        }

        private void InsertImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
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
            OpenFileDialog openFileDialog = new OpenFileDialog
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
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
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
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
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

        private static bool IsImageFile(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDeleteGeneratedFile(string? path, string reason)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to clean up generated file '{path}' after {reason}: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Debug($"Failed to clean up generated file '{path}' after {reason}: {ex.Message}");
            }
        }

        private void InsertImage(string sourceFilePath)
        {
            string destPath = null;
            try
            {
                string fileName = $"{Guid.NewGuid()}{Path.GetExtension(sourceFilePath)}";
                destPath = Path.Combine(_imageDirectory, fileName);
                File.Copy(sourceFilePath, destPath, true);

                string relativePath = $"note-assets/{NoteData.Id}/{fileName}";
                string altText = Path.GetFileNameWithoutExtension(sourceFilePath);
                string markdown = $"![{altText}]({relativePath}){Environment.NewLine}";

                InsertTextAtCaret(markdown);
            }
            catch (IOException ex)
            {
                TryDeleteGeneratedFile(destPath, "inserting image");
                AppLogger.Warn($"Failed to insert image '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to insert image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                TryDeleteGeneratedFile(destPath, "inserting image");
                AppLogger.Warn($"Failed to insert image '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to insert image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (NotSupportedException ex)
            {
                TryDeleteGeneratedFile(destPath, "inserting image");
                AppLogger.Warn($"Failed to insert image '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to insert image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (ArgumentException ex)
            {
                TryDeleteGeneratedFile(destPath, "inserting image");
                AppLogger.Warn($"Failed to insert image '{sourceFilePath}': {ex.Message}");
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
                FileInfo fileInfo = new FileInfo(sourceFilePath);
                string displayName = Path.GetFileName(sourceFilePath);
                string linkTarget;
                SettingsStore settingsStore = new SettingsStore();
                bool autoSyncEnabled = AttachmentSyncSettings.GetAutoSyncEnabled(settingsStore);
                long autoSyncMaxBytes = AttachmentSyncSettings.GetAutoSyncThresholdBytes(settingsStore);

                if (autoSyncEnabled && fileInfo.Length <= autoSyncMaxBytes)
                {
                    string fileName = $"{Guid.NewGuid()}{Path.GetExtension(sourceFilePath)}";
                    string destPath = Path.Combine(_attachmentDirectory, fileName);
                    File.Copy(sourceFilePath, destPath, true);
                    linkTarget = $"note-assets/attachments/{NoteData.Id}/{fileName}";
                }
                else
                {
                    linkTarget = new Uri(sourceFilePath, UriKind.Absolute).AbsoluteUri;
                }

                string markdown = $"[{displayName}]({linkTarget}){Environment.NewLine}";
                InsertTextAtCaret(markdown);
            }
            catch (IOException ex)
            {
                AppLogger.Warn($"Failed to insert attachment '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to insert attachment: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Warn($"Failed to insert attachment '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to insert attachment: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UriFormatException ex)
            {
                AppLogger.Warn($"Failed to insert attachment '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to insert attachment: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (ArgumentException ex)
            {
                AppLogger.Warn($"Failed to insert attachment '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to insert attachment: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InsertTextAtCaret(string text)
        {
            int index = ContentTextBox.CaretIndex;
            string current = ContentTextBox.Text ?? string.Empty;
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
            SettingsStore settingsStore = new SettingsStore();
            string modeValue = settingsStore.GetValue(
                FloatingWindowTaskbarVisibility.SettingKey,
                shouldSync: true,
                defaultValue: FloatingWindowTaskbarVisibility.DefaultValue);

            ShowInTaskbar = FloatingWindowTaskbarVisibility.ShouldShowInTaskbar(NoteData.Level, modeValue);
        }

        public void RefreshPreviewStyleFromSettings(bool forceRender = true)
        {
            SettingsStore settingsStore = new SettingsStore();
            string selectedStyle = settingsStore.GetValue(
                PreviewStyleManager.SettingKey,
                shouldSync: false,
                defaultValue: PreviewStyleManager.DefaultStyleRelativePath);
            string resolvedStyle = PreviewStyleManager.ResolveStyle(selectedStyle);
            bool hasChanged = !string.Equals(_previewStyleRelativePath, resolvedStyle, StringComparison.OrdinalIgnoreCase);
            if (hasChanged)
            {
                AppLogger.Debug($"Note {NoteData.Id} preview style changed: '{_previewStyleRelativePath}' -> '{resolvedStyle}'.");
                _previewStyleRelativePath = resolvedStyle;
                ConfigurePreviewStyleWatchers();
            }

            if (forceRender)
            {
                SchedulePreviewRender();
            }
        }

        private void ConfigurePreviewStyleWatchers()
        {
            try
            {
                _activeStyleWatcher?.Dispose();
                _activeStyleWatcher = null;
                _debugSourceStyleWatcher?.Dispose();
                _debugSourceStyleWatcher = null;

                string activeStylePath = PreviewStyleManager.ToStyleAbsolutePath(_previewStyleRelativePath);
                string? activeDirectory = Path.GetDirectoryName(activeStylePath);
                if (!string.IsNullOrEmpty(activeDirectory))
                {
                    Directory.CreateDirectory(activeDirectory);
                }

                if (!string.IsNullOrEmpty(activeDirectory) && File.Exists(activeStylePath))
                {
                    _activeStyleWatcher = new FileSystemWatcher(activeDirectory, Path.GetFileName(activeStylePath))
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName,
                        EnableRaisingEvents = true
                    };
                    _activeStyleWatcher.Changed += (_, _) => QueuePreviewStyleRefresh();
                    _activeStyleWatcher.Created += (_, _) => QueuePreviewStyleRefresh();
                    _activeStyleWatcher.Renamed += (_, _) => QueuePreviewStyleRefresh();
                }

#if DEBUG
                string? sourceStylePath = TryResolveDebugSourceStylePath(_previewStyleRelativePath);
                if (string.IsNullOrEmpty(sourceStylePath) ||
                    string.Equals(sourceStylePath, activeStylePath, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(sourceStylePath)) return;
                {
                    string? sourceDirectory = Path.GetDirectoryName(sourceStylePath);
                    if (string.IsNullOrEmpty(sourceDirectory)) return;
                    _debugSourceStyleWatcher = new FileSystemWatcher(sourceDirectory, Path.GetFileName(sourceStylePath))
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName,
                        EnableRaisingEvents = true
                    };

                    void SyncAndRefresh()
                    {
                        try
                        {
                            File.Copy(sourceStylePath, activeStylePath, overwrite: true);
                        }
                        catch (IOException ex)
                        {
                            AppLogger.Debug($"Failed to sync preview style from '{sourceStylePath}' to '{activeStylePath}': {ex.Message}");
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            AppLogger.Debug($"Failed to sync preview style from '{sourceStylePath}' to '{activeStylePath}': {ex.Message}");
                        }

                        QueuePreviewStyleRefresh();
                    }

                    _debugSourceStyleWatcher.Changed += (_, _) => SyncAndRefresh();
                    _debugSourceStyleWatcher.Created += (_, _) => SyncAndRefresh();
                    _debugSourceStyleWatcher.Renamed += (_, _) => SyncAndRefresh();
                }
#endif
            }
            catch (IOException ex)
            {
                AppLogger.Warn($"Failed to configure preview style watcher: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Warn($"Failed to configure preview style watcher: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Warn($"Failed to configure preview style watcher: {ex.Message}");
            }
        }

        private void QueuePreviewStyleRefresh()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshPreviewStyleFromSettings(forceRender: true);
            }), DispatcherPriority.Background);
        }

#if DEBUG
        private static string? TryResolveDebugSourceStylePath(string relativeStylePath)
        {
            try
            {
                DirectoryInfo baseDir = new DirectoryInfo(AppPaths.BaseDirectory);
                for (DirectoryInfo? current = baseDir; current != null; current = current.Parent)
                {
                    string csproj = Path.Combine(current.FullName, "YASN.csproj");
                    if (!File.Exists(csproj))
                    {
                        continue;
                    }

                    string candidate = Path.Combine(current.FullName, "style", relativeStylePath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to resolve debug preview style path '{relativeStylePath}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Debug($"Failed to resolve debug preview style path '{relativeStylePath}': {ex.Message}");
            }
            catch (ArgumentException ex)
            {
                AppLogger.Debug($"Failed to resolve debug preview style path '{relativeStylePath}': {ex.Message}");
            }

            return null;
        }
#endif

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
#if DEBUG
            bool isOpenDevToolsShortcut = e.Key == Key.F12 ||
                                         (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.I);
            if (isOpenDevToolsShortcut)
            {
                try
                {
                    PreviewWebView.CoreWebView2?.OpenDevToolsWindow();
                }
                catch (COMException ex)
                {
                    AppLogger.Warn($"Failed to open WebView2 DevTools: {ex.Message}");
                }
                catch (InvalidOperationException ex)
                {
                    AppLogger.Warn($"Failed to open WebView2 DevTools: {ex.Message}");
                }

                e.Handled = true;
                return;
            }
#endif

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

            DateTime now = DateTime.UtcNow;
            TimeSpan threshold = TimeSpan.FromMilliseconds(900);
            bool isDoubleRightClick = now - _lastPreviewSurfaceRightClickUtc <= threshold;
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

            DateTime now = DateTime.UtcNow;
            TimeSpan threshold = TimeSpan.FromMilliseconds(900);
            bool isDoubleRightClick = now - _lastPreviewRightClickUtc <= threshold;
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
            catch (COMException ex)
            {
                AppLogger.Debug($"Failed to read preview web message: {ex.Message}");
                return;
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Debug($"Failed to read preview web message: {ex.Message}");
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

            if (!TryResolveOpenTarget(e.Uri, out string? openTarget))
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
                    string localPath = uri.LocalPath;
                    if (File.Exists(localPath))
                    {
                        openTarget = localPath;
                        return true;
                    }
                }
                else if (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(uri.Host, "yasn.local", StringComparison.OrdinalIgnoreCase))
                {
                    string localRelative = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))
                        .Replace('/', Path.DirectorySeparatorChar);
                    string localPath = Path.Combine(AppPaths.DataDirectory, localRelative);
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
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch (Win32Exception ex)
            {
                AppLogger.Warn($"Failed to open target '{target}': {ex.Message}");
                MessageBox.Show($"Fail to open attachment: {ex.Message}", "Open Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (FileNotFoundException ex)
            {
                AppLogger.Warn($"Failed to open target '{target}': {ex.Message}");
                MessageBox.Show($"Fail to open attachment: {ex.Message}", "Open Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Warn($"Failed to open target '{target}': {ex.Message}");
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
                DragMove();

                if (NoteData.Level == WindowLevel.BottomMost)
                {
                    ApplyWindowLevel();
                }
            }
        }

        private void TitleBar_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement placementTarget)
            {
                return;
            }

            ShowTitleBarContextMenu(placementTarget);
            e.Handled = true;
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
            if (sender is not FrameworkElement placementTarget)
            {
                return;
            }

            ShowTitleBarContextMenu(placementTarget);
        }

        private void ShowTitleBarContextMenu(FrameworkElement placementTarget)
        {
            ContextMenu contextMenu = new ContextMenu();

            MenuItem renameTitleItem = new MenuItem { Header = "Edit Title" };
            renameTitleItem.Click += (_, _) => PromptRenameTitle();

            MenuItem quickMoveItem = new MenuItem { Header = "Quick Move" };
            quickMoveItem.Click += (_, _) => FloatingWindowQuickActions.ShowQuickMove(this);

            MenuItem quickMoveAndResizeItem = new MenuItem { Header = "Quick Move + Resize" };
            quickMoveAndResizeItem.Click += (_, _) => FloatingWindowQuickActions.ShowQuickMoveAndResize(this);

            MenuItem showMainWindowItem = new MenuItem { Header = "Open MainWindow" };
            showMainWindowItem.Click += (_, _) =>
            {
                if (Application.Current is not App app || app.MainWindow == null) return;
                app.MainWindow.Show();
                app.MainWindow.WindowState = WindowState.Normal;
                app.MainWindow.Activate();
            };

            MenuItem createNoteItem = new MenuItem { Header = "Create New Note" };
            createNoteItem.Click += (_, _) =>
            {
                NoteData newNote = NoteManager.Instance.CreateNote();
                new FloatingWindow(newNote).Show();
            };

            MenuItem createTopMostNoteItem = new MenuItem { Header = "Create TopMost Note" };
            createTopMostNoteItem.Click += (_, _) =>
            {
                NoteData newNote = NoteManager.Instance.CreateNote(WindowLevel.TopMost);
                new FloatingWindow(newNote).Show();
            };

            MenuItem deleteNoteItem = new MenuItem { Header = "Delete Note" };
            deleteNoteItem.Click += (_, _) =>
            {
                MessageBoxResult? result = MessageBox.Show(
                    "Are you sure you want to delete this note?",
                    "Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;
                NoteManager.Instance.DeleteNote(NoteData);
                Close();
            };

            MenuItem clearContentItem = new MenuItem { Header = "Clear Content" };
            clearContentItem.Click += (_, _) =>
            {
                MessageBoxResult? result = MessageBox.Show(
                    "Are you sure you want to clear the current content?",
                    "Clear Content",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ContentTextBox.Text = string.Empty;
                }
            };

            MenuItem changeTitleBarColorItem = new MenuItem { Header = "Change Title Bar Color" };
            changeTitleBarColorItem.Click += (_, _) => ShowColorPicker();

            MenuItem backgroundImageItem = new MenuItem { Header = "Background Image" };
            backgroundImageItem.Click += (_, _) => ShowBackgroundImageMenu(placementTarget);

            MenuItem aboutItem = new MenuItem { Header = "About" };
            aboutItem.Click += (_, _) =>
            {
                MessageBox.Show(
                    "YASN - Yet Another Sticky Notes\nv1.0.1\n\nMarkdown mode enabled.",
                    "About YASN",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            };

            contextMenu.Items.Add(renameTitleItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(quickMoveItem);
            contextMenu.Items.Add(quickMoveAndResizeItem);
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

            contextMenu.PlacementTarget = placementTarget;
            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
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

        private void QuickResizeButton_Click(object sender, RoutedEventArgs e)
        {
            FloatingWindowQuickActions.ShowQuickResize(this);
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

        public void ChangeWindowLevel(WindowLevel level)
        {
            SetWindowLevel(level);
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
            MinimizeButton?.Visibility = NoteData.Level == WindowLevel.Normal
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void ExpandWindowWidthForSplitMode()
        {
            if (WindowState != WindowState.Normal)
            {
                return;
            }

            if (double.IsNaN(_singleModeWidthBeforeSplit))
            {
                _singleModeWidthBeforeSplit = Width;
            }

            double minWidth = MinWidth > 0 ? MinWidth : 320;
            double targetWidth = Math.Max(minWidth, _singleModeWidthBeforeSplit * 2);
            double maxWidth = Math.Max(minWidth, SystemParameters.WorkArea.Width - 20);
            double finalWidth = Math.Min(targetWidth, maxWidth);
            if (Math.Abs(finalWidth - Width) <= 1)
            {
                return;
            }

            double currentWidth = Width;
            Width = finalWidth;
            AppLogger.Debug($"Auto expand width for split mode: {currentWidth:F0} -> {finalWidth:F0}");
        }

        private void RestoreWindowWidthAfterSplitMode()
        {
            if (WindowState != WindowState.Normal || double.IsNaN(_singleModeWidthBeforeSplit))
            {
                return;
            }

            double minWidth = MinWidth > 0 ? MinWidth : 320;
            double restoreWidth = Math.Max(minWidth, _singleModeWidthBeforeSplit);
            double currentWidth = Width;
            _singleModeWidthBeforeSplit = double.NaN;
            if (Math.Abs(currentWidth - restoreWidth) <= 1)
            {
                return;
            }

            Width = restoreWidth;
            AppLogger.Debug($"Restore width after leaving split mode: {currentWidth:F0} -> {restoreWidth:F0}");
        }

        private void PromptRenameTitle()
        {
            string? input = ShowTitleInputDialog(NoteData.Title);
            if (input == null)
            {
                return;
            }

            string newTitle = input.Trim();
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

            bool hasDuplicate = NoteManager.Instance.Notes.Any(n =>
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
            Window dialog = new Window
            {
                Title = "Edit Title",
                Width = 360,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            Grid grid = new Grid { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock prompt = new TextBlock
            {
                Text = "New Title",
                Margin = new Thickness(0, 0, 0, 8)
            };

            System.Windows.Controls.TextBox inputBox = new System.Windows.Controls.TextBox
            {
                Text = initialValue ?? string.Empty,
                Margin = new Thickness(0, 0, 0, 12)
            };

            StackPanel buttonPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button okButton = new Button
            {
                Content = "Confirm",
                Width = 72,
                IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0)
            };

            Button cancelButton = new Button
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
            string levelPrefix = NoteData.Level switch
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
                            FloatingWindow previousWindow = _currentBottomMostWindow;
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
            _activeStyleWatcher?.Dispose();
            _debugSourceStyleWatcher?.Dispose();

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
            _ = ApplyPreviewThemeClassAsync(NoteData.IsDarkMode);
            _previewDebounceTimer.Stop();
            _ = RenderPreviewAsync();
        }

        private async Task ApplyPreviewThemeClassAsync(bool isDarkMode)
        {
            if (!_previewReady || PreviewWebView.CoreWebView2 == null)
            {
                return;
            }

            try
            {
                string themeClass = isDarkMode ? "theme-dark" : "theme-light";
                string script = $"(() => {{ if (document.body) document.body.className = '{themeClass}'; }})();";
                await PreviewWebView.ExecuteScriptAsync(script);
            }
            catch (COMException ex)
            {
                AppLogger.Debug($"Failed to apply preview theme class: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Debug($"Failed to apply preview theme class: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                AppLogger.Debug($"Failed to apply preview theme class: {ex.Message}");
            }
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
                ContentTextBox.Background = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00));
            }
            else
            {
                MainBorder.Background = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xF0));
                MainBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xC0, 0xC0, 0xC0));
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
                MarkdownToolbar.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xE0, 0xE0, 0xE0));
                ContentTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
                ContentTextBox.Background = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
            }
        }

        internal void ReapplyWindowLevelAfterQuickLayout()
        {
            switch (NoteData.Level)
            {
                case WindowLevel.BottomMost:
                    ApplyWindowLevel();
                    break;
                case WindowLevel.TopMost:
                    Topmost = true;
                    break;
            }
        }

        private void ApplyTitleBarColor(string colorHex)
        {
            try
            {
                Color color = (Color)ColorConverter.ConvertFromString(colorHex);
                TitleBar.Background = new SolidColorBrush(color);
            }
            catch (FormatException ex)
            {
                AppLogger.Warn($"Failed to apply title bar color '{colorHex}': {ex.Message}");
                TitleBar.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0xD4, 0xC5, 0xE0));
            }
            catch (NotSupportedException ex)
            {
                AppLogger.Warn($"Failed to apply title bar color '{colorHex}': {ex.Message}");
                TitleBar.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0xD4, 0xC5, 0xE0));
            }
        }
        private void ShowColorPicker()
        {
            Window colorPickerWindow = new Window
            {
                Title = "Title Bar Color",
                Width = 390,
                Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            StackPanel stackPanel = new StackPanel { Margin = new Thickness(18) };
            stackPanel.Children.Add(new TextBlock
            {
                Text = "Choose a preset color",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            Grid colorsGrid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            colorsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            colorsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            colorsGrid.ColumnDefinitions.Add(new ColumnDefinition());

            (string, string)[] presetColors =
            [
                ("#E6D4C5E0", "Lavender"),
                ("#E6FFB6C1", "Rose"),
                ("#E6B0E0E6", "Sky"),
                ("#E6C8E6C9", "Mint"),
                ("#E6FFE4B5", "Peach"),
                ("#E6F5DEB3", "Sand"),
                ("#E6E6E6FA", "Soft Indigo"),
                ("#E6FFE4E1", "Mist")
            ];

            int row = 0;
            int col = 0;
            foreach (var (colorHex, colorName) in presetColors)
            {
                if (col == 0)
                {
                    colorsGrid.RowDefinitions.Add(new RowDefinition());
                }

                Button button = new Button
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
                    string? selectedColor = (s as Button)?.Tag as string;
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

        private void ShowBackgroundImageMenu(FrameworkElement anchorElement)
        {
            ContextMenu contextMenu = new ContextMenu();

            MenuItem selectImageItem = new MenuItem { Header = "Select Background Image" };
            selectImageItem.Click += (_, _) =>
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "Image File|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
                    Title = "Select Background Image"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    SetBackgroundImage(openFileDialog.FileName);
                }
            };

            MenuItem clearBackgroundItem = new MenuItem { Header = "Clear Background Image" };
            clearBackgroundItem.Click += (_, _) => ClearBackgroundImage();

            MenuItem adjustOpacityItem = new MenuItem { Header = "Adjust Background Opacity" };
            adjustOpacityItem.Click += (_, _) => ShowOpacityAdjuster();

            contextMenu.Items.Add(selectImageItem);
            contextMenu.Items.Add(clearBackgroundItem);
            contextMenu.Items.Add(adjustOpacityItem);

            contextMenu.PlacementTarget = anchorElement;
            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            contextMenu.IsOpen = true;
        }

        private void SetBackgroundImage(string sourceFilePath)
        {
            string destPath = null;
            try
            {
                string fileName = $"background{Path.GetExtension(sourceFilePath)}";
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
            catch (IOException ex)
            {
                TryDeleteGeneratedFile(destPath, "setting background image");
                AppLogger.Warn($"Failed to set background image from '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to set background image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                TryDeleteGeneratedFile(destPath, "setting background image");
                AppLogger.Warn($"Failed to set background image from '{sourceFilePath}': {ex.Message}");
                MessageBox.Show($"Fail to set background image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (ArgumentException ex)
            {
                TryDeleteGeneratedFile(destPath, "setting background image");
                AppLogger.Warn($"Failed to set background image from '{sourceFilePath}': {ex.Message}");
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
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                BackgroundImageBrush.ImageSource = bitmap;
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to load background image '{imagePath}': {ex.Message}");
                BackgroundImageBrush.ImageSource = null;
            }
            catch (NotSupportedException ex)
            {
                AppLogger.Debug($"Failed to load background image '{imagePath}': {ex.Message}");
                BackgroundImageBrush.ImageSource = null;
            }
            catch (UriFormatException ex)
            {
                AppLogger.Debug($"Failed to load background image '{imagePath}': {ex.Message}");
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
                foreach (string file in Directory.GetFiles(_backgroundImageDirectory))
                {
                    File.Delete(file);
                }
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Failed to clear background image files in '{_backgroundImageDirectory}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Debug($"Failed to clear background image files in '{_backgroundImageDirectory}': {ex.Message}");
            }
        }

        private void ShowOpacityAdjuster()
        {
            Window opacityWindow = new Window
            {
                Title = "Background Opacity",
                Width = 300,
                Height = 190,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            StackPanel stackPanel = new StackPanel { Margin = new Thickness(20) };
            stackPanel.Children.Add(new TextBlock
            {
                Text = "Background opacity",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            Slider slider = new Slider
            {
                Minimum = 0.05,
                Maximum = 1.0,
                Value = BackgroundImageBorder.Opacity,
                TickFrequency = 0.05,
                IsSnapToTickEnabled = true,
                Margin = new Thickness(0, 0, 0, 10)
            };

            TextBlock valueText = new TextBlock
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

        // For Editor mode switching in title bar
        EditorDisplayMode GetNextEditorDisplayMode(EditorDisplayMode previousMode)
        {
            return previousMode switch
            {
                EditorDisplayMode.TextOnly => EditorDisplayMode.TextAndPreview,
                EditorDisplayMode.TextAndPreview => EditorDisplayMode.PreviewOnly,
                _ => EditorDisplayMode.TextOnly
            };
        }
        private void EditorModeButton_Click(object sender, RoutedEventArgs e)
        {
            EditorDisplayMode nextMode = GetNextEditorDisplayMode(_editorDisplayMode);
            SetDisplayMode(nextMode, focusEditor: nextMode != EditorDisplayMode.PreviewOnly);
        }

        private static string GetEditorModeLabel(EditorDisplayMode mode)
        {
            return mode switch
            {
                EditorDisplayMode.TextOnly => "Text only",
                EditorDisplayMode.TextAndPreview => "Text + Preview",
                _ => "Preview only"
            };
        }

        private void UpdateEditorModeButton()
        {
            // for collapse
            if (EditorModeButton == null)
            {
                return;
            }

            EditorDisplayMode nextMode = GetNextEditorDisplayMode(_editorDisplayMode);
            switch (_editorDisplayMode)
            {
                case EditorDisplayMode.TextOnly:
                    EditorModeButton.Content = IconModeTextOnly;
                    EditorModeButton.ToolTip = $"Mode: Text only (Next: {GetEditorModeLabel(nextMode)})";
                    break;
                case EditorDisplayMode.TextAndPreview:
                    EditorModeButton.Content = IconModeTextAndPreview;
                    EditorModeButton.ToolTip = $"Mode: Text + Preview (Next: {GetEditorModeLabel(nextMode)})";
                    break;
                case EditorDisplayMode.PreviewOnly:
                default:
                    EditorModeButton.Content = IconModePreviewOnly;
                    EditorModeButton.ToolTip = $"Mode: Preview only (Next: {GetEditorModeLabel(nextMode)})";
                    break;
            }
        }

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            SetDisplayMode(EditorDisplayMode.PreviewOnly);
        }
    }
}


