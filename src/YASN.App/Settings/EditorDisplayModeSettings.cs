namespace YASN.App.Settings
{
    internal static class EditorDisplayModeSettings
    {
        internal const string SettingKey = "editor.enterMode";
        internal const string TextOnlyValue = "textOnly";
        internal const string TextAndPreviewValue = "textAndPreview";
        internal const string PreviewOnlyValue = "previewOnly";
        internal const string DefaultValue = TextAndPreviewValue;

        internal static EditorDisplayMode ParseValue(string? value)
        {
            return value?.Trim() switch
            {
                TextOnlyValue => EditorDisplayMode.TextOnly,
                PreviewOnlyValue => EditorDisplayMode.PreviewOnly,
                _ => EditorDisplayMode.TextAndPreview
            };
        }

        internal static bool TryParseValue(string? value, out EditorDisplayMode mode)
        {
            mode = EditorDisplayMode.TextAndPreview;
            switch (value?.Trim())
            {
                case TextOnlyValue:
                    mode = EditorDisplayMode.TextOnly;
                    return true;
                case TextAndPreviewValue:
                    mode = EditorDisplayMode.TextAndPreview;
                    return true;
                case PreviewOnlyValue:
                    mode = EditorDisplayMode.PreviewOnly;
                    return true;
                default:
                    return false;
            }
        }

        internal static string ToValue(EditorDisplayMode mode)
        {
            return mode switch
            {
                EditorDisplayMode.TextOnly => TextOnlyValue,
                EditorDisplayMode.PreviewOnly => PreviewOnlyValue,
                _ => TextAndPreviewValue
            };
        }

        internal static EditorDisplayMode GetEnterMode(SettingsStore settingsStore)
        {
            string raw = settingsStore.GetValue(
                SettingKey,
                shouldSync: false,
                defaultValue: DefaultValue);
            return ParseValue(raw);
        }
    }
}
