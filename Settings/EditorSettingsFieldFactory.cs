namespace YASN.Settings
{
    internal sealed class EditorSettingFields
    {
        internal required SettingField EnterModeField { get; init; }
    }

    internal static class EditorSettingsFieldFactory
    {
        internal static EditorSettingFields Create()
        {
            var enterModeField = new SettingField
            {
                Key = EditorDisplayModeSettings.SettingKey,
                Title = "Edit Mode",
                Description = "双击右键进入时的编辑模式。",
                FieldType = SettingFieldType.Select,
                Value = EditorDisplayModeSettings.DefaultValue,
                ShouldSync = false
            };

            enterModeField.Options.Add(new SettingOption
            {
                Label = "纯文本",
                Value = EditorDisplayModeSettings.TextOnlyValue
            });
            enterModeField.Options.Add(new SettingOption
            {
                Label = "文本 + 预览",
                Value = EditorDisplayModeSettings.TextAndPreviewValue
            });
            // enterModeField.Options.Add(new SettingOption
            // {
            //     Label = "仅预览",
            //     Value = EditorDisplayModeSettings.PreviewOnlyValue
            // });

            return new EditorSettingFields
            {
                EnterModeField = enterModeField
            };
        }
    }
}
