using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using YASN.AvaloniaNotes;
using YASN.Core;
using YASN.Hotkeys;
using YASN.Infrastructure;
using YASN.Notifications;
using YASN.PlatformServices;
using YASN.Reminders;
using YASN.ViewModels;
using YASN.WindowLayout;

namespace YASN.Views
{
    /// <summary>
    /// Displays the phase-two single note editor and Markdown preview.
    /// </summary>
    public sealed partial class FloatingNoteWindow : Window
    {
        private readonly NoteWindowViewModel viewModel;
        private readonly IWindowLevelController windowLevels;
        private readonly IQuickWindowLayoutController quickLayout;
        private readonly Infrastructure.Settings.SettingsStore settings;
        private readonly NativeWebView previewWebView;
        private readonly ComboBox levelSelector;
        private readonly TextBox editorTextBox;
        private readonly ColumnDefinition editorColumn;
        private readonly ColumnDefinition previewColumn;
        private readonly Control editorPanel;
        private readonly Control previewPanel;
        private readonly Button editorModeButton;
        private readonly Material.Icons.Avalonia.MaterialIcon editorModeIcon;
        private readonly EditorHotkeyController editorHotkeys;
        private List<WindowLevel> supportedLevels = new();
        private double savedLeftPhysical = double.NaN;
        private double savedWidthDip = double.NaN;
        private Action? openMainWindowAction;
        private readonly Avalonia.Threading.DispatcherTimer caretSyncTimer;
        private int caretLine;
        /// <summary>
        /// Initializes a note window for XAML loading and simple manual startup.
        /// </summary>
        public FloatingNoteWindow()
            : this(CreateDefaultViewModel(), new AvaloniaWindowLevelController(), new AvaloniaQuickWindowLayoutController(),
                new KeybindingRegistry(new Infrastructure.Settings.SettingsStore()), new Infrastructure.Settings.SettingsStore())
        {
        }

        /// <summary>
        /// Initializes a note window with an explicit view model and window-level controller.
        /// </summary>
        /// <param name="viewModel">The note view model to bind and preview.</param>
        /// <param name="windowLevels">The service used to apply window levels.</param>
        /// <param name="quickLayout">The service used to apply overlay-selected bounds.</param>
        /// <param name="keybindings">The shared keybinding registry for editor hotkeys.</param>
        /// <param name="settings">The shared settings store read for attachment and taskbar policy.</param>
        public FloatingNoteWindow(
            NoteWindowViewModel viewModel,
            IWindowLevelController windowLevels,
            IQuickWindowLayoutController quickLayout,
            KeybindingRegistry keybindings,
            Infrastructure.Settings.SettingsStore settings)
        {
            this.viewModel = viewModel;
            this.windowLevels = windowLevels;
            this.quickLayout = quickLayout;
            this.settings = settings;
            InitializeComponent();

            previewWebView = this.FindControl<NativeWebView>("PreviewWebView")
                ?? throw new InvalidOperationException("PreviewWebView was not found.");
            levelSelector = this.FindControl<ComboBox>("LevelSelector")
                ?? throw new InvalidOperationException("LevelSelector was not found.");
            editorTextBox = this.FindControl<TextBox>("EditorTextBox")
                ?? throw new InvalidOperationException("EditorTextBox was not found.");
            Thumb resizeGrip = this.FindControl<Thumb>("ResizeGrip")
                ?? throw new InvalidOperationException("ResizeGrip was not found.");
            editorPanel = this.FindControl<DockPanel>("EditorPanel")
                ?? throw new InvalidOperationException("EditorPanel was not found.");
            previewPanel = this.FindControl<Border>("PreviewPanel")
                ?? throw new InvalidOperationException("PreviewPanel was not found.");
            editorModeButton = this.FindControl<Button>("EditorModeButton")
                ?? throw new InvalidOperationException("EditorModeButton was not found.");
            editorModeIcon = this.FindControl<Material.Icons.Avalonia.MaterialIcon>("EditorModeIcon")
                ?? throw new InvalidOperationException("EditorModeIcon was not found.");

            Grid contentGrid = (Grid)editorPanel.Parent!;
            editorColumn = contentGrid.ColumnDefinitions[0];
            previewColumn = contentGrid.ColumnDefinitions[1];

            previewWebView.WebMessageReceived += HandlePreviewMessage;
            previewWebView.NavigationCompleted += (_, _) => ScrollPreviewToCaretLine();
            resizeGrip.AddHandler(Thumb.PointerPressedEvent, HandleResizeGripPressed, RoutingStrategies.Tunnel);

            // Debounce caret moves: editing/cursor changes fire rapidly, but each preview scroll is a
            // WebView script call. Coalesce to the latest line on a short timer before invoking JS.
            caretSyncTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            caretSyncTimer.Tick += HandleCaretSyncTick;
            editorTextBox.PropertyChanged += HandleEditorPropertyChanged;

            DragDrop.SetAllowDrop(editorTextBox, true);
            editorTextBox.AddHandler(DragDrop.DragOverEvent, HandleEditorDragOver);
            editorTextBox.AddHandler(DragDrop.DropEvent, HandleEditorDrop);
            editorTextBox.AddHandler(KeyDownEvent, HandleEditorKeyDown, RoutingStrategies.Tunnel);

            editorHotkeys = new EditorHotkeyController(keybindings, new Dictionary<HotkeyAction, Action>
            {
                [HotkeyAction.InsertImage] = () => _ = PickImageAsync(),
                [HotkeyAction.InsertAttachment] = () => _ = PickAttachmentAsync(),
                [HotkeyAction.CycleEditorMode] = CycleEditorMode,
                [HotkeyAction.CycleWindowLevel] = CycleWindowLevel,
                [HotkeyAction.QuickLayout] = () => _ = ShowQuickLayoutOverlay()
            });
            AddHandler(KeyDownEvent, HandleWindowKeyDown, RoutingStrategies.Tunnel);

            DataContext = viewModel;
            Loaded += HandleLoaded;
            Closing += HandleClosing;
            Resized += (_, _) => PersistCurrentBounds();
            viewModel.PreviewRequested += (_, _) => RefreshPreview();
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(NoteWindowViewModel.Level))
                {
                    ApplyWindowLevel();
                    RefreshTaskbarVisibility();
                }
            };

            ConfigureLevelSelector();
            ApplyInitialWindowState();
        }

        private static NoteWindowViewModel CreateDefaultViewModel()
        {
            NoteRepository repository = new NoteRepository();
            AvaloniaNoteDocument note = repository.CreateNote();
            ReminderScheduler reminders = new ReminderScheduler(new UnsupportedNotificationService());
            return new NoteWindowViewModel(note, repository, reminders);
        }

        private void HandleWindowKeyDown(object? sender, KeyEventArgs e)
        {
            if (editorHotkeys.Handle(e))
            {
                e.Handled = true;
            }
        }

        private void HandleLoaded(object? sender, RoutedEventArgs e)
        {
            RefreshPreview();
            ApplyWindowLevel();
        }

        private void HandleClosing(object? sender, WindowClosingEventArgs e)
        {
            viewModel.UpdateBounds(Position.X, Position.Y, Width, Height);
            viewModel.SetOpen(false);
        }

        /// <summary>
        /// Applies content changed outside the editor (e.g. a fire-once reminder disabling itself) so
        /// the editor text and preview update live. Routes through the view model, which persists and
        /// reschedules. No-op when the content is unchanged.
        /// </summary>
        /// <param name="content">The new note content.</param>
        public void ApplyExternalContent(string content)
        {
            viewModel.Content = content;
        }

        private void ConfigureLevelSelector()
        {
            List<WindowLevel> levels = new List<WindowLevel> { WindowLevel.Normal, WindowLevel.TopMost };
            if (windowLevels.SupportsBottomMost)
            {
                levels.Add(WindowLevel.BottomMost);
            }

            supportedLevels = levels;
            levelSelector.ItemsSource = levels;
            levelSelector.SelectedItem = levels.Contains(viewModel.Level) ? viewModel.Level : WindowLevel.Normal;
            levelSelector.SelectionChanged += (_, _) =>
            {
                if (levelSelector.SelectedItem is WindowLevel level)
                {
                    viewModel.Level = level;
                }
            };
        }

        /// <summary>
        /// Advances the window stacking level to the next supported level, wrapping around. Updates
        /// the selector, which routes the change through the view model.
        /// </summary>
        private void CycleWindowLevel()
        {
            if (supportedLevels.Count == 0)
            {
                return;
            }

            int current = supportedLevels.IndexOf(viewModel.Level);
            WindowLevel next = supportedLevels[(current + 1) % supportedLevels.Count];
            levelSelector.SelectedItem = next;
        }

        private void ApplyInitialWindowState()
        {
            Position = new Avalonia.PixelPoint((int)viewModel.Note.Left, (int)viewModel.Note.Top);
            Width = viewModel.Note.Width;
            Height = viewModel.Note.Height;
            RefreshTaskbarVisibility();

            Border titleBar = this.FindControl<Border>("TitleBar")
                ?? throw new InvalidOperationException("TitleBar was not found.");
            titleBar.PointerPressed += HandleTitleBarPointerPressed;

            SetDisplayMode(viewModel.DisplayMode, adjustWidth: false);
        }

        private void ApplyWindowLevel()
        {
            windowLevels.Apply(this, viewModel.Level);
        }

        private void RefreshPreview()
        {
            PreviewStyleManager.EnsureInitialized();
            string stylePath = PreviewStyleManager.ToStyleAbsolutePath(PreviewStyleManager.DefaultStyleRelativePath);
            string styleHref = new Uri(stylePath).AbsoluteUri;
            string baseHref = new Uri(AppPaths.DataDirectory + Path.DirectorySeparatorChar).AbsoluteUri;
            string html = MarkdownPreviewDocument.Render(viewModel.Content, styleHref, baseHref);
            string htmlPath = AppPaths.GetNoteHtmlCachePath(viewModel.NoteId);
            string? directory = Path.GetDirectoryName(htmlPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(htmlPath, html);
            previewWebView.Navigate(new Uri(htmlPath));
        }

        private void HandleEditorPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
        {
            // Re-sync the preview when the caret moves or the text changes (text edits shift the caret's
            // line even when CaretIndex stays put, e.g. inserting a line above).
            if (e.Property == TextBox.CaretIndexProperty || e.Property == TextBox.TextProperty)
            {
                OnCaretMoved();
            }
        }

        /// <summary>
        /// Recomputes the caret's 0-based line from the editor text and restarts the debounce timer.
        /// The actual preview scroll happens on the timer tick so rapid caret moves collapse into one
        /// WebView script call.
        /// </summary>
        private void OnCaretMoved()
        {
            string text = editorTextBox.Text ?? string.Empty;
            int caret = Math.Clamp(editorTextBox.CaretIndex, 0, text.Length);

            caretLine = LineForOffset(text, caret);
            caretSyncTimer.Stop();
            caretSyncTimer.Start();
        }

        /// <summary>
        /// Counts the 0-based source line containing the character at <paramref name="offset"/> by
        /// counting newlines before it. Shared by caret-sync and reminder scroll-to so both map a
        /// character offset to the same line the preview annotates with <c>data-source-line</c>.
        /// </summary>
        /// <param name="text">The note content.</param>
        /// <param name="offset">A character offset into <paramref name="text"/>.</param>
        /// <returns>The 0-based line number.</returns>
        private static int LineForOffset(string text, int offset)
        {
            int clamped = Math.Clamp(offset, 0, text.Length);
            int line = 0;
            for (int i = 0; i < clamped; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        /// <summary>
        /// Brings the window to the foreground and scrolls the preview to the block at the given
        /// 0-based source line. Used when a reminder fires to reveal its location in the note.
        /// </summary>
        /// <param name="line">The 0-based source line to scroll into view.</param>
        public void ScrollToSourceLine(int line)
        {
            caretLine = Math.Max(0, line);
            ScrollPreviewToCaretLine();
        }

        /// <summary>
        /// Brings the window to the foreground and scrolls the preview to the block containing the
        /// character at <paramref name="sourceOffset"/>. Convenience over <see cref="ScrollToSourceLine"/>
        /// for callers that hold a content offset (e.g. an inline reminder rule's source start).
        /// </summary>
        /// <param name="sourceOffset">A character offset into the note content.</param>
        public void ScrollToSourceOffset(int sourceOffset)
        {
            ScrollToSourceLine(LineForOffset(viewModel.Content, sourceOffset));
        }

        private void HandleCaretSyncTick(object? sender, EventArgs e)
        {
            caretSyncTimer.Stop();
            ScrollPreviewToCaretLine();
        }

        /// <summary>
        /// Asks the preview to scroll the block matching the caret's source line into view. Invoked on
        /// the debounce tick and after each navigation completes (the reloaded document re-defines the
        /// scroll function). Failures are non-fatal: the WebView may be mid-teardown or not yet ready.
        /// </summary>
        private async void ScrollPreviewToCaretLine()
        {
            try
            {
                await previewWebView.InvokeScript($"window.__scrollToSourceLine && window.__scrollToSourceLine({caretLine})")
                    .ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ObjectDisposedException)
            {
                AppLogger.Debug($"Preview scroll-sync skipped: {ex.Message}");
            }
        }

        private void HandleTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Only start a window move when the press lands on the bar surface itself. Without this
            // guard the drag loop swallows clicks meant for the level selector, checkbox and buttons
            // hosted in the title bar, so the level selector never opens or applies a choice.
            if (e.Source is not Control source || IsInteractiveChromeControl(source))
            {
                return;
            }

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }

        private static bool IsInteractiveChromeControl(Control source)
        {
            Control? current = source;
            while (current is not null)
            {
                // CheckBox derives from Button in Avalonia, so the Button check covers it too.
                if (current is Button or ComboBox or Thumb)
                {
                    return true;
                }

                if (current is Border { Name: "TitleBar" })
                {
                    return false;
                }

                current = current.Parent as Control;
            }

            return false;
        }

        private void HandleEditorModeClick(object? sender, RoutedEventArgs e)
        {
            CycleEditorMode();
        }

        /// <summary>
        /// Advances the editor display mode in the order Preview → Text → Split → Preview.
        /// </summary>
        private void CycleEditorMode()
        {
            EditorDisplayMode next = viewModel.DisplayMode switch
            {
                EditorDisplayMode.PreviewOnly => EditorDisplayMode.TextOnly,
                EditorDisplayMode.TextOnly => EditorDisplayMode.TextAndPreview,
                _ => EditorDisplayMode.PreviewOnly
            };

            SetDisplayMode(next, adjustWidth: true);
        }

        private void SetDisplayMode(EditorDisplayMode mode, bool adjustWidth)
        {
            EditorDisplayMode previous = viewModel.DisplayMode;
            bool showEditor = mode is EditorDisplayMode.TextOnly or EditorDisplayMode.TextAndPreview;
            bool showPreview = mode is EditorDisplayMode.PreviewOnly or EditorDisplayMode.TextAndPreview;

            editorPanel.IsVisible = showEditor;
            previewPanel.IsVisible = showPreview;
            editorColumn.Width = showEditor ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            previewColumn.Width = showPreview ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

            editorModeIcon.Kind = mode switch
            {
                EditorDisplayMode.PreviewOnly => Material.Icons.MaterialIconKind.EyeOutline,
                EditorDisplayMode.TextOnly => Material.Icons.MaterialIconKind.PencilOutline,
                _ => Material.Icons.MaterialIconKind.ViewSplitVertical
            };

            ToolTip.SetTip(editorModeButton, GetLocalizedTitle(mode switch
            {
                EditorDisplayMode.PreviewOnly => "Window.EditorMode.PreviewOnly",
                EditorDisplayMode.TextOnly => "Window.EditorMode.TextOnly",
                _ => "Window.EditorMode.TextAndPreview"
            }));

            viewModel.DisplayMode = mode;

            if (adjustWidth && WindowState == WindowState.Normal)
            {
                AdjustWidthForMode(mode, previous);
            }
        }

        private void AdjustWidthForMode(EditorDisplayMode mode, EditorDisplayMode previous)
        {
            bool entering = mode == EditorDisplayMode.TextAndPreview && previous != EditorDisplayMode.TextAndPreview;
            bool leaving = previous == EditorDisplayMode.TextAndPreview && mode != EditorDisplayMode.TextAndPreview;
            double scaling = RenderScaling <= 0 ? 1.0 : RenderScaling;

            if (entering)
            {
                savedLeftPhysical = Position.X;
                savedWidthDip = Width;
                double maxWidthDip = GetWorkAreaWidthDip(scaling);
                EditorModeLayout.ModeBounds bounds = EditorModeLayout.ExpandLeft(
                    Position.X, Width, scaling, MinWidth, maxWidthDip);
                ApplyModeBounds(bounds);
                return;
            }

            if (leaving)
            {
                EditorModeLayout.ModeBounds bounds = double.IsNaN(savedWidthDip)
                    ? EditorModeLayout.Collapse(Position.X, Width, scaling, MinWidth)
                    : EditorModeLayout.Restore(savedLeftPhysical, savedWidthDip);
                ApplyModeBounds(bounds);
                savedLeftPhysical = double.NaN;
                savedWidthDip = double.NaN;
            }
        }

        private void ApplyModeBounds(EditorModeLayout.ModeBounds bounds)
        {
            Position = new Avalonia.PixelPoint((int)Math.Round(bounds.LeftPhysical), Position.Y);
            Width = bounds.WidthDip;
            PersistCurrentBounds();
        }

        private double GetWorkAreaWidthDip(double scaling)
        {
            const double margin = 20;
            Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            double workWidthPhysical = screen?.WorkingArea.Width ?? (Width * scaling);
            return Math.Max(MinWidth, workWidthPhysical / scaling - margin);
        }

        private void HandlePreviewMessage(object? sender, Avalonia.Controls.WebMessageReceivedEventArgs e)
        {
            if (e.Body == MarkdownPreviewDocument.DoubleRightClickMessage)
            {
                editorTextBox.Focus();
                return;
            }

            if (e.Body is { } body && body.StartsWith(MarkdownPreviewDocument.OpenLinkMessagePrefix, StringComparison.Ordinal))
            {
                string target = body[MarkdownPreviewDocument.OpenLinkMessagePrefix.Length..];
                if (!SystemShellLauncher.Open(target))
                {
                    AppLogger.Warn($"Failed to open preview link '{target}' in the default application.");
                }
            }
        }

        private void HandleCloseClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void HandleQuickLayoutClick(object? sender, RoutedEventArgs e)
        {
            await ShowQuickLayoutOverlay().ConfigureAwait(true);
        }

        /// <summary>
        /// Opens the quick-layout overlay for this window and applies the chosen bounds.
        /// </summary>
        public async Task ShowQuickLayoutOverlay()
        {
            QuickLayoutOverlayWindow overlay = new QuickLayoutOverlayWindow(Width, Height);
            WindowRect? bounds = await overlay.ShowDialog<WindowRect?>(this).ConfigureAwait(true);
            if (bounds is not null)
            {
                quickLayout.ApplyBounds(this, bounds);
                PersistCurrentBounds();
            }
        }

        private async void HandleSetReminderClick(object? sender, RoutedEventArgs e)
        {
            ReminderDialog dialog = new ReminderDialog(viewModel.ReminderAt);
            ReminderDialogResult? result = await dialog.ShowDialog<ReminderDialogResult?>(this).ConfigureAwait(true);
            if (result is not null)
            {
                viewModel.ReminderAt = result.ReminderAt;
            }
        }

        private async void HandleInsertImageClick(object? sender, RoutedEventArgs e)
        {
            await PickImageAsync().ConfigureAwait(true);
        }

        private async Task PickImageAsync()
        {
            FilePickerFileType imageFileType = new FilePickerFileType("Images")
            {
                Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp" }
            };

            await PickAndInsertAsync(new FilePickerOpenOptions
            {
                Title = GetLocalizedTitle("Editor.InsertImage"),
                AllowMultiple = false,
                FileTypeFilter = new[] { imageFileType }
            }).ConfigureAwait(true);
        }

        private async void HandleInsertAttachmentClick(object? sender, RoutedEventArgs e)
        {
            await PickAttachmentAsync().ConfigureAwait(true);
        }

        private async Task PickAttachmentAsync()
        {
            await PickAndInsertAsync(new FilePickerOpenOptions
            {
                Title = GetLocalizedTitle("Editor.InsertAttachment"),
                AllowMultiple = false,
                FileTypeFilter = new[] { FilePickerFileTypes.All }
            }).ConfigureAwait(true);
        }

        private async Task PickAndInsertAsync(FilePickerOpenOptions options)
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(options).ConfigureAwait(true);
            foreach (IStorageFile file in files)
            {
                string? path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                {
                    InsertFile(path);
                }
            }
        }

        private void HandleEditorDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private void HandleEditorDrop(object? sender, DragEventArgs e)
        {
            if (!e.DataTransfer.Contains(DataFormat.File))
            {
                return;
            }

            if (InsertStorageItems(e.DataTransfer.TryGetFiles()))
            {
                e.Handled = true;
            }
        }

        private async void HandleEditorKeyDown(object? sender, KeyEventArgs e)
        {
            bool isPasteShortcut = e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (!isPasteShortcut)
            {
                return;
            }

            IClipboard? clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            IAsyncDataTransfer? data = await clipboard.TryGetDataAsync().ConfigureAwait(true);
            if (data is null || !data.Contains(DataFormat.File))
            {
                return;
            }

            IStorageItem[]? items = await data.TryGetFilesAsync().ConfigureAwait(true);

            // Suppress the default text paste only when files were inserted; otherwise allow normal paste.
            if (InsertStorageItems(items))
            {
                e.Handled = true;
            }
        }

        private bool InsertStorageItems(IEnumerable<IStorageItem>? items)
        {
            if (items is null)
            {
                return false;
            }

            bool insertedAny = false;
            foreach (IStorageItem item in items)
            {
                if (item is not IStorageFile)
                {
                    continue;
                }

                string? path = item.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                {
                    InsertFile(path);
                    insertedAny = true;
                }
            }

            return insertedAny;
        }

        private void InsertFile(string sourceFilePath)
        {
            try
            {
                (bool autoSyncEnabled, long thresholdBytes) = ReadAttachmentPolicy();
                string snippet = NoteAssetInserter.BuildSnippet(viewModel.NoteId, sourceFilePath, autoSyncEnabled, thresholdBytes);
                InsertSnippetAtCaret(snippet);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException or ArgumentException)
            {
                AppLogger.Warn($"Failed to insert file '{sourceFilePath}': {ex.Message}");
                _ = ShowErrorAsync(ex.Message);
            }
        }

        private (bool autoSyncEnabled, long thresholdBytes) ReadAttachmentPolicy()
        {
            string enabledRaw = settings.GetValue(SettingsUi.SettingsSchemaBuilder.AttachmentAutoSyncEnabledKey, shouldSync: true, "true");
            bool autoSyncEnabled = !bool.TryParse(enabledRaw, out bool parsedEnabled) || parsedEnabled;

            string thresholdRaw = settings.GetValue(
                SettingsUi.SettingsSchemaBuilder.AttachmentThresholdMbKey,
                shouldSync: true,
                SettingsUi.SettingsSchemaBuilder.DefaultAttachmentThresholdMb.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!int.TryParse(thresholdRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int thresholdMb))
            {
                thresholdMb = SettingsUi.SettingsSchemaBuilder.DefaultAttachmentThresholdMb;
            }

            thresholdMb = Math.Clamp(thresholdMb, 1, 1024);
            return (autoSyncEnabled, (long)thresholdMb * 1024 * 1024);
        }

        private void InsertSnippetAtCaret(string snippet)
        {
            string current = editorTextBox.Text ?? string.Empty;
            int caretIndex = Math.Clamp(editorTextBox.CaretIndex, 0, current.Length);
            string updated = current.Insert(caretIndex, snippet);

            // Route through the view model so the change persists and re-renders the preview.
            viewModel.Content = updated;
            editorTextBox.CaretIndex = caretIndex + snippet.Length;
            editorTextBox.Focus();
        }

        private async Task ShowErrorAsync(string message)
        {
            await MessageBoxManager
                .GetMessageBoxStandard("Error", message, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error)
                .ShowWindowDialogAsync(this)
                .ConfigureAwait(true);
        }

        private static string GetLocalizedTitle(string key)
        {
            return YASN.Localization.LocalizationService.Current[key];
        }

        private async void HandleRenameClick(object? sender, RoutedEventArgs e)
        {
            RenameDialog dialog = new RenameDialog(
                viewModel.Title,
                proposed => viewModel.TryRename(proposed, out string? errorKey) ? null : errorKey,
                YASN.Localization.LocalizationService.Current);

            await dialog.ShowDialog<string?>(this).ConfigureAwait(true);
        }

        /// <summary>
        /// Re-reads the global taskbar-visibility mode and applies it to this window for its level.
        /// </summary>
        public void RefreshTaskbarVisibility()
        {
            string raw = settings.GetValue(TaskbarVisibility.SettingKey, shouldSync: true, TaskbarVisibility.AlwaysHideValue);
            TaskbarVisibilityMode mode = TaskbarVisibility.ParseMode(raw);
            ShowInTaskbar = TaskbarVisibility.ShouldShowInTaskbar(viewModel.Level, mode);
        }

        private void HandleResizeGripPressed(object? sender, PointerPressedEventArgs e)
        {
            // Mirrors the WPF ResizeThumb: drag the bottom-right corner to resize the window. The
            // native resize loop enforces MinWidth/MinHeight and reflows the editor and preview live.
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginResizeDrag(WindowEdge.SouthEast, e);
            }
        }

        private void PersistCurrentBounds()
        {
            viewModel.UpdateBounds(Position.X, Position.Y, Width, Height);
        }

        /// <summary>
        /// Stores a callback that opens or activates the main note-manager window. The callback
        /// handles creation of the window when it does not already exist, so the note window never
        /// needs a direct reference to <see cref="MainWindow"/>.
        /// </summary>
        /// <param name="action">
        /// The action that opens the note manager, or <see langword="null"/> when unavailable.
        /// </param>
        public void SetOpenMainWindowAction(Action? action)
        {
            openMainWindowAction = action;
        }

        private void MenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            if (openMainWindowAction is not null)
            {
                openMainWindowAction();
            }
            else
            {
                AppLogger.Warn("Failed to invoke manage windows");
            }
        }
    }
}
