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
    /// <summary>
    /// Contains markdown preview rendering, style watching, and preview surface interaction logic.
    /// </summary>
    public partial class FloatingWindow
    {
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
                    string csproj = Path.Combine(current.FullName, "src", "YASN.App", "YASN.App.csproj");
                    if (!File.Exists(csproj))
                    {
                        continue;
                    }

                    string candidate = Path.Combine(current.FullName, "src", "YASN.App", "style", relativeStylePath.Replace('/', Path.DirectorySeparatorChar));
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
    }
}