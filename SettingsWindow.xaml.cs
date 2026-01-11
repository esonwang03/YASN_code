using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using YASN.Settings;
using YASN.Sync.WebDav;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using Point = System.Windows.Point;

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
        }

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            BuildModules();
        }

        private void BuildModules()
        {
            ViewModel.Modules.Clear();
            var allFields = new System.Collections.Generic.List<SettingField>();

            var autoStartField = new SettingField
            {
                Key = "autoStart",
                Title = "开机自启动",
                Description = "启动 Windows 时自动运行 YASN。",
                FieldType = SettingFieldType.Toggle,
                BoolValue = AutoStartManager.IsAutoStartEnabled(),
                ShouldSync = false
            };
            allFields.Add(autoStartField);

            var generalModule = new SettingModule
            {
                Key = "general",
                Title = "通用",
                Description = "应用通用行为与系统集成设置。"
            };
            generalModule.Fields.Add(autoStartField);

            var serverUrlField = new SettingField
            {
                Key = "webdav.server",
                Title = "Server URL",
                Description = "例如：https://dav.jianguoyun.com/dav/",
                FieldType = SettingFieldType.Text,
                Value = "https://dav.jianguoyun.com/dav/",
                ShouldSync = true
            };
            allFields.Add(serverUrlField);

            var userField = new SettingField
            {
                Key = "webdav.user",
                Title = "用户名 / 邮箱",
                Description = "用于认证的账户名。",
                FieldType = SettingFieldType.Text,
                ShouldSync = true
            };
            allFields.Add(userField);

            var passwordField = new SettingField
            {
                Key = "webdav.password",
                Title = "密码 / App Token",
                Description = "建议使用专用应用密码。",
                FieldType = SettingFieldType.Password,
                ShouldSync = true
            };
            allFields.Add(passwordField);

            var remoteField = new SettingField
            {
                Key = "webdav.remote",
                Title = "远程目录",
                Description = "例如：/MyJianGuoYun/YASN",
                FieldType = SettingFieldType.Text,
                Value = "/MyJianGuoYun/YASN",
                ShouldSync = true
            };
            allFields.Add(remoteField);

            var autoSyncField = new SettingField
            {
                Key = "webdav.autoSync",
                Title = "启用自动同步",
                Description = "每 5 分钟自动同步一次。",
                FieldType = SettingFieldType.Toggle,
                BoolValue = App.SyncManager?.IsEnabled ?? false,
                ShouldSync = true
            };
            allFields.Add(autoSyncField);

            var webDavModule = new SettingModule
            {
                Key = "webdav",
                Title = "云同步",
                Description = "配置 WebDAV 以在多设备间同步笔记。"
            };

            webDavModule.Fields.Add(serverUrlField);
            webDavModule.Fields.Add(userField);
            webDavModule.Fields.Add(passwordField);
            webDavModule.Fields.Add(remoteField);
            webDavModule.Fields.Add(autoSyncField);

            _settingsStore.ApplyValues(allFields);

            autoStartField.OnChanged = field =>
            {
                HandleAutoStartChanged(field);
                _settingsStore.PersistField(field);
            };

            foreach (var field in new[] { serverUrlField, userField, passwordField, remoteField, autoSyncField })
            {
                field.OnChanged = f => _settingsStore.PersistField(f);
            }

            webDavModule.Actions.Add(new SettingAction
            {
                Key = "webdav.test",
                Label = "测试连接",
                ExecuteAsync = async () =>
                {
                    var options = BuildWebDavOptions(serverUrlField, userField, passwordField);
                    using var client = new WebDavSyncClient(options);
                    var ok = await client.TestConnectionAsync(NormalizeDirectory(remoteField.Value));
                    return ok ? "连接成功" : $"连接失败：{client.LastError ?? "未知错误"}";
                }
            });

            webDavModule.Actions.Add(new SettingAction
            {
                Key = "webdav.save",
                Label = "保存并应用",
                ExecuteAsync = async () =>
                {
                    if (App.SyncManager == null)
                    {
                        return "同步管理器未初始化。";
                    }

                    var options = BuildWebDavOptions(serverUrlField, userField, passwordField);
                    var client = new WebDavSyncClient(options);
                    var enabled = autoSyncField.BoolValue;
                    var configured = await App.SyncManager.ConfigureAsync(client, NormalizeDirectory(remoteField.Value), enabled);
                    return configured ? "WebDAV 已保存并生效。" : "WebDAV 配置失败。";
                }
            });

            ViewModel.Modules.Add(generalModule);
            ViewModel.Modules.Add(webDavModule);
            NavList.SelectedIndex = 0;
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
            var enabled = field.BoolValue;
            var success = enabled
                ? AutoStartManager.EnableAutoStart()
                : AutoStartManager.DisableAutoStart();

            if (!success)
            {
                field.BoolValue = !enabled;
                MessageBox.Show("更新开机自启动失败。", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is SettingAction action)
            {
                button.IsEnabled = false;
                try
                {
                    var module = FindModuleForAction(action);
                    if (module != null)
                    {
                        module.Status = "执行中...";
                    }

                    var message = await (action.ExecuteAsync?.Invoke() ?? Task.FromResult(string.Empty));

                    if (module != null)
                    {
                        module.Status = message;
                    }
                }
                catch (Exception ex)
                {
                    var module = FindModuleForAction(action);
                    if (module != null)
                    {
                        module.Status = $"操作失败：{ex.Message}";
                    }
                }
                finally
                {
                    button.IsEnabled = true;
                }
            }
        }

        private SettingModule FindModuleForAction(SettingAction action)
        {
            return ViewModel.Modules.FirstOrDefault(m => m.Actions.Contains(action));
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
                    return;

                var container = ModuleItemsControl.ItemContainerGenerator.ContainerFromItem(module) as FrameworkElement;
                if (container == null)
                    return;

                container.UpdateLayout();
                var transform = container.TransformToAncestor(ModuleScrollViewer);
                var point = transform.Transform(new Point(0, 0));
                ModuleScrollViewer.ScrollToVerticalOffset(point.Y + ModuleScrollViewer.VerticalOffset);
            }), DispatcherPriority.Background);
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox && passwordBox.DataContext is SettingField field)
            {
                field.Value = passwordBox.Password;
            }
        }

        private void PasswordBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox && passwordBox.DataContext is SettingField field)
            {
                passwordBox.Password = field.Value ?? string.Empty;
            }
        }
    }
}
