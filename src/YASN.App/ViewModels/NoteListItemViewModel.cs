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
        private readonly Action<NoteListItemViewModel, WindowLevel>? changeLevel;
        private bool isOpen;
        private bool isConflicted;

        /// <summary>
        /// Initializes a new instance of the <see cref="NoteListItemViewModel"/> class.
        /// </summary>
        /// <param name="note">The note this row represents.</param>
        /// <param name="isOpen">Whether the note currently has an open window.</param>
        /// <param name="availableLevels">The window stacking levels selectable for this row.</param>
        /// <param name="changeLevel">A callback that persists and applies a level change, or null when level editing is unavailable.</param>
        public NoteListItemViewModel(
            AvaloniaNoteDocument note,
            bool isOpen,
            IReadOnlyList<WindowLevel>? availableLevels = null,
            Action<NoteListItemViewModel, WindowLevel>? changeLevel = null)
        {
            Note = note;
            this.isOpen = isOpen;
            AvailableLevels = availableLevels ?? new[] { WindowLevel.Normal, WindowLevel.TopMost };
            this.changeLevel = changeLevel;
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
        /// Gets the window stacking levels selectable for this note.
        /// </summary>
        public IReadOnlyList<WindowLevel> AvailableLevels { get; }

        /// <summary>
        /// Gets or sets the note window stacking level. Setting it routes through the manager callback
        /// to persist and apply the change; a no-op when the level is unchanged or no callback was
        /// supplied (e.g. the designer).
        /// </summary>
        public WindowLevel SelectedLevel
        {
            get => Note.Level;
            set
            {
                if (Note.Level == value || changeLevel is null)
                {
                    return;
                }

                changeLevel(this, value);
            }
        }

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
        public string LevelText => LocalizationService.Current[$"Window.Level.{Note.Level}"];

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
