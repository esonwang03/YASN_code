using Avalonia.Threading;
using YASN.Application;
using YASN.AvaloniaNotes;
using YASN.Infrastructure.Reminders;
using YASN.Infrastructure.Settings;
using YASN.SettingsUi;

namespace YASN.Reminders
{
    /// <summary>
    /// Activates the note window for a fired reminder and scrolls the preview to the rule's location,
    /// over the live <see cref="NoteWindowManager"/>. Marshals to the UI thread (the scheduler fires on
    /// a timer thread) and honors the user's "activate on fire" setting; the OS toast is sent by the
    /// scheduler independently of this.
    /// </summary>
    public sealed class NoteWindowReminderActivator : IReminderActivator
    {
        private readonly INoteWindowManager windows;
        private readonly SettingsStore settings;

        /// <summary>
        /// Initializes a reminder activator.
        /// </summary>
        /// <param name="windows">The window manager used to open, focus, and scroll a note.</param>
        /// <param name="settings">The settings store read to honor the activate-on-fire toggle.</param>
        public NoteWindowReminderActivator(INoteWindowManager windows, SettingsStore settings)
        {
            this.windows = windows;
            this.settings = settings;
        }

        /// <inheritdoc/>
        public void Activate(AvaloniaNoteDocument note, NoteReminderRule? rule)
        {
            if (!IsEnabled())
            {
                return;
            }

            int? offset = rule?.SourceStart;
            Dispatcher.UIThread.Post(() => windows.ActivateForReminder(note, offset));
        }

        /// <summary>
        /// Reads the activate-on-fire toggle, defaulting to enabled when the value has never been set.
        /// </summary>
        private bool IsEnabled()
        {
            string value = settings.GetValue(SettingsSchemaBuilder.ReminderActivateOnFireKey, shouldSync: true, defaultValue: bool.TrueString);
            return !bool.TryParse(value, out bool enabled) || enabled;
        }
    }
}
