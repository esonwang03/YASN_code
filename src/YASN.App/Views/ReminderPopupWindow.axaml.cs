using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using YASN.AvaloniaNotes;
using YASN.Infrastructure;
using YASN.Infrastructure.Reminders;

namespace YASN.Views
{
    /// <summary>
    /// Topmost, frameless-ish popup shown when a note reminder fires. Renders the reminder content as
    /// Markdown using the same preview pipeline as the note editor, so formatting matches the note.
    /// </summary>
    public sealed partial class ReminderPopupWindow : Window
    {
        private readonly NativeWebView reminderWebView;
        private readonly TextBlock headerText;

        /// <summary>
        /// Initializes the popup for a fired reminder and renders its content.
        /// </summary>
        /// <param name="note">The note that owns the rule.</param>
        /// <param name="rule">The rule that fired.</param>
        public ReminderPopupWindow(AvaloniaNoteDocument note, NoteReminderRule rule)
        {
            InitializeComponent();

            reminderWebView = this.FindControl<NativeWebView>("ReminderWebView")
                ?? throw new InvalidOperationException("ReminderWebView was not found.");
            headerText = this.FindControl<TextBlock>("HeaderText")
                ?? throw new InvalidOperationException("HeaderText was not found.");

            headerText.Text = note.Title;
            RenderContent(note.Id, rule);
            PositionBottomRight();
        }

        /// <summary>
        /// Initializes an empty popup for the XAML designer.
        /// </summary>
        public ReminderPopupWindow()
        {
            InitializeComponent();
            reminderWebView = this.FindControl<NativeWebView>("ReminderWebView")
                ?? throw new InvalidOperationException("ReminderWebView was not found.");
            headerText = this.FindControl<TextBlock>("HeaderText")
                ?? throw new InvalidOperationException("HeaderText was not found.");
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void RenderContent(int noteId, NoteReminderRule rule)
        {
            PreviewStyleManager.EnsureInitialized();
            string stylePath = PreviewStyleManager.ToStyleAbsolutePath(PreviewStyleManager.DefaultStyleRelativePath);
            string styleHref = new Uri(stylePath).AbsoluteUri;
            string baseHref = new Uri(AppPaths.DataDirectory + Path.DirectorySeparatorChar).AbsoluteUri;
            string body = string.IsNullOrWhiteSpace(rule.Content) ? rule.DisplayText : rule.Content;
            string html = MarkdownPreviewDocument.Render(body, styleHref, baseHref);

            string htmlPath = Path.Combine(AppPaths.HtmlCacheRoot, $"reminder-{noteId}.html");
            string? directory = Path.GetDirectoryName(htmlPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(htmlPath, html);
            reminderWebView.Navigate(new Uri(htmlPath));
        }

        private void PositionBottomRight()
        {
            Screen? screen = Screens.Primary ?? Screens.All.FirstOrDefault();
            if (screen is null)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                return;
            }

            const int margin = 24;
            Avalonia.PixelRect area = screen.WorkingArea;
            double scaling = screen.Scaling;
            int physicalWidth = (int)(Width * scaling);
            int physicalHeight = (int)(Height * scaling);
            int x = area.X + area.Width - physicalWidth - margin;
            int y = area.Y + area.Height - physicalHeight - margin;
            Position = new Avalonia.PixelPoint(Math.Max(area.X, x), Math.Max(area.Y, y));
        }

        private void HandleDismissClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
