namespace YASN.Settings
{
    internal sealed class GeneralSettingFields
    {
        internal required SettingField AutoStartField { get; init; }
        internal required SettingField AutoCollapseNoteChromeField { get; init; }
        internal required SettingField LogSizeField { get; init; }
        internal required SettingField FloatingTaskbarVisibilityField { get; init; }
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
                ShouldSync = true
            };

            var autoCollapseNoteChromeField = new SettingField
            {
                Key = NoteWindowUiSettings.AutoCollapseChromeKey,
                Title = "Auto collapse note window bar",
                Description = "Automatically collapse title bar and toolbar when the mouse leaves the note window.",
                FieldType = SettingFieldType.Toggle,
                BoolValue = NoteWindowUiSettings.DefaultAutoCollapseChrome,
                ShouldSync = true
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

            return new GeneralSettingFields
            {
                AutoStartField = autoStartField,
                AutoCollapseNoteChromeField = autoCollapseNoteChromeField,
                LogSizeField = logSizeField,
                FloatingTaskbarVisibilityField = floatingTaskbarVisibilityField
            };
        }
    }
}
