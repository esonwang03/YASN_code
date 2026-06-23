using Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace YASN.Localization
{
    /// <summary>
    /// XAML markup extension that binds a control property to a localized string, relocalizing
    /// live when the culture changes. Usage: <c>Text="{l:Tr Menu.Exit}"</c>.
    /// </summary>
    /// <remarks>
    /// Returns a binding built from a hand-rolled <see cref="IObservable{T}"/> rather than a
    /// reflection <see cref="Binding"/>. A reflection binding pulls in <c>BindingExpression</c>/
    /// <c>ReflectionBinding</c>, which are not trim/AOT-safe (IL2026/IL3050); the observable path is.
    /// </remarks>
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
        /// Returns a one-way binding that resolves the localized value and re-pushes it whenever the
        /// active culture changes.
        /// </summary>
        /// <param name="serviceProvider">The XAML service provider.</param>
        /// <returns>A binding over the localized-value observable.</returns>
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return new LocalizedValueObservable(Key).ToBinding();
        }

        /// <summary>
        /// Pushes the localized value for a key to its subscriber and re-pushes on every culture
        /// change. One subscriber per binding; unsubscribing detaches the culture-changed handler.
        /// </summary>
        private sealed class LocalizedValueObservable : IObservable<object?>
        {
            private readonly string key;

            public LocalizedValueObservable(string key)
            {
                this.key = key;
            }

            public IDisposable Subscribe(IObserver<object?> observer)
            {
                return new Subscription(key, observer);
            }

            private sealed class Subscription : IDisposable
            {
                private readonly string key;
                private readonly IObserver<object?> observer;

                public Subscription(string key, IObserver<object?> observer)
                {
                    this.key = key;
                    this.observer = observer;
                    LocalizationService.Current.CultureChanged += OnCultureChanged;
                    Push();
                }

                private void OnCultureChanged(object? sender, EventArgs e) => Push();

                private void Push() => observer.OnNext(LocalizationService.Current[key]);

                public void Dispose()
                {
                    LocalizationService.Current.CultureChanged -= OnCultureChanged;
                }
            }
        }
    }
}
