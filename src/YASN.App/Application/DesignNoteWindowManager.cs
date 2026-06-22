using YASN.AvaloniaNotes;
using YASN.Core;

namespace YASN.Application
{
    /// <summary>
    /// A no-op <see cref="INoteWindowManager"/> used only by the XAML designer constructor so the
    /// manager window can be previewed without the real window lifecycle.
    /// </summary>
    public sealed class DesignNoteWindowManager : INoteWindowManager
    {
        /// <inheritdoc />
        public event EventHandler? NotesChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public bool IsOpen(string noteId) => false;

        /// <inheritdoc />
        public void Open(AvaloniaNoteDocument note)
        {
        }

        /// <inheritdoc />
        public void Close(string noteId)
        {
        }

        /// <inheritdoc />
        public void ApplyLevel(string noteId, WindowLevel level)
        {
        }

        /// <inheritdoc />
        public void ShowQuickLayout(AvaloniaNoteDocument note)
        {
        }

        /// <inheritdoc />
        public void ActivateForReminder(AvaloniaNoteDocument note, int? sourceOffset)
        {
        }

        /// <inheritdoc />
        public void RefreshTaskbarVisibilityForAll()
        {
        }

        /// <inheritdoc />
        public void RefreshPreviewForAll()
        {
        }

        /// <inheritdoc />
        public void SetOpenMainWindowAction(Action? action)
        {
        }

        /// <inheritdoc />
        public bool TryApplyExternalContent(string noteId, string content) => false;

        /// <inheritdoc />
        public bool TryEditContent(string noteId, Func<string, string?> transform) => false;
    }
}
