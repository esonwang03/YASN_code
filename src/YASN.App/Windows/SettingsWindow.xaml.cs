using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using YASN.App.Desktop;
using YASN.App.Notes;
using YASN.App.Settings;
using YASN.Infrastructure;
using YASN.Infrastructure.Logging;
using YASN.Infrastructure.Settings;
using YASN.Infrastructure.Sync;
using YASN.Infrastructure.Sync.WebDav;
using Button = System.Windows.Controls.Button;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Point = System.Windows.Point;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace YASN
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsStore _settingsStore = new SettingsStore();
        public SettingsViewModel ViewModel { get; } = new();

        public SettingsWindow()
        {
            InitializeComponent();
            DataContext = ViewModel;
            Loaded += SettingsWindow_Loaded;
            AppLogger.Debug("Settings window initialized");
        }

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            BuildModules();
        }

        private void BuildModules()
        {
            ViewModel.Modules.Clear();
            List<SettingField> allFields = new List<SettingField>();

            GeneralSettingFields generalFields = GeneralSettingsFieldFactory.Create();
            EditorSettingFields editorFields = EditorSettingsFieldFactory.Create();
            WebDavSettingFields webDavFields = WebDavSettingsFieldFactory.Create();

            allFields.Add(generalFields.AutoStartField);
            allFields.Add(generalFields.AutoCollapseNoteChromeField);
            allFields.Add(generalFields.LogSizeField);
            allFields.Add(generalFields.FloatingTaskbarVisibilityField);
            allFields.Add(generalFields.PreviewStyleField);
            allFields.Add(generalFields.DataDirectoryField);
            allFields.Add(editorFields.EnterModeField);
            allFields.Add(webDavFields.ServerUrlField);
            allFields.Add(webDavFields.UserField);
            allFields.Add(webDavFields.PasswordField);
            allFields.Add(webDavFields.RemoteField);
            allFields.Add(webDavFields.SyncIntervalField);
            allFields.Add(webDavFields.AutoSyncField);
            allFields.Add(webDavFields.AttachmentAutoSyncField);
            allFields.Add(webDavFields.AttachmentThresholdField);

            SettingModule generalModule = new SettingModule
            {
                Key = "general",
                Title = "通用",
                Description = "应用通用行为与系统集成设置。"
            };
            generalModule.Fields.Add(generalFields.AutoStartField);
            generalModule.Fields.Add(generalFields.AutoCollapseNoteChromeField);
            generalModule.Fields.Add(generalFields.LogSizeField);
            generalModule.Fields.Add(generalFields.FloatingTaskbarVisibilityField);
            generalModule.Fields.Add(generalFields.PreviewStyleField);
            generalModule.Fields.Add(generalFields.DataDirectoryField);

            SettingModule editorModule = new SettingModule
            {
                Key = "editor",
                Title = "编辑",
                Description = "控制便签编辑行为。"
            };
            editorModule.Fields.Add(editorFields.EnterModeField);

            SettingModule webDavModule = new SettingModule
            {
                Key = "webdav",
                Title = "云同步",
                Description = "配置 WebDAV 以在多设备间同步笔记。"
            };
            webDavModule.Fields.Add(webDavFields.ServerUrlField);
            webDavModule.Fields.Add(webDavFields.UserField);
            webDavModule.Fields.Add(webDavFields.PasswordField);
            webDavModule.Fields.Add(webDavFields.RemoteField);
            webDavModule.Fields.Add(webDavFields.SyncIntervalField);
            webDavModule.Fields.Add(webDavFields.AutoSyncField);
            webDavModule.Fields.Add(webDavFields.AttachmentAutoSyncField);
            webDavModule.Fields.Add(webDavFields.AttachmentThresholdField);

            _settingsStore.ApplyValues(allFields);
            generalFields.FloatingTaskbarVisibilityField.Value =
                FloatingWindowTaskbarVisibility.NormalizeValue(generalFields.FloatingTaskbarVisibilityField.Value);
            bool previewStyleNormalized = ConfigurePreviewStyleField(generalFields.PreviewStyleField);
            string normalizedEditorEnterMode = EditorDisplayModeSettings.ToValue(
                EditorDisplayModeSettings.ParseValue(editorFields.EnterModeField.Value));
            bool editorModeNormalized = !string.Equals(
                editorFields.EnterModeField.Value,
                normalizedEditorEnterMode,
                StringComparison.Ordinal);
            editorFields.EnterModeField.Value = normalizedEditorEnterMode;

            generalFields.AutoStartField.OnChanged = field =>
            {
                HandleAutoStartChanged(field);
                _settingsStore.PersistField(field);
            };
            generalFields.AutoCollapseNoteChromeField.OnChanged = field =>
            {
                _settingsStore.PersistField(field);
                ApplyNoteChromeAutoCollapseToOpenWindows();
            };
            generalFields.LogSizeField.OnChanged = field =>
            {
                ApplyLogSize(field.Value);
                _settingsStore.PersistField(field);
            };
            generalFields.FloatingTaskbarVisibilityField.OnChanged = field =>
            {
                _settingsStore.PersistField(field);
                ApplyFloatingWindowTaskbarVisibilityToOpenWindows();
            };
            generalFields.PreviewStyleField.OnChanged = field =>
            {
                string resolved = PreviewStyleManager.ResolveStyle(field.Value);
                if (!string.Equals(field.Value, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    field.Value = resolved;
                    return;
                }

                _settingsStore.PersistField(field);
                ApplyPreviewStyleToOpenWindows();
                AppLogger.Debug($"Preview style switched to '{resolved}'.");
            };
            if (previewStyleNormalized)
            {
                _settingsStore.PersistField(generalFields.PreviewStyleField);
            }
            editorFields.EnterModeField.OnChanged = field =>
            {
                _settingsStore.PersistField(field);
                AppLogger.Debug($"Editor enter mode set to '{field.Value}'.");
            };
            if (editorModeNormalized)
            {
                _settingsStore.PersistField(editorFields.EnterModeField);
            }

            foreach (SettingField field in new[]
                     {
                         webDavFields.ServerUrlField,
                         webDavFields.UserField,
                         webDavFields.PasswordField,
                         webDavFields.RemoteField,
                         webDavFields.AutoSyncField
                     })
            {
                field.OnChanged = f => _settingsStore.PersistField(f);
            }

            webDavFields.SyncIntervalField.OnChanged = f =>
            {
                _settingsStore.PersistField(f);
                ApplySyncInterval(f.Value);
            };
            webDavFields.AttachmentAutoSyncField.OnChanged = f => _settingsStore.PersistField(f);
            webDavFields.AttachmentThresholdField.OnChanged = f =>
            {
                string normalized = AttachmentSyncSettings.ParseThresholdMb(f.Value).ToString(CultureInfo.InvariantCulture);
                if (!string.Equals(f.Value, normalized, StringComparison.Ordinal))
                {
                    f.Value = normalized;
                    return;
                }

                _settingsStore.PersistField(f);
            };

            ApplyLogSize(generalFields.LogSizeField.Value);
            ApplySyncInterval(webDavFields.SyncIntervalField.Value);
            ApplyFloatingWindowTaskbarVisibilityToOpenWindows();
            ApplyNoteChromeAutoCollapseToOpenWindows();
            ApplyPreviewStyleToOpenWindows();

            generalModule.Actions.Add(new SettingAction
            {
                Key = "settings.export",
                Label = "导出设置",
                ExecuteAsync = () => Task.FromResult(ExportSettings())
            });
            generalModule.Actions.Add(new SettingAction
            {
                Key = "settings.import",
                Label = "导入设置",
                ExecuteAsync = () => Task.FromResult(ImportSettings())
            });
            generalModule.Actions.Add(new SettingAction
            {
                Key = "settings.dataDirectory.browse",
                Label = "浏览数据目录",
                ExecuteAsync = () => Task.FromResult(BrowseDataDirectory(generalFields.DataDirectoryField))
            });
            generalModule.Actions.Add(new SettingAction
            {
                Key = "settings.dataDirectory.apply",
                Label = "应用数据目录",
                ExecuteAsync = () => Task.FromResult(ApplyDataDirectory(generalFields.DataDirectoryField))
            });

            webDavModule.Actions.Add(new SettingAction
            {
                Key = "webdav.test",
                Label = "测试连接",
                ExecuteAsync = async () =>
                {
                    var options = BuildWebDavOptions(webDavFields.ServerUrlField, webDavFields.UserField, webDavFields.PasswordField);
                    using WebDavSyncClient client = new WebDavSyncClient(options);
                    bool ok = await client.TestConnectionAsync(NormalizeDirectory(webDavFields.RemoteField.Value)).ConfigureAwait(false);
                    return ok ? "连接成功" : $"连接失败: {client.LastError ?? "未知错误"}";
                }
            });
            webDavModule.Actions.Add(new SettingAction
            {
                Key = "webdav.save",
                Label = "保存并应用",
                ExecuteAsync = async () =>
                {
                    if (global::YASN.App.App.SyncManager == null)
                    {
                        return "同步管理器未初始化。";
                    }

                    var options = BuildWebDavOptions(webDavFields.ServerUrlField, webDavFields.UserField, webDavFields.PasswordField);
                    WebDavSyncClient client = new WebDavSyncClient(options);
                    bool enabled = webDavFields.AutoSyncField.BoolValue;
                    int intervalSeconds = ParseSyncInterval(webDavFields.SyncIntervalField.Value);
                    ApplySyncInterval(webDavFields.SyncIntervalField.Value);
                    bool configured = await global::YASN.App.App.SyncManager.ConfigureAsync(client, NormalizeDirectory(webDavFields.RemoteField.Value), enabled, intervalSeconds).ConfigureAwait(false);
                    return configured ? "WebDAV 已保存并生效。" : "WebDAV 配置失败。";
                }
            });

            ViewModel.Modules.Add(generalModule);
            ViewModel.Modules.Add(editorModule);
            ViewModel.Modules.Add(webDavModule);
            NavList.SelectedIndex = 0;
        }

        private string ExportSettings()
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Export Settings",
                Filter = "YASN Settings (*.json)|*.json|JSON (*.json)|*.json|All Files (*.*)|*.*",
                FileName = $"yasn-settings-{DateTime.Now:yyyyMMdd-HHmmss}.json"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return "已取消导出。";
            }

            bool ok = _settingsStore.ExportToFile(dialog.FileName, out string? errorMessage);
            return ok ? $"设置已导出到: {dialog.FileName}" : $"导出失败: {errorMessage}";
        }

        private string ImportSettings()
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Import Settings",
                Filter = "YASN Settings (*.json)|*.json|JSON (*.json)|*.json|All Files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
            {
                return "已取消导入。";
            }

            bool ok = _settingsStore.ImportFromFile(dialog.FileName, out string? errorMessage);
            if (!ok)
            {
                return $"导入失败: {errorMessage}";
            }

            BuildModules();
            return "设置已导入并生效。";
        }

        private string ApplyDataDirectory(SettingField dataDirectoryField)
        {
            if (!AppPaths.TryNormalizeDataDirectory(dataDirectoryField.Value, out string? normalizedPath, out string? errorMessage))
            {
                return $"数据目录无效: {errorMessage}";
            }

            dataDirectoryField.Value = normalizedPath;
            _settingsStore.PersistField(dataDirectoryField);
            return $"数据目录已保存为: {normalizedPath}。请重启 YASN 后生效。";
        }

        private string BrowseDataDirectory(SettingField dataDirectoryField)
        {
            if (TryBrowseDataDirectory(dataDirectoryField, out string? selectedPath))
            {
                dataDirectoryField.Value = selectedPath;
                return $"已选择数据目录: {selectedPath}";
            }

            return "已取消选择。";
        }

        private void BrowseDataDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: SettingField field })
            {
                return;
            }

            if (TryBrowseDataDirectory(field, out string? selectedPath))
            {
                field.Value = selectedPath;
            }
        }

        private static bool TryBrowseDataDirectory(SettingField field, out string selectedPath)
        {
            selectedPath = string.Empty;

            string initialPath = AppPaths.DataDirectory;
            if (!string.IsNullOrWhiteSpace(field.Value) && AppPaths.TryNormalizeDataDirectory(field.Value, out string? normalized, out _))
            {
                initialPath = normalized;
            }

            using FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "选择 YASN 数据目录",
                UseDescriptionForTitle = true,
                InitialDirectory = Directory.Exists(initialPath) ? initialPath : AppPaths.BaseDirectory
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                selectedPath = dialog.SelectedPath;
                return true;
            }

            return false;
        }

        private WebDavOptions BuildWebDavOptions(SettingField server, SettingField user, SettingField pass)
        {
            return new WebDavOptions
            {
                ServerUrl = server.Value?.Trim() ?? string.Empty,
                Username = user.Value?.Trim() ?? string.Empty,
                Password = pass.Value ?? string.Empty
            };
        }

        private static string NormalizeDirectory(string path)
        {
            return (path ?? string.Empty).Trim().Trim('/');
        }

        private void HandleAutoStartChanged(SettingField field)
        {
            bool enabled = field.BoolValue;
            bool success = enabled
                ? AutoStartManager.EnableAutoStart()
                : AutoStartManager.DisableAutoStart();

            if (!success)
            {
                field.BoolValue = !enabled;
            }

            AppLogger.Debug($"Auto-start {(field.BoolValue ? "enabled" : "disabled")}");
        }

        /// <summary>
        /// Executes a settings action and marshals all WPF state changes back onto the UI dispatcher.
        /// </summary>
        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not SettingAction action)
            {
                return;
            }

            SettingModule? module = FindModuleForAction(action);
            await SetActionButtonEnabledAsync(button, false).ConfigureAwait(true);

            try
            {
                await SetModuleStatusAsync(module, "执行中...").ConfigureAwait(true);
                string message = await (action.ExecuteAsync?.Invoke() ?? Task.FromResult(string.Empty)).ConfigureAwait(true);
                await SetModuleStatusAsync(module, message).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException or UnauthorizedAccessException)
            {
                await SetModuleStatusAsync(module, $"操作失败: {ex.Message}").ConfigureAwait(true);
                AppLogger.Warn($"Settings action '{action.Key}' failed: {ex.Message}");
            }
            finally
            {
                await SetActionButtonEnabledAsync(button, true).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Updates the action button state on the owning dispatcher to avoid cross-thread access violations.
        /// </summary>
        private static Task SetActionButtonEnabledAsync(Button button, bool isEnabled)
        {
            if (button.Dispatcher.CheckAccess())
            {
                button.IsEnabled = isEnabled;
                return Task.CompletedTask;
            }

            return button.Dispatcher.InvokeAsync(() => button.IsEnabled = isEnabled).Task;
        }

        /// <summary>
        /// Updates the module status on the window dispatcher because the status is bound to WPF UI elements.
        /// </summary>
        private Task SetModuleStatusAsync(SettingModule? module, string status)
        {
            if (module == null)
            {
                return Task.CompletedTask;
            }

            if (Dispatcher.CheckAccess())
            {
                module.Status = status;
                return Task.CompletedTask;
            }

            return Dispatcher.InvokeAsync(() => module.Status = status).Task;
        }

        private SettingModule? FindModuleForAction(SettingAction action)
        {
            return ViewModel.Modules.FirstOrDefault(m => m.Actions.Contains(action));
        }

        private static void ApplyFloatingWindowTaskbarVisibilityToOpenWindows()
        {
            foreach (NoteData note in NoteManager.Instance.Notes)
            {
                note.Window?.RefreshTaskbarVisibilityFromSettings();
            }
        }

        private static void ApplyNoteChromeAutoCollapseToOpenWindows()
        {
            foreach (NoteData note in NoteManager.Instance.Notes)
            {
                note.Window?.RefreshChromeBehaviorFromSettings();
            }
        }

        private static void ApplyPreviewStyleToOpenWindows()
        {
            foreach (NoteData note in NoteManager.Instance.Notes)
            {
                note.Window?.RefreshPreviewStyleFromSettings();
            }
        }

        private static bool ConfigurePreviewStyleField(SettingField field)
        {
            field.Options.Clear();
            foreach (string stylePath in PreviewStyleManager.ListStyles())
            {
                field.Options.Add(new SettingOption
                {
                    Label = stylePath,
                    Value = stylePath
                });
            }

            string resolved = PreviewStyleManager.ResolveStyle(field.Value);
            bool changed = !string.Equals(field.Value, resolved, StringComparison.OrdinalIgnoreCase);
            field.Value = resolved;
            return changed;
        }

        private void ApplyLogSize(string value)
        {
            if (int.TryParse(value, out int kb) && kb > 0)
            {
                AppLogger.SetMaxSizeKb(kb);
                AppLogger.Debug($"Log file size set to {kb} KB");
            }
            else
            {
                AppLogger.Warn("Invalid log file size KB");
            }
        }

        private void ApplySyncInterval(string value)
        {
            int seconds = ParseSyncInterval(value);
            global::YASN.App.App.SyncManager?.SetIntervalSeconds(seconds);
            AppLogger.Debug($"Sync interval set to {seconds} s");
        }

        private static int ParseSyncInterval(string value)
        {
            const int defaultSeconds = 300;
            if (int.TryParse(value, out int seconds) && seconds > 0)
            {
                return Math.Max(10, seconds);
            }

            AppLogger.Warn("Int seconds needed");
            return defaultSeconds;
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedItem is SettingModule module)
            {
                ScrollToModule(module.Key);
            }
        }

        private void ScrollToModule(string key)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ModuleItemsControl.UpdateLayout();
                var module = ViewModel.Modules.FirstOrDefault(m => m.Key == key);
                if (module == null)
                {
                    return;
                }

                FrameworkElement? container = ModuleItemsControl.ItemContainerGenerator.ContainerFromItem(module) as FrameworkElement;
                if (container == null)
                {
                    return;
                }

                container.UpdateLayout();
                GeneralTransform transform = container.TransformToAncestor(ModuleScrollViewer);
                Point point = transform.Transform(new Point(0, 0));
                ModuleScrollViewer.ScrollToVerticalOffset(point.Y + ModuleScrollViewer.VerticalOffset);
            }), DispatcherPriority.Background);
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox { DataContext: SettingField field } passwordBox)
            {
                field.Value = passwordBox.Password;
            }
        }

        private void PasswordBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox { DataContext: SettingField field } passwordBox)
            {
                passwordBox.Password = field.Value ?? string.Empty;
            }
        }
    }
}
