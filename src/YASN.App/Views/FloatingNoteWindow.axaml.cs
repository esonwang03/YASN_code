using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using YASN.AvaloniaNotes;
using YASN.Core;
using YASN.Hotkeys;
using YASN.Infrastructure;
using YASN.Infrastructure.Markdown;
using YASN.MarkdownEditing;
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
    public sealed partial class FloatingNoteWindow : Window, ILiveNoteContentEditor
    {
        private readonly NoteWindowViewModel viewModel;
        private readonly IWindowLevelController windowLevels;
        private readonly IQuickWindowLayoutController quickLayout;
        private readonly Infrastructure.Settings.SettingsStore settings;
        private readonly NativeWebView previewWebView;
        private readonly ComboBox levelSelector;
        private readonly TextEditor editorTextEditor;
        private readonly ColumnDefinition editorColumn;
        private readonly ColumnDefinition previewColumn;
        private readonly Control editorPanel;
        private readonly Control previewPanel;
        private readonly Button editorModeButton;
        private readonly Material.Icons.Avalonia.MaterialIcon editorModeIcon;
        private readonly EditorHotkeyController editorHotkeys;
        private CompletionWindow? completionWindow;
        private List<WindowLevel> supportedLevels = new();
        private double savedLeftPhysical = double.NaN;
        private double savedWidthDip = double.NaN;
        private Action? openMainWindowAction;
        private readonly Avalonia.Threading.DispatcherTimer caretSyncTimer;
        private int caretLine;

        // Guards the document<->view-model mirror against re-entrancy: when an edit pushes new text into
        // the document, its TextChanged fires and writes viewModel.Content; this flag stops that write
        // from bouncing back into the document. Set only while applying a document edit on the UI thread.
        private bool applyingDocumentEdit;

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
            editorTextEditor = this.FindControl<TextEditor>("EditorTextEditor")
                ?? throw new InvalidOperationException("EditorTextEditor was not found.");
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
            previewWebView.EnvironmentRequested += HandlePreviewEnvironmentRequested;
            previewWebView.NavigationCompleted += (_, _) => ScrollPreviewToCaretLine(onlyIfOffscreen: false, smooth: false);
            resizeGrip.AddHandler(Thumb.PointerPressedEvent, HandleResizeGripPressed, RoutingStrategies.Tunnel);

            // Debounce caret moves: editing/cursor changes fire rapidly, but each preview scroll is a
            // WebView script call. Coalesce to the latest line on a short timer before invoking JS.
            caretSyncTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            caretSyncTimer.Tick += HandleCaretSyncTick;
            editorTextEditor.Text = viewModel.Content;
            editorTextEditor.TextChanged += HandleEditorTextChanged;
            editorTextEditor.TextArea.Caret.PositionChanged += HandleEditorCaretPositionChanged;

            DragDrop.SetAllowDrop(editorTextEditor, true);
            editorTextEditor.AddHandler(DragDrop.DragOverEvent, HandleEditorDragOver);
            editorTextEditor.AddHandler(DragDrop.DropEvent, HandleEditorDrop);
            editorTextEditor.AddHandler(KeyDownEvent, HandleEditorKeyDown, RoutingStrategies.Tunnel);
            editorTextEditor.AddHandler(TextInputEvent, HandleEditorTextInput, RoutingStrategies.Tunnel);

            editorHotkeys = new EditorHotkeyController(keybindings, new Dictionary<HotkeyAction, Action>
            {
                [HotkeyAction.InsertImage] = () => _ = PickImageAsync(),
                [HotkeyAction.InsertAttachment] = () => _ = PickAttachmentAsync(),
                [HotkeyAction.CycleEditorMode] = CycleEditorMode,
                [HotkeyAction.CycleWindowLevel] = CycleWindowLevel,
                [HotkeyAction.QuickLayout] = () => _ = ShowQuickLayoutOverlay(),
                [HotkeyAction.ToggleChrome] = ToggleChrome
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

                if (args.PropertyName == nameof(NoteWindowViewModel.Content)
                    && !applyingDocumentEdit
                    && editorTextEditor.Document is { } doc
                    && doc.Text != viewModel.Content)
                {
                    // A direct view-model content write (not originating from a document edit) is mirrored
                    // back as a minimal splice so the editor stays in step without a full-text reset that
                    // would clear undo and move the caret.
                    MarkdownTextSplice splice = MarkdownTextDiff.Compute(doc.Text, viewModel.Content);
                    if (!splice.IsNoOp)
                    {
                        doc.Replace(splice.Offset, splice.RemovedLength, splice.InsertedText);
                    }
                }
            };

            ConfigureLevelSelector();
            ApplyConfiguredMinWidth();
            ApplyInitialWindowState();
            InitializeChromeCollapse();
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
            // Esc from any editing mode reverts to preview, giving the keyboard a quick "done editing"
            // exit that mirrors the QuickLayout overlay's Esc-to-cancel. Bound directly rather than
            // through the registry since it is a fixed, non-rebindable key.
            if (e.Key == Key.Escape && viewModel.DisplayMode is EditorDisplayMode.TextOnly or EditorDisplayMode.TextAndPreview)
            {
                SetDisplayMode(EditorDisplayMode.PreviewOnly, adjustWidth: true);
                e.Handled = true;
                return;
            }

            if (editorHotkeys.Handle(e))
            {
                e.Handled = true;
            }
        }

        private void HandleLoaded(object? sender, RoutedEventArgs e)
        {
            RefreshPreview();
            ApplyWindowLevel();
            BeginChromeAutoCollapse();
        }

        private void HandleClosing(object? sender, WindowClosingEventArgs e)
        {
            viewModel.UpdateBounds(Position.X, Position.Y, Width, Height);
            viewModel.SetOpen(false);
        }

        /// <summary>
        /// Gets the note's current live content: the editor document text, including unsaved edits.
        /// </summary>
        public string LiveContent => editorTextEditor.Document?.Text ?? viewModel.Content;

        /// <summary>
        /// Replaces the entire live content as a single minimal document edit, so an externally-authored
        /// update (e.g. a fire-once reminder disabling itself) refreshes the editor and preview live,
        /// stays on the undo stack, and keeps the caret where it was. No-op when unchanged.
        /// </summary>
        /// <param name="content">The new note content.</param>
        public void ReplaceAll(string content)
        {
            ApplyContentSplice(content ?? string.Empty);
        }

        /// <summary>
        /// Applies a transform to the live note content as a single minimal document edit, so a
        /// programmatic edit composes with the current (possibly unsaved) editor text, is individually
        /// undoable, and preserves the caret/selection outside the changed span. A <see langword="null"/>
        /// result is a no-op.
        /// </summary>
        /// <param name="transform">The content transform; returning <see langword="null"/> is a no-op.</param>
        /// <returns><see langword="true"/> when a change was applied.</returns>
        public bool ApplyTransform(Func<string, string?> transform)
        {
            if (transform(LiveContent) is { } updated)
            {
                return ApplyContentSplice(updated);
            }

            return false;
        }

        /// <summary>
        /// Diffs the desired text against the current document and applies only the changed span as one
        /// <see cref="AvaloniaEdit.Document.TextDocument.Replace(int, int, string)"/>. This is the single
        /// funnel every whole-string content mutation flows through; the document's TextChanged then
        /// mirrors the result into the view model. Falls back to assigning the view model directly when
        /// the document is not yet available.
        /// </summary>
        /// <param name="desired">The full content the document should hold after the edit.</param>
        /// <returns><see langword="true"/> when the document text changed.</returns>
        private bool ApplyContentSplice(string desired)
        {
            AvaloniaEdit.Document.TextDocument? document = editorTextEditor.Document;
            if (document is null)
            {
                // No editor document yet (pre-load); route straight to the view model.
                if (viewModel.Content == desired)
                {
                    return false;
                }

                viewModel.Content = desired;
                return true;
            }

            MarkdownTextSplice splice = MarkdownTextDiff.Compute(document.Text, desired);
            if (splice.IsNoOp)
            {
                return false;
            }

            // Edit only the changed span: one undo step, caret/selection outside the span preserved. The
            // document's TextChanged then mirrors the new text into the view model (persist, reschedule,
            // preview); the guard stops that mirror from bouncing back through the PropertyChanged handler.
            applyingDocumentEdit = true;
            try
            {
                document.Replace(splice.Offset, splice.RemovedLength, splice.InsertedText);
            }
            finally
            {
                applyingDocumentEdit = false;
            }

            return true;
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
            string configuredStyle = settings.GetValue(PreviewStyleManager.SettingKey, shouldSync: true, PreviewStyleManager.DefaultStyleRelativePath);
            string stylePath = PreviewStyleManager.ToStyleAbsolutePath(PreviewStyleManager.ResolveStyle(configuredStyle));
            string styleHref = new Uri(stylePath).AbsoluteUri;
            string baseHref = new Uri(AppPaths.DataDirectory + Path.DirectorySeparatorChar).AbsoluteUri;

            // KaTeX is vendored under the data-dir style folder (materialized from the bundle). Pass its
            // folder as an absolute file URI so math renders offline; empty if the assets are missing so
            // the preview still works (math falls back to source text).
            string katexDir = Path.Combine(AppPaths.StyleRoot, "katex");
            string katexBaseHref = File.Exists(Path.Combine(katexDir, "katex.min.js"))
                ? new Uri(katexDir + Path.DirectorySeparatorChar).AbsoluteUri
                : string.Empty;
            string html = MarkdownPreviewDocument.Render(viewModel.Content, styleHref, baseHref, katexBaseHref);
            string htmlPath = AppPaths.GetNoteHtmlCachePath(viewModel.NoteId);
            string? directory = Path.GetDirectoryName(htmlPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(htmlPath, html);
            previewWebView.Navigate(new Uri(htmlPath));
        }

        /// <summary>
        /// Pushes AvaloniaEdit text changes into the view model and refreshes caret-preview sync.
        /// </summary>
        private void HandleEditorTextChanged(object? sender, EventArgs e)
        {
            if (viewModel.Content != editorTextEditor.Text)
            {
                viewModel.Content = editorTextEditor.Text;
            }

            OnCaretMoved();
        }

        /// <summary>
        /// Refreshes caret-preview sync when AvaloniaEdit moves the caret without changing text.
        /// </summary>
        private void HandleEditorCaretPositionChanged(object? sender, EventArgs e)
        {
            OnCaretMoved();
        }

        /// <summary>
        /// Recomputes the caret's 0-based line from the editor text and restarts the debounce timer.
        /// The actual preview scroll happens on the timer tick so rapid caret moves collapse into one
        /// WebView script call.
        /// </summary>
        private void OnCaretMoved()
        {
            string text = editorTextEditor.Text ?? string.Empty;
            int caret = Math.Clamp(editorTextEditor.CaretOffset, 0, text.Length);

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
            // A fired reminder should reveal its location wherever it is, smoothly.
            ScrollPreviewToCaretLine(onlyIfOffscreen: false, smooth: true);
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

        /// <summary>
        /// Focuses the text editor with the caret on the given 0-based source line so the user can edit
        /// the token there (e.g. a reminder rule from the manager, or a double-clicked preview block).
        /// Switches out of preview-only mode first when needed so the editor is visible, then scrolls
        /// the target line to the vertical center — AvaloniaEdit does not auto-scroll on a caret-offset
        /// change, and centering keeps the clicked block in context rather than pinned to an edge.
        /// </summary>
        /// <param name="line">The 0-based source line to place the caret on.</param>
        public void FocusEditorAtLine(int line)
        {
            if (viewModel.DisplayMode == EditorDisplayMode.PreviewOnly)
            {
                SetDisplayMode(EditorDisplayMode.TextAndPreview, adjustWidth: true);
            }

            string text = viewModel.Content;
            int offset = OffsetForLine(text, Math.Max(0, line));
            editorTextEditor.CaretOffset = offset;

            // ScrollTo takes a 1-based line and centers it (VisualYPosition.LineMiddle at half the
            // viewport height); the source line here is 0-based, so add one. Out-of-range lines are
            // clamped internally.
            editorTextEditor.ScrollTo(Math.Max(0, line) + 1, column: -1);
            editorTextEditor.Focus();
        }

        /// <summary>
        /// Returns the character offset of the start of the given 0-based line, the inverse of
        /// <see cref="LineForOffset"/>. Clamps to the end of the text when the line is past the content.
        /// </summary>
        /// <param name="text">The note content.</param>
        /// <param name="line">The 0-based line whose start offset is wanted.</param>
        /// <returns>The character offset of the line start.</returns>
        private static int OffsetForLine(string text, int line)
        {
            if (line <= 0)
            {
                return 0;
            }

            int seen = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    seen++;
                    if (seen == line)
                    {
                        return i + 1;
                    }
                }
            }

            return text.Length;
        }

        private void HandleCaretSyncTick(object? sender, EventArgs e)
        {
            caretSyncTimer.Stop();
            // While typing, only nudge the preview when the caret line scrolled out of view, and
            // animate it so it reads as a follow rather than a jump.
            ScrollPreviewToCaretLine(onlyIfOffscreen: true, smooth: true);
        }

        /// <summary>
        /// Asks the preview to align the block matching the caret's source line. Invoked on the debounce
        /// tick (only-if-offscreen, smooth) and after each navigation completes (unconditional instant
        /// jump, so a re-render lands on the caret line without animating). Failures are non-fatal: the
        /// WebView may be mid-teardown or not yet ready.
        /// </summary>
        /// <param name="onlyIfOffscreen">When true, do nothing if the target block is already visible.</param>
        /// <param name="smooth">When true, animate the scroll; otherwise jump instantly.</param>
        private async void ScrollPreviewToCaretLine(bool onlyIfOffscreen, bool smooth)
        {
            try
            {
                string opts = $"{{onlyIfOffscreen:{(onlyIfOffscreen ? "true" : "false")},smooth:{(smooth ? "true" : "false")}}}";
                await previewWebView.InvokeScript($"window.__scrollToSourceLine && window.__scrollToSourceLine({caretLine},{opts})")
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

        private void HandleToggleChromeClick(object? sender, RoutedEventArgs e)
        {
            ToggleChrome();
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

        /// <summary>
        /// When diagnose mode is on, configures the preview's WebView2 environment to enable developer
        /// tools and auto-open a floating DevTools window as the preview loads. Fires once per WebView
        /// environment creation, so a preview opened while diagnose is on gets DevTools; previews opened
        /// earlier pick it up when reopened. Non-Windows backends ignore the Windows-specific args.
        /// </summary>
        private static void HandlePreviewEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
        {
            if (!Diagnostics.DiagnoseMode.IsEnabled)
            {
                return;
            }

            e.EnableDevTools = true;
            if (e is WindowsWebView2EnvironmentRequestedEventArgs windows)
            {
                windows.AdditionalBrowserArguments = "--auto-open-devtools-for-tabs";
            }
        }

        private void HandlePreviewMessage(object? sender, Avalonia.Controls.WebMessageReceivedEventArgs e)
        {
            if (e.Body == MarkdownPreviewDocument.ToggleChromeMessage)
            {
                ToggleChrome();
                return;
            }

            if (e.Body == MarkdownPreviewDocument.DoubleRightClickMessage)
            {
                editorTextEditor.Focus();
                return;
            }

            if (e.Body is { } toggleBody && toggleBody.StartsWith(MarkdownPreviewDocument.TaskToggleMessagePrefix, StringComparison.Ordinal))
            {
                HandleTaskToggle(toggleBody[MarkdownPreviewDocument.TaskToggleMessagePrefix.Length..]);
                return;
            }

            if (e.Body is { } focusBody && focusBody.StartsWith(MarkdownPreviewDocument.FocusEditorLineMessagePrefix, StringComparison.Ordinal))
            {
                string payload = focusBody[MarkdownPreviewDocument.FocusEditorLineMessagePrefix.Length..];
                if (int.TryParse(payload, out int sourceLine) && sourceLine >= 0)
                {
                    FocusEditorAtLine(sourceLine);
                }

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

        /// <summary>
        /// Applies a checkbox toggle from the preview to the live note content. The payload is
        /// <c>&lt;sourceLine&gt;:&lt;0|1&gt;</c>. The clicked line is set as the caret line so the
        /// post-render jump keeps the item in view rather than scrolling away.
        /// </summary>
        /// <param name="payload">The message payload after the toggle prefix.</param>
        private void HandleTaskToggle(string payload)
        {
            int separator = payload.LastIndexOf(':');
            if (separator <= 0
                || !int.TryParse(payload.AsSpan(0, separator), out int sourceLine)
                || sourceLine < 0)
            {
                AppLogger.Warn($"Task toggle ignored: unparseable payload '{payload}'.");
                return;
            }

            bool isChecked = payload[(separator + 1)..] == "1";
            bool applied = ApplyTransform(content =>
                NoteTaskEditor.TrySetChecked(content, sourceLine, isChecked, out string updated) ? updated : null);
            if (applied)
            {
                AppLogger.Debug($"Task toggle: note '{viewModel.NoteId}' line {sourceLine} -> checked={isChecked}.");
                caretLine = sourceLine;
            }
            else
            {
                // No change applied: the reported line is out of range, is not a task item, or already
                // holds the requested state. Most often a stale preview (the source changed after the
                // preview last rendered). Logged so the "click does nothing" reports are diagnosable.
                AppLogger.Warn($"Task toggle no-op: note '{viewModel.NoteId}' line {sourceLine} checked={isChecked} did not match a toggleable task item (possibly a stale preview).");
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
            double scaling = WindowScreenScaling.Get(this);
            QuickLayoutOverlayWindow overlay = new QuickLayoutOverlayWindow(Width, Height, scaling);
            WindowRect? bounds = await overlay.ShowDialog<WindowRect?>(this).ConfigureAwait(true);
            if (bounds is not null)
            {
                quickLayout.ApplyBounds(this, bounds);

                // QuickLayout resized the window to an explicit user choice, so any width saved when the
                // text+preview split was entered is now stale. Drop it: leaving split mode should collapse
                // from this new width (the NaN-means-collapse path in AdjustWidthForMode) rather than snap
                // back to the pre-split width, which left the editor column un-reclaimed.
                savedLeftPhysical = double.NaN;
                savedWidthDip = double.NaN;

                PersistCurrentBounds();
            }
        }

        private async void HandleSetReminderClick(object? sender, RoutedEventArgs e)
        {
            ReminderManagerDialog dialog = new ReminderManagerDialog(
                () => LiveContent,
                updated => ReplaceAll(updated),
                FocusEditorAtLine,
                YASN.Localization.LocalizationService.Current);
            await dialog.ShowDialog(this).ConfigureAwait(true);
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
            KeyModifiers commandModifier = this.GetPlatformSettings()?.HotkeyConfiguration.CommandModifiers
                ?? KeyModifiers.Control;

            if (e.Key == Key.Space && e.KeyModifiers.HasFlag(commandModifier))
            {
                ShowSnippetCompletion();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.B && e.KeyModifiers.HasFlag(commandModifier))
            {
                ApplyMarkdownCommand(MarkdownEditorCommand.Bold);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.I && e.KeyModifiers.HasFlag(commandModifier))
            {
                ApplyMarkdownCommand(MarkdownEditorCommand.Italic);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter
                && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                && MarkdownAutoIndent.TryHandleEnter(
                    RequireEditorDocument(),
                    CurrentEditorSelection(),
                    out MarkdownEditorEdit edit))
            {
                ApplyEditorEdit(edit);
                e.Handled = true;
                return;
            }

            TopLevel? topLevel = GetTopLevel(this);

            // Use the platform's command modifier (Ctrl on Windows/Linux, Cmd/Meta on macOS) rather
            // than hardcoding Control, so Cmd+V triggers the file paste on macOS.
            bool isPasteShortcut = e.Key == Key.V && e.KeyModifiers.HasFlag(commandModifier);
            if (!isPasteShortcut)
            {
                return;
            }

            IClipboard? clipboard = topLevel?.Clipboard;
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

        /// <summary>
        /// Applies the user-configured minimum note width, replacing the XAML default. Governs the
        /// narrowest resize and the lower bound the editor-mode expand/collapse and QuickLayout clamp to.
        /// </summary>
        private void ApplyConfiguredMinWidth()
        {
            string raw = settings.GetValue(
                SettingsUi.SettingsSchemaBuilder.NoteMinWidthKey,
                shouldSync: true,
                SettingsUi.SettingsSchemaBuilder.DefaultNoteMinWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int minWidth))
            {
                minWidth = SettingsUi.SettingsSchemaBuilder.DefaultNoteMinWidth;
            }

            MinWidth = Math.Clamp(minWidth, 360, 1200);
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
            // Insert through the document at the caret like the catalog snippet path, so the insert is one
            // undoable edit that leaves surrounding undo history and the caret intact (the old full-text
            // reset discarded both). Caret lands at the end of the inserted text.
            MarkdownEditorEdit edit = MarkdownEditorCommandService.InsertSnippet(
                RequireEditorDocument(),
                CurrentEditorSelection(),
                new MarkdownSnippet("Insert", snippet, snippet.Length));
            ApplyEditorEdit(edit);
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

        /// <summary>
        /// Re-renders the preview so a changed preview-style setting takes effect on this open window.
        /// </summary>
        public void RefreshPreviewStyle()
        {
            RefreshPreview();
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
            AppLogger.Debug($"Note '{viewModel.NoteId}' bounds: pos=({Position.X},{Position.Y}) sizeDip={Width}x{Height} mode={viewModel.DisplayMode}");
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
