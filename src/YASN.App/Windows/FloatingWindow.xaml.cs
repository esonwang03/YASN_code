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
using YASN.Infrastructure.Logging;
using YASN.Infrastructure.Markdown;
using YASN.App.Settings;
using YASN.App.WindowLayout;
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

        private void TitleBar_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
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
                if (Application.Current is not global::YASN.App.App app || app.MainWindow == null) return;
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
                    "YASN - Yet Another Sticky Notes\nv1.0.3\n\nMarkdown mode enabled.",
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
                    "Title can't be empty.",
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

    }
}

