using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace YASN.ViewModels
{
    /// <summary>
    /// Coordinates editable note content with persistence and preview refreshes.
    /// </summary>
    public sealed class SingleNoteViewModel : INotifyPropertyChanged
    {
        private readonly SingleNoteStore store;
        private string content;

        /// <summary>
        /// Initializes a view model from the persisted single note.
        /// </summary>
        /// <param name="store">The note store used for loading and saving.</param>
        public SingleNoteViewModel(SingleNoteStore store)
        {
            this.store = store;
            SingleNoteDocument note = store.Load();
            NoteId = note.Id;
            content = note.Content;
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
        /// Gets the persisted note identifier.
        /// </summary>
        public int NoteId { get; }

        /// <summary>
        /// Gets or sets the editable Markdown content.
        /// </summary>
        public string Content
        {
            get => content;
            set
            {
                string next = value ?? string.Empty;
                if (content == next)
                {
                    return;
                }

                content = next;
                OnPropertyChanged();
                Save();
                PreviewRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Saves the current note content.
        /// </summary>
        public void Save()
        {
            store.Save(new SingleNoteDocument(NoteId, Content));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
