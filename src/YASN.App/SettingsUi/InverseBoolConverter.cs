using System.Globalization;
using Avalonia.Data.Converters;

namespace YASN.SettingsUi
{
    /// <summary>
    /// Returns the logical negation of a bound boolean. Used to drive the "off" radio button of a
    /// boolean setting from the same <c>BoolValue</c> the "on" radio button binds to, so the pair acts
    /// as one two-state toggle.
    /// </summary>
    public sealed class InverseBoolConverter : IValueConverter
    {
        /// <summary>Negates the bound boolean.</summary>
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is bool flag && !flag;
        }

        /// <summary>Negates the control value on the way back to the source.</summary>
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is bool flag && !flag;
        }
    }
}
