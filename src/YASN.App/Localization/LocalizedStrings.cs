using System.ComponentModel;

namespace YASN.Localization
{
    /// <summary>
    /// Change-notifying localized string indexer bound by the <c>{l:Tr}</c> markup extension so
    /// open windows relocalize live when the culture changes.
    /// </summary>
    public sealed class LocalizedStrings : INotifyPropertyChanged
    {
        private readonly LocalizationService service;

        /// <summary>
        /// Initializes the indexer over a localization service.
        /// </summary>
        /// <param name="service">The backing localization service.</param>
        public LocalizedStrings(LocalizationService service)
        {
            this.service = service;
        }

        /// <summary>
        /// Raised for the indexer when the active culture changes.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Gets the localized string for a key in the active culture.
        /// </summary>
        /// <param name="key">The localization key.</param>
        public string this[string key] => service[key];

        /// <summary>
        /// Notifies bindings that every indexed value may have changed.
        /// </summary>
        public void RaiseAllChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }
}
