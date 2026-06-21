using Avalonia.Styling;

namespace YASN.Theming
{
    /// <summary>
    /// Pure logic for the global theme setting: parsing the persisted value and mapping it to an
    /// Avalonia <see cref="ThemeVariant"/>. <see cref="System"/> defers to the OS by leaving the
    /// requested variant unset so Avalonia follows the platform theme.
    /// </summary>
    public static class ThemePreference
    {
        /// <summary>The persisted value for following the operating-system theme.</summary>
        public const string SystemValue = "SYSTEM";

        /// <summary>The persisted value for the light theme.</summary>
        public const string LightValue = "LIGHT";

        /// <summary>The persisted value for the dark theme.</summary>
        public const string DarkValue = "DARK";

        /// <summary>The synced settings key for the theme preference.</summary>
        public const string SettingKey = "ui.theme";

        /// <summary>The default preference: follow the operating-system theme.</summary>
        public const string DefaultValue = SystemValue;

        /// <summary>
        /// Maps a persisted value to the Avalonia theme variant to request on the application, or
        /// <see langword="null"/> to follow the operating-system theme.
        /// </summary>
        /// <param name="value">The persisted string value.</param>
        /// <returns>The variant to request, or <see langword="null"/> for the system default.</returns>
        public static ThemeVariant? ToVariant(string? value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                LightValue => ThemeVariant.Light,
                DarkValue => ThemeVariant.Dark,
                _ => null
            };
        }
    }
}
