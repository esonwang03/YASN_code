using System.ComponentModel;
using System.Runtime.CompilerServices;
using YASN.AvaloniaNotes;
using YASN.Core;
using YASN.Localization;

namespace YASN.ViewModels
{
    /// <summary>
    /// Presents one note as a row in the note manager list.
    /// </summary>
    public sealed class NoteListItemViewModel : INotifyPropertyChanged
    {
        private bool isOpen;
        private bool isConflicted;

        /// <summary>
        /// Initializes a new instance of the <see cref="NoteListItemViewModel"/> class.
        /// </summary>
        /// <param name="note">The note this row represents.</param>
        /// <param name="isOpen">Whether the note currently has an open window.</param>
        public NoteListItemViewModel(AvaloniaNoteDocument note, bool isOpen)
        {
            Note = note;
            this.isOpen = isOpen;
        }

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Gets the underlying note document.
        /// </summary>
        public AvaloniaNoteDocument Note { get; }

        /// <summary>
        /// Gets the note identifier.
        /// </summary>
        public string Id => Note.Id;

        /// <summary>
        /// Gets the note display title.
        /// </summary>
        public string Title => Note.Title;

        /// <summary>
        /// Gets the note window stacking level.
        /// </summary>
        public WindowLevel Level => Note.Level;

        /// <summary>
        /// Gets or sets whether the note currently has an open window.
        /// </summary>
        public bool IsOpen
        {
            get => isOpen;
            set
            {
                if (isOpen == value)
                {
                    return;
                }

                isOpen = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusKey));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(ToggleText));
            }
        }

        /// <summary>
        /// Gets the localization key for the open/closed status text.
        /// </summary>
        public string StatusKey => isOpen ? "Main.Status.Open" : "Main.Status.Closed";

        /// <summary>
        /// Gets the localized toggle button text (Close when open, Open when closed).
        /// </summary>
        public string ToggleText => LocalizationService.Current[isOpen ? "Main.Close" : "Main.Open"];

        /// <summary>
        /// Gets the localized open/closed status text.
        /// </summary>
        public string StatusText => LocalizationService.Current[StatusKey];

        /// <summary>
        /// Gets or sets whether this note is part of an unresolved sync conflict. Conflicted rows are
        /// pinned to the top of the list and show a banner.
        /// </summary>
        public bool IsConflicted
        {
            get => isConflicted;
            set
            {
                if (isConflicted == value)
                {
                    return;
                }

                isConflicted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ConflictStatusText));
            }
        }

        /// <summary>
        /// Gets the localized conflict status text shown when <see cref="IsConflicted"/> is set.
        /// </summary>
        public string ConflictStatusText => isConflicted ? LocalizationService.Current["Sync.Conflict.Row"] : string.Empty;

        /// <summary>
        /// Gets the localized window level text.
        /// </summary>
        public string LevelText => LocalizationService.Current[$"Window.Level.{Level}"];

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
