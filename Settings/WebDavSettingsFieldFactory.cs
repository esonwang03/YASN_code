using System.Globalization;

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
                    Title = "Webdav Server URL",
                    Description = "例如：https://dav.jianguoyun.com/dav/",
                    FieldType = SettingFieldType.Text,
                    Value = "https://dav.jianguoyun.com/dav/",
                    ShouldSync = false
                },
                UserField = new SettingField
                {
                    Key = "webdav.user",
                    Title = "User",
                    Description = "用于认证的账户名。",
                    FieldType = SettingFieldType.Text,
                    ShouldSync = false
                },
                PasswordField = new SettingField
                {
                    Key = "webdav.password",
                    Title = "PassWord / Token",
                    Description = "",
                    FieldType = SettingFieldType.Text,
                    ShouldSync = false
                },
                RemoteField = new SettingField
                {
                    Key = "webdav.remote",
                    Title = "Remote Directory",
                    Description = "例如：MyJianGuoYun/YASN",
                    FieldType = SettingFieldType.Text,
                    Value = "/YASN",
                    ShouldSync = false
                },
                SyncIntervalField = new SettingField
                {
                    Key = "webdav.syncIntervalSeconds",
                    Title = "Sync Interval (Seconds)",
                    Description = "自动同步的时间间隔，最小 10 秒。",
                    FieldType = SettingFieldType.Text,
                    Value = "300",
                    ShouldSync = true
                },
                AutoSyncField = new SettingField
                {
                    Key = "webdav.autoSync",
                    Title = "Enable Syncing",
                    Description = "是否执行云同步。",
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
                    ShouldSync = true
                },
                AttachmentThresholdField = new SettingField
                {
                    Key = AttachmentSyncSettings.AutoSyncThresholdMbKey,
                    Title = "Attachment Sync Threshold (MB)",
                    Description = "Files up to this size are copied and synced; larger files keep file-system paths.",
                    FieldType = SettingFieldType.Text,
                    Value = AttachmentSyncSettings.DefaultAutoSyncThresholdMb.ToString(CultureInfo.InvariantCulture),
                    ShouldSync = true
                }
            };
        }
    }
}
