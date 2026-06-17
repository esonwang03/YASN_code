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
        public bool IsOpen(int noteId) => false;

        /// <inheritdoc />
        public void Open(AvaloniaNoteDocument note)
        {
        }

        /// <inheritdoc />
        public void Close(int noteId)
        {
        }

        /// <inheritdoc />
        public void ApplyLevel(int noteId, WindowLevel level)
        {
        }

        /// <inheritdoc />
        public void ShowQuickLayout(AvaloniaNoteDocument note)
        {
        }

        /// <inheritdoc />
        public void RefreshTaskbarVisibilityForAll()
        {
        }

        /// <inheritdoc />
        public void SetOpenMainWindowAction(Action? action)
        {
        }

        /// <inheritdoc />
        public bool TryApplyExternalContent(int noteId, string content) => false;
    }
}
