using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace YASN.Views
{
    /// <summary>
    /// Modal dialog that edits a note's optional reminder time. Returns the chosen
    /// <see cref="DateTimeOffset"/>, or null when the reminder is cleared or cancelled.
    /// </summary>
    public sealed partial class ReminderDialog : Window
    {
        private readonly DatePicker reminderDate;
        private readonly TimePicker reminderTime;

        /// <summary>
        /// Initializes the dialog from an existing reminder time.
        /// </summary>
        /// <param name="current">The current reminder time, or null when none is set.</param>
        public ReminderDialog(DateTimeOffset? current)
        {
            InitializeComponent();

            reminderDate = this.FindControl<DatePicker>("ReminderDate")
                ?? throw new InvalidOperationException("ReminderDate was not found.");
            reminderTime = this.FindControl<TimePicker>("ReminderTime")
                ?? throw new InvalidOperationException("ReminderTime was not found.");

            DateTimeOffset seed = current?.ToLocalTime() ?? DateTimeOffset.Now.AddHours(1);
            reminderDate.SelectedDate = seed;
            reminderTime.SelectedTime = seed.TimeOfDay;
        }

        /// <summary>
        /// Initializes an empty dialog for the XAML designer.
        /// </summary>
        public ReminderDialog()
            : this(null)
        {
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void HandleSaveClick(object? sender, RoutedEventArgs e)
        {
            if (reminderDate.SelectedDate is not { } date)
            {
                Close(new ReminderDialogResult(null));
                return;
            }

            TimeSpan time = reminderTime.SelectedTime ?? TimeSpan.Zero;
            DateTimeOffset local = new DateTimeOffset(date.Date + time, date.Offset);
            Close(new ReminderDialogResult(local.ToUniversalTime()));
        }

        private void HandleClearClick(object? sender, RoutedEventArgs e)
        {
            Close(new ReminderDialogResult(null));
        }
    }
}
