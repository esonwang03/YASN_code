using System.Globalization;
using Avalonia.Data.Converters;

namespace YASN.SettingsUi
{
    /// <summary>
    /// Returns true when a field's type matches the converter parameter, so the matching editor
    /// control is the only one shown for each settings field.
    /// </summary>
    public sealed class FieldTypeVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// Compares the bound <see cref="SettingFieldType"/> against the parameter name.
        /// </summary>
        /// <param name="value">The bound field type.</param>
        /// <param name="targetType">The binding target type.</param>
        /// <param name="parameter">The field-type name to match.</param>
        /// <param name="culture">The binding culture.</param>
        /// <returns>True when the field type matches the parameter.</returns>
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is SettingFieldType fieldType
                && parameter is string name
                && Enum.TryParse(name, out SettingFieldType expected)
                && fieldType == expected;
        }

        /// <summary>
        /// Not supported; visibility binding is one-way.
        /// </summary>
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
