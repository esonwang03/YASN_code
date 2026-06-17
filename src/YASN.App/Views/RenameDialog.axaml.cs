using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using YASN.Localization;

namespace YASN.Views
{
    /// <summary>
    /// Modal dialog that prompts for a new note title with inline validation. Returns the accepted
    /// trimmed title, or null when cancelled.
    /// </summary>
    public sealed partial class RenameDialog : Window
    {
        private readonly TextBox titleBox;
        private readonly TextBlock errorText;
        private readonly Func<string, string?> validate;
        private readonly LocalizationService localization;

        /// <summary>
        /// Initializes the dialog with the current title and a validator.
        /// </summary>
        /// <param name="currentTitle">The current title used to seed the input.</param>
        /// <param name="validate">Returns a localization key when the input is invalid, else null.</param>
        /// <param name="localization">The localization service used to resolve error messages.</param>
        public RenameDialog(string currentTitle, Func<string, string?> validate, LocalizationService localization)
        {
            this.validate = validate;
            this.localization = localization;
            InitializeComponent();

            titleBox = this.FindControl<TextBox>("TitleBox")
                ?? throw new InvalidOperationException("TitleBox was not found.");
            errorText = this.FindControl<TextBlock>("ErrorText")
                ?? throw new InvalidOperationException("ErrorText was not found.");

            titleBox.Text = currentTitle;
        }

        /// <summary>
        /// Initializes an empty dialog for the XAML designer.
        /// </summary>
        public RenameDialog()
            : this(string.Empty, _ => null, LocalizationService.Current)
        {
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void HandleSaveClick(object? sender, RoutedEventArgs e)
        {
            string proposed = (titleBox.Text ?? string.Empty).Trim();
            string? errorKey = validate(proposed);
            if (errorKey is not null)
            {
                errorText.Text = localization[errorKey];
                errorText.IsVisible = true;
                return;
            }

            Close(proposed);
        }

        private void HandleCancelClick(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}
