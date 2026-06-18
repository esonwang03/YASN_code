using System.ComponentModel;
using System.Runtime.CompilerServices;
using YASN.AvaloniaNotes;
using YASN.Core;
using YASN.Reminders;

namespace YASN.ViewModels
{
    /// <summary>
    /// Coordinates one Avalonia note window with persistence, title updates, and reminder scheduling.
    /// </summary>
    public sealed class NoteWindowViewModel : INotifyPropertyChanged
    {
        private readonly NoteRepository repository;
        private readonly ReminderScheduler reminders;
        private readonly AvaloniaNoteDocument note;

        /// <summary>
        /// Initializes a note window view model.
        /// </summary>
        /// <param name="note">The note document represented by the window.</param>
        /// <param name="repository">The note repository used for persistence.</param>
        /// <param name="reminders">The scheduler used to arm and clear reminders.</param>
        public NoteWindowViewModel(AvaloniaNoteDocument note, NoteRepository repository, ReminderScheduler reminders)
        {
            this.note = note;
            this.repository = repository;
            this.reminders = reminders;
        }

        /// <summary>
        /// Raised when bindable state changes.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raised when the preview HTML should be regenerated.
        /// </summary>
        public event EventHandler? PreviewRequested;

        /// <summary>
        /// Gets the note identifier.
        /// </summary>
        public string NoteId => note.Id;

        /// <summary>
        /// Gets the note display title.
        /// </summary>
        public string Title => note.Title;

        /// <summary>
        /// Attempts to rename the note, validating against other notes' titles.
        /// </summary>
        /// <param name="proposed">The proposed new title.</param>
        /// <param name="errorKey">The localization key describing a validation failure, if any.</param>
        /// <returns><see langword="true"/> when the rename succeeded.</returns>
        public bool TryRename(string? proposed, out string? errorKey)
        {
            IEnumerable<string> otherTitles = repository.LoadAll()
                .Where(other => other.Id != note.Id)
                .Select(other => other.Title);

            if (!NoteTitleValidator.TryValidate(proposed, otherTitles, out string normalized, out errorKey))
            {
                return false;
            }

            note.StoredTitle = normalized;
            repository.Save(note);
            OnPropertyChanged(nameof(Title));
            return true;
        }

        /// <summary>
        /// Gets the editable Markdown content.
        /// </summary>
        public string Content
        {
            get => note.Content;
            set
            {
                string next = value ?? string.Empty;
                if (note.Content == next)
                {
                    return;
                }

                note.Content = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Title));
                Save();
                reminders.RescheduleCron(note);
                PreviewRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Gets or sets the requested window level.
        /// </summary>
        public WindowLevel Level
        {
            get => note.Level;
            set
            {
                if (note.Level == value)
                {
                    return;
                }

                note.Level = value;
                OnPropertyChanged();
                Save();
            }
        }

        /// <summary>
        /// Gets or sets the editor display mode and persists the change.
        /// </summary>
        public EditorDisplayMode DisplayMode
        {
            get => note.DisplayMode;
            set
            {
                if (note.DisplayMode == value)
                {
                    return;
                }

                note.DisplayMode = value;
                OnPropertyChanged();
                Save();
            }
        }

        /// <summary>
        /// Gets or sets whether the note appears in the taskbar where supported.
        /// </summary>
        public bool ShowInTaskbar
        {
            get => note.ShowInTaskbar;
            set
            {
                if (note.ShowInTaskbar == value)
                {
                    return;
                }

                note.ShowInTaskbar = value;
                OnPropertyChanged();
                Save();
            }
        }

        /// <summary>
        /// Gets the note document.
        /// </summary>
        public AvaloniaNoteDocument Note => note;

        /// <summary>
        /// Gets or sets the optional reminder time, persisting and rescheduling on change.
        /// </summary>
        public DateTimeOffset? ReminderAt
        {
            get => note.ReminderAt;
            set
            {
                if (note.ReminderAt == value)
                {
                    return;
                }

                note.ReminderAt = value;
                OnPropertyChanged();
                Save();
                reminders.Reschedule(note);
            }
        }

        /// <summary>
        /// Saves the note document.
        /// </summary>
        public void Save()
        {
            repository.Save(note);
        }

        /// <summary>
        /// Updates persisted window bounds.
        /// </summary>
        /// <param name="left">Window left position.</param>
        /// <param name="top">Window top position.</param>
        /// <param name="width">Window width.</param>
        /// <param name="height">Window height.</param>
        public void UpdateBounds(double left, double top, double width, double height)
        {
            note.Left = left;
            note.Top = top;
            note.Width = width;
            note.Height = height;
            Save();
        }

        /// <summary>
        /// Marks the note as open or closed.
        /// </summary>
        /// <param name="isOpen">Whether the window is open.</param>
        public void SetOpen(bool isOpen)
        {
            note.IsOpen = isOpen;
            Save();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
