namespace YASN.Core
{
    /// <summary>
    /// Defines the scope in which a hotkey is dispatched.
    /// </summary>
    public enum HotkeyScope
    {
        /// <summary>System-wide: fires even when no YASN window has focus.</summary>
        Global,

        /// <summary>Editor-local: fires only while a note window is focused.</summary>
        Editor
    }

    /// <summary>
    /// Identifies a user-rebindable hotkey command.
    /// </summary>
    public enum HotkeyAction
    {
        /// <summary>Global: show or activate the note-manager window.</summary>
        RaiseMainWindow,

        /// <summary>Global: show or activate the settings window.</summary>
        RaiseSettingsWindow,

        /// <summary>Global: create a new note and open its window.</summary>
        CreateNote,

        /// <summary>Editor: insert an image at the caret.</summary>
        InsertImage,

        /// <summary>Editor: insert a file attachment at the caret.</summary>
        InsertAttachment,

        /// <summary>Editor: cycle the editor display mode.</summary>
        CycleEditorMode,

        /// <summary>Editor: cycle the window stacking level.</summary>
        CycleWindowLevel,

        /// <summary>Editor: open the quick-layout overlay.</summary>
        QuickLayout
    }
}
