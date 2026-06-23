using YASN.AvaloniaNotes;
using YASN.Infrastructure.Reminders;
using YASN.Notifications;

namespace YASN.Reminders
{
    /// <summary>
    /// Fires note reminders while the app is running and catches up reminders that came due while
    /// it was closed. Supports both the single <see cref="AvaloniaNoteDocument.ReminderAt"/> field and
    /// recurring crontab rules embedded in note content. Wake-when-fully-closed scheduling is
    /// intentionally out of scope.
    /// </summary>
    public sealed class ReminderScheduler : IDisposable
    {
        private readonly INotificationService notifications;
        private readonly ReminderStateStore? stateStore;
        private readonly Func<DateTimeOffset> clock;
        private readonly HashSet<string> firedNoteIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Timer> timers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Timer> cronTimers = new(StringComparer.Ordinal);
        private readonly Lock gate = new();

        /// <summary>
        /// Initializes a reminder scheduler.
        /// </summary>
        /// <param name="notifications">The notification service used for reminder delivery.</param>
        /// <param name="stateStore">Optional store tracking when each cron rule last fired, for catch-up.</param>
        /// <param name="clock">Optional clock override (UTC); defaults to <see cref="DateTimeOffset.UtcNow"/>.</param>
        /// <param name="activator">Optional in-app activator that focuses a note and scrolls to the reminder when a rule fires.</param>
        /// <param name="contentWriter">Optional writer that reduces a finite rule's count after it fires.</param>
        public ReminderScheduler(
            INotificationService notifications,
            ReminderStateStore? stateStore = null,
            Func<DateTimeOffset>? clock = null,
            IReminderActivator? activator = null,
            INoteContentWriter? contentWriter = null)
        {
            this.notifications = notifications;
            this.stateStore = stateStore;
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
            Activator = activator;
            ContentWriter = contentWriter;
        }

        /// <summary>
        /// Gets or sets the in-app activator invoked when a reminder fires. Settable post-construction to
        /// break the scheduler ↔ window-manager dependency cycle during startup wiring.
        /// </summary>
        public IReminderActivator? Activator { get; set; }

        /// <summary>
        /// Gets or sets the writer that reduces a finite rule's count after it fires. Settable
        /// post-construction for the same startup-wiring reason as <see cref="Presenter"/>.
        /// </summary>
        public INoteContentWriter? ContentWriter { get; set; }

        /// <summary>
        /// Sends notifications for reminders due at or before the supplied time.
        /// </summary>
        /// <param name="notes">The notes to inspect.</param>
        /// <param name="now">The current timestamp.</param>
        public async Task NotifyDueRemindersAsync(IEnumerable<AvaloniaNoteDocument> notes, DateTimeOffset now)
        {
            foreach (AvaloniaNoteDocument note in notes)
            {
                if (note.ReminderAt is { } reminderAt && reminderAt <= now && MarkFired(note.Id))
                {
                    await FireAsync(note).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Arms or clears the live timer for one note based on its reminder time.
        /// </summary>
        /// <param name="note">The note whose reminder changed.</param>
        public void Reschedule(AvaloniaNoteDocument note)
        {
            Cancel(note.Id);

            if (note.ReminderAt is not { } reminderAt)
            {
                return;
            }

            firedNoteIds.Remove(note.Id);
            TimeSpan delay = reminderAt - DateTimeOffset.UtcNow;
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            Timer timer = new Timer(_ => OnTimerFired(note), state: null, delay, Timeout.InfiniteTimeSpan);
            lock (gate)
            {
                timers[note.Id] = timer;
            }
        }

        /// <summary>
        /// Cancels and disposes the live timer for a note when present.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        public void Cancel(string noteId)
        {
            lock (gate)
            {
                if (timers.Remove(noteId, out Timer? timer))
                {
                    timer.Dispose();
                }
            }
        }

        /// <summary>
        /// Re-arms the crontab reminders declared in a note's content. Clears any existing cron timers
        /// for the note, replays at most one missed occurrence per rule (a catch-up nudge), then arms
        /// the next future occurrence of each enabled, valid rule.
        /// </summary>
        /// <param name="note">The note whose content declares the rules.</param>
        public void RescheduleCron(AvaloniaNoteDocument note)
        {
            CancelCron(note.Id);

            DateTimeOffset now = clock();
            foreach (NoteReminderRule rule in NoteReminderParser.Parse(note.Content))
            {
                if (!rule.IsSchedulable)
                {
                    continue;
                }

                // A once-rule that fired during catch-up disabled itself; do not arm a future timer.
                if (CatchUp(note, rule, now) && rule.RemainingCount <= 1)
                {
                    continue;
                }

                ArmCron(note, rule, now);
            }
        }

        /// <summary>
        /// Cancels and disposes all live crontab timers for a note.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        public void CancelCron(string noteId)
        {
            string prefix = noteId + ":";
            lock (gate)
            {
                foreach (string key in cronTimers.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                {
                    if (cronTimers.Remove(key, out Timer? timer))
                    {
                        timer.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Forgets a deleted note entirely: cancels its one-shot and crontab timers, clears its
        /// in-memory fired flag, and purges its persisted cron fire history. Without this a live
        /// timer (or a stale catch-up entry after restart) would fire for the deleted note and
        /// re-create it when the activator re-opens and re-saves it.
        /// </summary>
        /// <param name="noteId">The identifier of the deleted note.</param>
        public void Forget(string noteId)
        {
            Cancel(noteId);
            CancelCron(noteId);
            lock (gate)
            {
                firedNoteIds.Remove(noteId);
            }

            stateStore?.Remove(noteId);
        }

        private bool CatchUp(AvaloniaNoteDocument note, NoteReminderRule rule, DateTimeOffset now)
        {
            if (stateStore is null)
            {
                return false;
            }

            DateTimeOffset? lastFired = stateStore.GetLastFired(note.Id, rule.RuleId);
            if (lastFired is not { } since)
            {
                return false;
            }

            // Replay only the single most recent missed occurrence to avoid a notification flood after
            // long downtime.
            DateTimeOffset? due = rule.Schedule!.GetNextOccurrence(since);
            if (due is { } occurrence && occurrence <= now)
            {
                stateStore.SetLastFired(note.Id, rule.RuleId, now);
                Deliver(note, rule);
                if (rule.IsFinite)
                {
                    ContentWriter?.ReduceReminderCounter(note.Id, rule.RuleId);
                }

                return true;
            }

            return false;
        }

        private void ArmCron(AvaloniaNoteDocument note, NoteReminderRule rule, DateTimeOffset now)
        {
            if (rule.Schedule!.GetNextOccurrence(now) is not { } next)
            {
                return;
            }

            TimeSpan delay = next - clock();
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            string key = note.Id + ":" + rule.RuleId;
            Timer timer = new Timer(_ => OnCronTimerFired(note, rule), state: null, delay, Timeout.InfiniteTimeSpan);
            lock (gate)
            {
                if (cronTimers.Remove(key, out Timer? existing))
                {
                    existing.Dispose();
                }

                cronTimers[key] = timer;
            }
        }

        private void OnCronTimerFired(AvaloniaNoteDocument note, NoteReminderRule rule)
        {
            DateTimeOffset firedAt = clock();
            stateStore?.SetLastFired(note.Id, rule.RuleId, firedAt);
            Deliver(note, rule);

            if (rule.IsFinite)
            {
                // A finite rule counts down in the note content. Reduce its remaining count, and re-arm
                // only while fires remain; the last fire leaves a spent X-control that never re-arms.
                ContentWriter?.ReduceReminderCounter(note.Id, rule.RuleId);
                if (rule.RemainingCount > 1)
                {
                    ArmCron(note, rule, firedAt);
                }

                return;
            }

            // Re-arm for the following occurrence so the rule keeps recurring.
            ArmCron(note, rule, firedAt);
        }

        /// <summary>
        /// Sends the OS toast and, when enabled, activates the note window and scrolls to the rule.
        /// </summary>
        private void Deliver(AvaloniaNoteDocument note, NoteReminderRule rule)
        {
            _ = FireCronAsync(note, rule);
            Activator?.Activate(note, rule);
        }

        private Task<NotificationSendResult> FireCronAsync(AvaloniaNoteDocument note, NoteReminderRule rule)
        {
            string body = string.IsNullOrWhiteSpace(rule.Content) ? "Reminder" : rule.Content;
            NotificationRequest request = new NotificationRequest(note.Title, body, $"note:{note.Id}");
            return notifications.SendAsync(request);
        }

        /// <summary>
        /// Disposes all live reminder timers.
        /// </summary>
        public void Dispose()
        {
            lock (gate)
            {
                foreach (Timer timer in timers.Values)
                {
                    timer.Dispose();
                }

                foreach (Timer timer in cronTimers.Values)
                {
                    timer.Dispose();
                }

                timers.Clear();
                cronTimers.Clear();
            }
        }

        private void OnTimerFired(AvaloniaNoteDocument note)
        {
            Cancel(note.Id);
            if (MarkFired(note.Id))
            {
                _ = FireAsync(note);
            }
        }

        private bool MarkFired(string noteId)
        {
            lock (gate)
            {
                return firedNoteIds.Add(noteId);
            }
        }

        private Task<NotificationSendResult> FireAsync(AvaloniaNoteDocument note)
        {
            // A one-shot ReminderAt has no in-note anchor, so the activator focuses the note without
            // scrolling (rule: null).
            Activator?.Activate(note, null);
            NotificationRequest request = new NotificationRequest(note.Title, "Reminder", $"note:{note.Id}");
            return notifications.SendAsync(request);
        }
    }
}
