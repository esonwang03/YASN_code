namespace YASN.App.Desktop
{
    public enum FloatingWindowTaskbarVisibilityMode
    {
        AlwaysShow,
        AlwaysHide,
        HideTopMostOnly
    }

    public static class FloatingWindowTaskbarVisibility
    {
        public const string SettingKey = "floatingWindow.taskbarVisibility";
        public const string AlwaysShowValue = "ALWAYSSHOW";
        public const string AlwaysHideValue = "ALWAYSHIDE";
        public const string HideTopMostOnlyValue = "HIDETOPMOSTONLY";

        // Keep old behavior for compatibility: floating windows were hidden from taskbar by default.
        public const string DefaultValue = AlwaysHideValue;

        private static FloatingWindowTaskbarVisibilityMode ParseMode(string value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                AlwaysShowValue => FloatingWindowTaskbarVisibilityMode.AlwaysShow,
                HideTopMostOnlyValue => FloatingWindowTaskbarVisibilityMode.HideTopMostOnly,
                _ => FloatingWindowTaskbarVisibilityMode.AlwaysHide
            };
        }

        public static bool ShouldShowInTaskbar(WindowLevel level, string modeValue)
        {
            return ParseMode(modeValue) switch
            {
                FloatingWindowTaskbarVisibilityMode.AlwaysShow => true,
                FloatingWindowTaskbarVisibilityMode.HideTopMostOnly => level != WindowLevel.TopMost,
                _ => false
            };
        }

        public static string NormalizeValue(string modeValue)
        {
            return ParseMode(modeValue) switch
            {
                FloatingWindowTaskbarVisibilityMode.AlwaysShow => AlwaysShowValue,
                FloatingWindowTaskbarVisibilityMode.HideTopMostOnly => HideTopMostOnlyValue,
                _ => AlwaysHideValue
            };
        }
    }
}
