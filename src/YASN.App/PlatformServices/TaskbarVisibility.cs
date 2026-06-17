using YASN.Core;

namespace YASN.PlatformServices
{
    /// <summary>
    /// The three taskbar visibility modes for note windows, matching the legacy desktop behavior.
    /// </summary>
    public enum TaskbarVisibilityMode
    {
        /// <summary>Always show the note in the taskbar.</summary>
        AlwaysShow,

        /// <summary>Never show the note in the taskbar.</summary>
        AlwaysHide,

        /// <summary>Show the note in the taskbar unless it is topmost.</summary>
        HideTopMostOnly
    }

    /// <summary>
    /// Pure logic for the global note taskbar-visibility setting: parsing the persisted string and
    /// deciding whether a window at a given level should appear in the taskbar.
    /// </summary>
    public static class TaskbarVisibility
    {
        /// <summary>The persisted value for <see cref="TaskbarVisibilityMode.AlwaysShow"/>.</summary>
        public const string AlwaysShowValue = "ALWAYSSHOW";

        /// <summary>The persisted value for <see cref="TaskbarVisibilityMode.AlwaysHide"/>.</summary>
        public const string AlwaysHideValue = "ALWAYSHIDE";

        /// <summary>The persisted value for <see cref="TaskbarVisibilityMode.HideTopMostOnly"/>.</summary>
        public const string HideTopMostOnlyValue = "HIDETOPMOSTONLY";

        /// <summary>The synced settings key for the taskbar visibility mode.</summary>
        public const string SettingKey = "floatingWindow.taskbarVisibility";

        /// <summary>The default mode, preserving the legacy "hidden from taskbar" behavior.</summary>
        public const TaskbarVisibilityMode Default = TaskbarVisibilityMode.AlwaysHide;

        /// <summary>
        /// Parses a persisted value into a mode, falling back to <see cref="Default"/> when unknown.
        /// </summary>
        /// <param name="value">The persisted string value.</param>
        /// <returns>The parsed mode.</returns>
        public static TaskbarVisibilityMode ParseMode(string? value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                AlwaysShowValue => TaskbarVisibilityMode.AlwaysShow,
                AlwaysHideValue => TaskbarVisibilityMode.AlwaysHide,
                HideTopMostOnlyValue => TaskbarVisibilityMode.HideTopMostOnly,
                _ => Default
            };
        }

        /// <summary>
        /// Decides whether a window at the given level should appear in the taskbar.
        /// </summary>
        /// <param name="level">The window stacking level.</param>
        /// <param name="mode">The configured visibility mode.</param>
        /// <returns><see langword="true"/> when the window should show in the taskbar.</returns>
        public static bool ShouldShowInTaskbar(WindowLevel level, TaskbarVisibilityMode mode)
        {
            return mode switch
            {
                TaskbarVisibilityMode.AlwaysShow => true,
                TaskbarVisibilityMode.HideTopMostOnly => level != WindowLevel.TopMost,
                _ => false
            };
        }
    }
}
