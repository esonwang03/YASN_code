namespace YASN.Settings
{
    internal sealed class GeneralSettingFields
    {
        internal required SettingField AutoStartField { get; init; }
        internal required SettingField AutoCollapseNoteChromeField { get; init; }
        internal required SettingField LogSizeField { get; init; }
        internal required SettingField FloatingTaskbarVisibilityField { get; init; }
        internal required SettingField PreviewStyleField { get; init; }
        internal required SettingField DataDirectoryField { get; init; }
    }

    internal static class GeneralSettingsFieldFactory
    {
        internal static GeneralSettingFields Create()
        {
            var autoStartField = new SettingField
            {
                Key = "autoStart",
                Title = "Start on Windows startup",
                Description = "启动 Windows 时自动运行 YASN。",
                FieldType = SettingFieldType.Toggle,
                BoolValue = global::YASN.AutoStartManager.IsAutoStartEnabled(),
                ShouldSync = false
            };

            var autoCollapseNoteChromeField = new SettingField
            {
                Key = NoteWindowUiSettings.SettingKey,
                Title = "Auto collapse note window bar",
                Description = "Automatically collapse title bar and toolbar when the mouse leaves the note window.",
                FieldType = SettingFieldType.Toggle,
                BoolValue = NoteWindowUiSettings.DefaultValue,
                ShouldSync = false
            };

            var logSizeField = new SettingField
            {
                Key = "log.maxSizeKb",
                Title = "Max size of log file",
                Description = "达到上限后自动轮转日志。单位为kB",
                FieldType = SettingFieldType.Text,
                Value = "1024",
                ShouldSync = true
            };

            var floatingTaskbarVisibilityField = new SettingField
            {
                Key = global::YASN.FloatingWindowTaskbarVisibility.SettingKey,
                Title = "Show notes in taskbar",
                Description = "控制Note的悬浮窗是否在任务栏显示。",
                FieldType = SettingFieldType.Select,
                Value = global::YASN.FloatingWindowTaskbarVisibility.DefaultValue,
                ShouldSync = true
            };

            floatingTaskbarVisibilityField.Options.Add(new SettingOption
            {
                Label = "始终显示",
                Value = global::YASN.FloatingWindowTaskbarVisibility.AlwaysShowValue
            });
            floatingTaskbarVisibilityField.Options.Add(new SettingOption
            {
                Label = "始终不显示",
                Value = global::YASN.FloatingWindowTaskbarVisibility.AlwaysHideValue
            });
            floatingTaskbarVisibilityField.Options.Add(new SettingOption
            {
                Label = "仅 TopMost 不显示",
                Value = global::YASN.FloatingWindowTaskbarVisibility.HideTopMostOnlyValue
            });

            var previewStyleField = new SettingField
            {
                Key = global::YASN.PreviewStyleManager.SettingKey,
                Title = "Markdown preview style",
                Description = "Load CSS from data/style and apply it to markdown preview.",
                FieldType = SettingFieldType.Select,
                Value = global::YASN.PreviewStyleManager.DefaultStyleRelativePath,
                ShouldSync = false
            };
            previewStyleField.Options.Add(new SettingOption
            {
                Label = global::YASN.PreviewStyleManager.DefaultStyleRelativePath,
                Value = global::YASN.PreviewStyleManager.DefaultStyleRelativePath
            });

            var dataDirectoryField = new SettingField
            {
                Key = global::YASN.AppPaths.DataDirectorySettingKey,
                Title = "Data directory",
                Description = "设置 YASN 读取 JSON 数据的目录（例如 notes.index.json）。支持绝对路径或相对程序目录路径，重启后生效。",
                FieldType = SettingFieldType.Text,
                Value = global::YASN.AppPaths.DataDirectory,
                ShouldSync = false,
                EnableFolderBrowse = true
            };

            return new GeneralSettingFields
            {
                AutoStartField = autoStartField,
                AutoCollapseNoteChromeField = autoCollapseNoteChromeField,
                LogSizeField = logSizeField,
                FloatingTaskbarVisibilityField = floatingTaskbarVisibilityField,
                PreviewStyleField = previewStyleField,
                DataDirectoryField = dataDirectoryField
            };
        }
    }
}
