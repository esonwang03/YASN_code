namespace YASN.Settings
{
    internal sealed class WebDavSettingFields
    {
        internal required SettingField ServerUrlField { get; init; }
        internal required SettingField UserField { get; init; }
        internal required SettingField PasswordField { get; init; }
        internal required SettingField RemoteField { get; init; }
        internal required SettingField SyncIntervalField { get; init; }
        internal required SettingField AutoSyncField { get; init; }
        internal required SettingField AttachmentAutoSyncField { get; init; }
        internal required SettingField AttachmentThresholdField { get; init; }
    }

    internal static class WebDavSettingsFieldFactory
    {
        internal static WebDavSettingFields Create()
        {
            return new WebDavSettingFields
            {
                ServerUrlField = new SettingField
                {
                    Key = "webdav.server",
                    Title = "Server URL",
                    Description = "例如：https://dav.jianguoyun.com/dav/",
                    FieldType = SettingFieldType.Text,
                    Value = "https://dav.jianguoyun.com/dav/",
                    ShouldSync = false
                },
                UserField = new SettingField
                {
                    Key = "webdav.user",
                    Title = "用户名 / 邮箱",
                    Description = "用于认证的账户名。",
                    FieldType = SettingFieldType.Text,
                    ShouldSync = false
                },
                PasswordField = new SettingField
                {
                    Key = "webdav.password",
                    Title = "密码 / App Token",
                    Description = "专用应用密码。",
                    FieldType = SettingFieldType.Text,
                    ShouldSync = false
                },
                RemoteField = new SettingField
                {
                    Key = "webdav.remote",
                    Title = "远程目录",
                    Description = "例如：MyJianGuoYun/YASN",
                    FieldType = SettingFieldType.Text,
                    Value = "/MyJianGuoYun/YASN",
                    ShouldSync = false
                },
                SyncIntervalField = new SettingField
                {
                    Key = "webdav.syncIntervalSeconds",
                    Title = "同步间隔 (秒)",
                    Description = "自动同步的时间间隔，最小 10 秒。",
                    FieldType = SettingFieldType.Text,
                    Value = "300",
                    ShouldSync = false
                },
                AutoSyncField = new SettingField
                {
                    Key = "webdav.autoSync",
                    Title = "启用自动同步",
                    Description = "显式开关控制是否执行云同步。",
                    FieldType = SettingFieldType.Toggle,
                    BoolValue = global::YASN.App.SyncManager?.IsEnabled ?? false,
                    ShouldSync = false
                },
                AttachmentAutoSyncField = new SettingField
                {
                    Key = AttachmentSyncSettings.AutoSyncEnabledKey,
                    Title = "Auto Sync Attachments",
                    Description = "When enabled, attachments up to the threshold are copied into app data and synced.",
                    FieldType = SettingFieldType.Toggle,
                    BoolValue = AttachmentSyncSettings.DefaultAutoSyncEnabled,
                    ShouldSync = false
                },
                AttachmentThresholdField = new SettingField
                {
                    Key = AttachmentSyncSettings.AutoSyncThresholdMbKey,
                    Title = "Attachment Sync Threshold (MB)",
                    Description = "Files up to this size are copied and synced; larger files keep file-system paths.",
                    FieldType = SettingFieldType.Text,
                    Value = AttachmentSyncSettings.DefaultAutoSyncThresholdMb.ToString(),
                    ShouldSync = false
                }
            };
        }
    }
}
