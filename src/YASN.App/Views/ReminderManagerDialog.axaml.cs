using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using YASN.Infrastructure.Reminders;
using YASN.Localization;

namespace YASN.Views
{
    /// <summary>
    /// Lists the inline reminder rules declared in a note and lets the user enable/disable or delete
    /// each one, or jump to its source line in the editor. Edits are applied to the live note content
    /// through the supplied callbacks so the preview and scheduler refresh; the list rebuilds after each
    /// change. Replaces the old single one-shot date/time reminder dialog.
    /// </summary>
    public sealed partial class ReminderManagerDialog : Window
    {
        private readonly Func<string> getContent;
        private readonly Action<string> applyContent;
        private readonly Action<int> jumpToLine;
        private readonly LocalizationService localization;
        private readonly ItemsControl itemsList;
        private readonly TextBlock emptyLabel;

        /// <summary>
        /// Initializes the manager over a note's live content.
        /// </summary>
        /// <param name="getContent">Reads the note's current Markdown content.</param>
        /// <param name="applyContent">Applies rewritten content to the live note.</param>
        /// <param name="jumpToLine">Focuses the editor at a 0-based source line.</param>
        /// <param name="localization">The active localization service.</param>
        public ReminderManagerDialog(
            Func<string> getContent,
            Action<string> applyContent,
            Action<int> jumpToLine,
            LocalizationService localization)
        {
            this.getContent = getContent;
            this.applyContent = applyContent;
            this.jumpToLine = jumpToLine;
            this.localization = localization;
            InitializeComponent();

            itemsList = this.FindControl<ItemsControl>("ItemsList")
                ?? throw new InvalidOperationException("ItemsList was not found.");
            emptyLabel = this.FindControl<TextBlock>("EmptyLabel")
                ?? throw new InvalidOperationException("EmptyLabel was not found.");

            Rebuild();
        }

        /// <summary>
        /// Initializes an empty dialog for the XAML designer.
        /// </summary>
        public ReminderManagerDialog()
            : this(() => string.Empty, _ => { }, _ => { }, LocalizationService.Current)
        {
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void Rebuild()
        {
            string content = getContent();
            List<ReminderRow> rows = new();
            foreach (NoteReminderRule rule in NoteReminderParser.Parse(content))
            {
                rows.Add(new ReminderRow(rule, LineForOffset(content, rule.SourceStart), localization));
            }

            itemsList.ItemsSource = rows;
            emptyLabel.IsVisible = rows.Count == 0;
            itemsList.IsVisible = rows.Count > 0;
        }

        /// <summary>Counts the 0-based line containing <paramref name="offset"/> by counting newlines before it.</summary>
        private static int LineForOffset(string text, int offset)
        {
            int clamped = Math.Clamp(offset, 0, text.Length);
            int line = 0;
            for (int i = 0; i < clamped; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        private void HandleToggleClick(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.Tag is not ReminderRow row)
            {
                return;
            }

            if (ReminderControlEditor.TrySetEnabled(getContent(), row.RuleId, !row.Enabled, out string updated))
            {
                applyContent(updated);
                Rebuild();
            }
        }

        private void HandleDeleteClick(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.Tag is not ReminderRow row)
            {
                return;
            }

            if (ReminderControlEditor.TryDelete(getContent(), row.RuleId, out string updated))
            {
                applyContent(updated);
                Rebuild();
            }
        }

        private void HandleEditClick(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.Tag is not ReminderRow row)
            {
                return;
            }

            jumpToLine(row.SourceLine);
            Close();
        }

        private void HandleCloseClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>
    /// A single reminder row bound by the list: presentation derived from a parsed rule, plus the
    /// stable rule id and source line the action handlers act on. Top-level (not nested) so the XAML
    /// compiler can name it as the template's <c>x:DataType</c> for compiled, trim/AOT-safe bindings.
    /// </summary>
    internal sealed class ReminderRow
    {
        public ReminderRow(NoteReminderRule rule, int sourceLine, LocalizationService localization)
        {
            RuleId = rule.RuleId;
            Enabled = rule.Enabled;
            SourceLine = sourceLine;
            DisplayText = string.IsNullOrWhiteSpace(rule.DisplayText) ? rule.Content : rule.DisplayText;

            string cadence = rule.RemainingCount switch
            {
                null => localization["Reminder.Manager.Recurring"],
                1 => localization["Reminder.Manager.Once"],
                { } n => string.Format(System.Globalization.CultureInfo.CurrentCulture, localization["Reminder.Manager.TimesLeft"], n)
            };
            string state = rule.Enabled ? cadence : localization["Reminder.Manager.Disabled"];
            Summary = $"{state} · {rule.CronText}";
            ToggleLabel = rule.Enabled ? localization["Reminder.Manager.Disable"] : localization["Reminder.Manager.Enable"];
        }

        public string RuleId { get; }
        public bool Enabled { get; }
        public int SourceLine { get; }
        public string DisplayText { get; }
        public string Summary { get; }
        public string ToggleLabel { get; }
    }
}
