using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Globalization;
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
        private bool _isUpdatingEditorModeSelector;
        private bool _hasPendingPreviewScrollRestore;
        private bool _isChromeExpanded = true;
        private bool _autoCollapseChromeEnabled = NoteWindowUiSettings.DefaultAutoCollapseChrome;
        private string _previewStyleRelativePath = PreviewStyleManager.DefaultStyleRelativePath;
        private EditorDisplayMode _editorDisplayMode = EditorDisplayMode.PreviewOnly;
        private double _singleModeWidthBeforeSplit = double.NaN;
        private DateTime _lastPreviewRightClickUtc = DateTime.MinValue;
        private DateTime _lastPreviewSurfaceRightClickUtc = DateTime.MinValue;
        private double _pendingPreviewScrollRatio = -1;

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
            if (NoteData.LastEditorDisplayMode.HasValue)
            {
                SetDisplayMode(NoteData.LastEditorDisplayMode.Value, adjustWindowWidth: false);
                return;
            }

            var hasContent = !string.IsNullOrWhiteSpace(GetContent());
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
            var targetMode = GetConfiguredEditorEnterMode();
            SetDisplayMode(targetMode, focusEditor: focusEditor && targetMode != EditorDisplayMode.PreviewOnly);
        }

        private EditorDisplayMode GetConfiguredEditorEnterMode()
        {
            var settingsStore = new SettingsStore();
            return EditorDisplayModeSettings.GetEnterMode(settingsStore);
        }

        private void SetDisplayMode(
            EditorDisplayMode mode,
            bool focusEditor = false,
            bool adjustWindowWidth = true)
        {
            var previousMode = _editorDisplayMode;
            _editorDisplayMode = mode;
            var shouldPersistDisplayMode = NoteData.LastEditorDisplayMode != mode;
            if (shouldPersistDisplayMode)
            {
                NoteData.LastEditorDisplayMode = mode;
            }

            var showEditor = mode is EditorDisplayMode.TextOnly or EditorDisplayMode.TextAndPreview;
            var showPreview = mode is EditorDisplayMode.PreviewOnly or EditorDisplayMode.TextAndPreview;
            var showSplitter = mode == EditorDisplayMode.TextAndPreview;

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

            if (adjustWindowWidth &&
                mode == EditorDisplayMode.TextAndPreview &&
                previousMode != EditorDisplayMode.TextAndPreview)
            {
                ExpandWindowWidthForSplitMode();
            }
            else if (adjustWindowWidth &&
                     previousMode == EditorDisplayMode.TextAndPreview &&
                     mode != EditorDisplayMode.TextAndPreview)
            {
                RestoreWindowWidthAfterSplitMode();
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

        private void ContentTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var isPasteShortcut = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.V;
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
                    using var clipboardImage = WinFormsClipboard.GetImage();
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

                var files = System.Windows.Clipboard.GetFileDropList();
                if (files.Count == 0)
                {
                    return false;
                }

                var insertedAny = false;
                foreach (var path in files.Cast<string>())
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
            catch (Exception ex)
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
                var fileName = $"{Guid.NewGuid()}.png";
                destPath = Path.GetFullPath(Path.Combine(_imageDirectory, fileName));

                var targetDirectory = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                using var bitmap = new System.Drawing.Bitmap(image);
                using (var stream = File.Create(destPath))
                {
                    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                }

                var markdown = $"![clipboard-image](note-assets/{NoteData.Id}/{fileName}){Environment.NewLine}";
                InsertTextAtCaret(markdown);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(destPath) && File.Exists(destPath))
                {
                    try
                    {
                        File.Delete(destPath);
                    }
                    catch
                    {
                    }
                }

                MessageBox.Show($"Fail to paste image: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InsertClipboardImage(BitmapSource imageSource)
        {
            string? destPath = null;
            try
            {
                var fileName = $"{Guid.NewGuid()}.png";
                destPath = Path.GetFullPath(Path.Combine(_imageDirectory, fileName));

                var targetDirectory = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(imageSource));
                using (var stream = File.Create(destPath))
                {
                    encoder.Save(stream);
                }

                var markdown = $"![clipboard-image](note-assets/{NoteData.Id}/{fileName}){Environment.NewLine}";
                InsertTextAtCaret(markdown);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(destPath) && File.Exists(destPath))
                {
                    try
                    {
                        File.Delete(destPath);
                    }
                    catch
                    {
                    }
                }

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
                var shouldTrackEditorCaret = ContentTextBox.Visibility == Visibility.Visible &&
                                             ContentTextBox.IsKeyboardFocusWithin;
                var scrollRatio = shouldTrackEditorCaret
                    ? GetEditorCaretScrollRatio()
                    : await CapturePreviewScrollRatioAsync();
                _hasPendingPreviewScrollRestore = scrollRatio >= 0;
                _pendingPreviewScrollRatio = scrollRatio;

                var markdown = GetContent();
                var htmlBody = global::Markdig.Markdown.ToHtml(markdown ?? string.Empty, _markdownPipeline);
                var stylePath = PreviewStyleManager.ToStyleAbsolutePath(_previewStyleRelativePath);
                var styleVersion = File.Exists(stylePath) ? GetStyleCacheToken(stylePath) : DateTime.UtcNow.Ticks;
                var styleHref = PreviewStyleManager.BuildStyleHref(_previewStyleRelativePath, styleVersion);

                if (_isPreviewDocumentReady)
                {
                    await UpdatePreviewDocumentAsync(htmlBody, NoteData.IsDarkMode, styleHref, scrollRatio);
                    _hasPendingPreviewScrollRestore = false;
                    return;
                }

                var html = BuildHtmlPage(htmlBody, NoteData.IsDarkMode, styleHref);

                var cacheDir = Path.GetDirectoryName(_htmlCachePath);
                if (!string.IsNullOrEmpty(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                }

                File.WriteAllText(_htmlCachePath, html);
                PreviewContainer.Opacity = 0;
                PreviewWebView.NavigateToString(html);
            }
            catch (Exception ex)
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

            var themeClass = darkMode ? "theme-dark" : "theme-light";
            var htmlJson = JsonSerializer.Serialize(htmlBody ?? string.Empty);
            var themeJson = JsonSerializer.Serialize(themeClass);
            var styleJson = JsonSerializer.Serialize(styleHref);
            var ratioLiteral = scrollRatio.ToString("0.########", CultureInfo.InvariantCulture);

            var script = $"(() => {{ const html = {htmlJson}; const theme = {themeJson}; const styleHref = {styleJson}; const ratio = {ratioLiteral}; const stickToBottom = ratio >= 0.999; const root = document.scrollingElement || document.documentElement || document.body; const page = document.getElementById('page'); if (!root || !page) return; document.body.className = theme; const style = document.getElementById('yasn-style'); if (style && style.getAttribute('href') !== styleHref) style.setAttribute('href', styleHref); page.innerHTML = html; const apply = () => {{ const max = Math.max(0, root.scrollHeight - root.clientHeight); const target = stickToBottom ? max : Math.max(0, Math.min(1, ratio)) * max; if (typeof root.scrollTo === 'function') {{ root.scrollTo({{ top: target, behavior: stickToBottom ? 'auto' : 'smooth' }}); }} else {{ root.scrollTop = target; }} }}; apply(); requestAnimationFrame(apply); setTimeout(apply, 80); }})();";
            await PreviewWebView.ExecuteScriptAsync(script);
        }

        private double GetEditorCaretScrollRatio()
        {
            try
            {
                var textLength = ContentTextBox.Text?.Length ?? 0;
                if (textLength <= 0)
                {
                    return 0;
                }

                var lineCount = Math.Max(1, ContentTextBox.LineCount);
                if (lineCount <= 1)
                {
                    return 0;
                }

                var caretLine = ContentTextBox.GetLineIndexFromCharacterIndex(ContentTextBox.CaretIndex);
                var lastLine = lineCount - 1;

                // When editing at document end (new appended lines), force preview to follow to bottom.
                if (ContentTextBox.CaretIndex >= textLength - 1 || caretLine >= lastLine - 1)
                {
                    return 1;
                }

                var ratio = caretLine / (double)(lineCount - 1);
                return Math.Clamp(ratio, 0, 1);
            }
            catch
            {
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
                var script = "(() => { const root = document.scrollingElement || document.documentElement || document.body; if (!root) return -1; const max = Math.max(0, root.scrollHeight - root.clientHeight); if (max <= 0) return 0; const top = root.scrollTop || window.scrollY || 0; return top / max; })();";
                var raw = await PreviewWebView.ExecuteScriptAsync(script);
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio))
                {
                    return -1;
                }

                return Math.Clamp(ratio, 0, 1);
            }
            catch
            {
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
                var ratioLiteral = _pendingPreviewScrollRatio.ToString("0.########", CultureInfo.InvariantCulture);
                var script = $"(() => {{ const ratio = {ratioLiteral}; const stickToBottom = ratio >= 0.999; const root = document.scrollingElement || document.documentElement || document.body; if (!root) return; const apply = () => {{ const max = Math.max(0, root.scrollHeight - root.clientHeight); const target = stickToBottom ? max : max * Math.max(0, Math.min(1, ratio)); if (typeof root.scrollTo === 'function') {{ root.scrollTo({{ top: target, behavior: stickToBottom ? 'auto' : 'smooth' }}); }} else {{ root.scrollTop = target; }} }}; apply(); requestAnimationFrame(apply); setTimeout(apply, 80); }})();";
                await PreviewWebView.ExecuteScriptAsync(script);
            }
            catch
            {
                // ignore restore failures
            }
            finally
            {
                PreviewContainer.Opacity = 1;
            }
        }

        private static string BuildHtmlPage(string htmlBody, bool darkMode, string styleHref)
        {
            var themeClass = darkMode ? "theme-dark" : "theme-light";

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
                var info = new FileInfo(stylePath);
                return info.LastWriteTimeUtc.Ticks ^ info.Length;
            }
            catch
            {
                return DateTime.UtcNow.Ticks;
            }
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

        public void RefreshPreviewStyleFromSettings(bool forceRender = true)
        {
            var settingsStore = new SettingsStore();
            var selectedStyle = settingsStore.GetValue(
                PreviewStyleManager.SettingKey,
                shouldSync: false,
                defaultValue: PreviewStyleManager.DefaultStyleRelativePath);
            var resolvedStyle = PreviewStyleManager.ResolveStyle(selectedStyle);
            var hasChanged = !string.Equals(_previewStyleRelativePath, resolvedStyle, StringComparison.OrdinalIgnoreCase);
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

                var activeStylePath = PreviewStyleManager.ToStyleAbsolutePath(_previewStyleRelativePath);
                var activeDirectory = Path.GetDirectoryName(activeStylePath);
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
                var sourceStylePath = TryResolveDebugSourceStylePath(_previewStyleRelativePath);
                if (!string.IsNullOrEmpty(sourceStylePath) &&
                    !string.Equals(sourceStylePath, activeStylePath, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(sourceStylePath))
                {
                    var sourceDirectory = Path.GetDirectoryName(sourceStylePath);
                    if (!string.IsNullOrEmpty(sourceDirectory))
                    {
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
                            catch
                            {
                            }

                            QueuePreviewStyleRefresh();
                        }

                        _debugSourceStyleWatcher.Changed += (_, _) => SyncAndRefresh();
                        _debugSourceStyleWatcher.Created += (_, _) => SyncAndRefresh();
                        _debugSourceStyleWatcher.Renamed += (_, _) => SyncAndRefresh();
                    }
                }
#endif
            }
            catch (Exception ex)
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
                var baseDir = new DirectoryInfo(AppPaths.BaseDirectory);
                for (var current = baseDir; current != null; current = current.Parent)
                {
                    var csproj = Path.Combine(current.FullName, "YASN.csproj");
                    if (!File.Exists(csproj))
                    {
                        continue;
                    }

                    var candidate = Path.Combine(current.FullName, "style", relativeStylePath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
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
            var isOpenDevToolsShortcut = e.Key == Key.F12 ||
                                         (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.I);
            if (isOpenDevToolsShortcut)
            {
                try
                {
                    PreviewWebView.CoreWebView2?.OpenDevToolsWindow();
                }
                catch (Exception ex)
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
                DragMove();

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
                    "YASN - Yet Another Sticky Notes\nv1.0.1\n\nMarkdown mode enabled.",
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
            if (MinimizeButton == null)
            {
                return;
            }

            MinimizeButton.Visibility = NoteData.Level == WindowLevel.Normal
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

            var minWidth = MinWidth > 0 ? MinWidth : 320;
            var targetWidth = Math.Max(minWidth, _singleModeWidthBeforeSplit * 2);
            var maxWidth = Math.Max(minWidth, SystemParameters.WorkArea.Width - 20);
            var finalWidth = Math.Min(targetWidth, maxWidth);
            if (Math.Abs(finalWidth - Width) <= 1)
            {
                return;
            }

            var currentWidth = Width;
            Width = finalWidth;
            AppLogger.Debug($"Auto expand width for split mode: {currentWidth:F0} -> {finalWidth:F0}");
        }

        private void RestoreWindowWidthAfterSplitMode()
        {
            if (WindowState != WindowState.Normal || double.IsNaN(_singleModeWidthBeforeSplit))
            {
                return;
            }

            var minWidth = MinWidth > 0 ? MinWidth : 320;
            var restoreWidth = Math.Max(minWidth, _singleModeWidthBeforeSplit);
            var currentWidth = Width;
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
                var themeClass = isDarkMode ? "theme-dark" : "theme-light";
                var script = $"(() => {{ if (document.body) document.body.className = '{themeClass}'; }})();";
                await PreviewWebView.ExecuteScriptAsync(script);
            }
            catch
            {
                // ignore immediate theme apply failures
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
                        File.Delete(file);
                }
            }
            catch (Exception ex) 
            {
                AppLogger.Debug(ex.Message);
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
            var nextMode = GetNextEditorDisplayMode(_editorDisplayMode);
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

            var nextMode = GetNextEditorDisplayMode(_editorDisplayMode);
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


