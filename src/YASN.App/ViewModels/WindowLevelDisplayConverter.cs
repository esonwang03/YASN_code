using System.Globalization;
using Avalonia.Data.Converters;
using YASN.Core;
using YASN.Localization;

namespace YASN.ViewModels
{
    /// <summary>
    /// Converts a <see cref="WindowLevel"/> to its localized display name (the <c>Window.Level.*</c>
    /// catalog keys) for the per-row level dropdown in the note manager. One-way: the dropdown's
    /// selected value stays the <see cref="WindowLevel"/> enum, only its rendered label is localized.
    /// </summary>
    public sealed class WindowLevelDisplayConverter : IValueConverter
    {
        /// <summary>Returns the localized name for a window level.</summary>
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is WindowLevel level
                ? LocalizationService.Current[$"Window.Level.{level}"]
                : string.Empty;
        }

        /// <summary>Not supported: the dropdown binds the enum value directly.</summary>
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
