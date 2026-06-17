using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace YASN.Localization
{
    /// <summary>
    /// XAML markup extension that binds a control property to a localized string, relocalizing
    /// live when the culture changes. Usage: <c>Text="{l:Tr Menu.Exit}"</c>.
    /// </summary>
    public sealed class TrExtension : MarkupExtension
    {
        /// <summary>
        /// Initializes an empty extension for XAML attribute syntax.
        /// </summary>
        public TrExtension()
        {
        }

        /// <summary>
        /// Initializes the extension with a localization key.
        /// </summary>
        /// <param name="key">The localization key to bind.</param>
        public TrExtension(string key)
        {
            Key = key;
        }

        /// <summary>
        /// Gets or sets the localization key.
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Returns a binding to the shared localized string indexer.
        /// </summary>
        /// <param name="serviceProvider">The XAML service provider.</param>
        /// <returns>A binding that resolves and updates the localized value.</returns>
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return new Binding
            {
                Source = LocalizationService.Current.Strings,
                Path = $"[{Key}]",
                Mode = BindingMode.OneWay
            };
        }
    }
}
