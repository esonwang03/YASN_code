using YASN.AvaloniaNotes;
using YASN.Infrastructure.Reminders;
using YASN.Notifications;
using YASN.Reminders;

namespace YASN.Migration.Tests
{
    /// <summary>Verifies crontab reminder scheduling: live firing, catch-up, disabling, and cancel.</summary>
    public sealed class ReminderSchedulerCronTests : IDisposable
    {
        private readonly string statePath = Path.Combine(
            Path.GetTempPath(), "yasn-cron-sched", Guid.NewGuid().ToString("N"), "state.json");

        public void Dispose()
        {
            string? dir = Path.GetDirectoryName(statePath);
            if (dir is not null && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task EnabledRuleFiresThroughTimer()
        {
            RecordingNotificationService notifications = new RecordingNotificationService();
            ReminderStateStore state = new ReminderStateStore(statePath);
            using ReminderScheduler scheduler = new ReminderScheduler(notifications, state);

            // Every-second cron fires within ~1s through the live timer path.
            AvaloniaNoteDocument note = new AvaloniaNoteDocument
            {
                Id = "1",
                Content = "[!tick][]{* * * * * *}{ping}"
            };

            scheduler.RescheduleCron(note);
            await WaitForAsync(() => notifications.Requests.Count > 0);

            NotificationRequest request = notifications.Requests[0];
            Assert.Equal("ping", request.Body);
            Assert.Equal("note:1", request.ActivationArgument);
        }

        [Fact]
        public async Task DisabledRuleNeverFires()
        {
            RecordingNotificationService notifications = new RecordingNotificationService();
            ReminderStateStore state = new ReminderStateStore(statePath);
            using ReminderScheduler scheduler = new ReminderScheduler(notifications, state);

            AvaloniaNoteDocument note = new AvaloniaNoteDocument
            {
                Id = "2",
                Content = "[!off][X]{* * * * * *}{nope}"
            };

            scheduler.RescheduleCron(note);
            await Task.Delay(1200).ConfigureAwait(true);

            Assert.Empty(notifications.Requests);
        }

        [Fact]
        public void CatchUpFiresOnceForMissedOccurrence()
        {
            RecordingNotificationService notifications = new RecordingNotificationService();
            ReminderStateStore state = new ReminderStateStore(statePath);

            // Rule fired "yesterday"; a daily 09:00 occurrence has since been missed.
            AvaloniaNoteDocument note = new AvaloniaNoteDocument
            {
                Id = "5",
                Content = "[!daily][]{0 9 * * *}{standup}"
            };
            string ruleId = NoteReminderParser.Parse(note.Content)[0].RuleId;
            state.SetLastFired("5", ruleId, new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

            // "Now" is two days later at noon, so 2026-01-02 09:00 was missed.
            DateTimeOffset now = new DateTimeOffset(2026, 1, 3, 12, 0, 0, TimeSpan.Zero);
            using ReminderScheduler scheduler = new ReminderScheduler(notifications, state, () => now);

            scheduler.RescheduleCron(note);

            NotificationRequest request = Assert.Single(notifications.Requests);
            Assert.Equal("standup", request.Body);
        }

        [Fact]
        public void NoCatchUpWithoutPriorFireState()
        {
            RecordingNotificationService notifications = new RecordingNotificationService();
            ReminderStateStore state = new ReminderStateStore(statePath);
            DateTimeOffset now = new DateTimeOffset(2026, 1, 3, 12, 0, 0, TimeSpan.Zero);
            using ReminderScheduler scheduler = new ReminderScheduler(notifications, state, () => now);

            AvaloniaNoteDocument note = new AvaloniaNoteDocument
            {
                Id = "6",
                Content = "[!daily][]{0 9 * * *}{standup}"
            };

            scheduler.RescheduleCron(note);

            // Never fired before => nothing to catch up; the future timer is armed but not invoked here.
            Assert.Empty(notifications.Requests);
        }

        [Fact]
        public async Task CancelCronStopsDelivery()
        {
            RecordingNotificationService notifications = new RecordingNotificationService();
            ReminderStateStore state = new ReminderStateStore(statePath);
            using ReminderScheduler scheduler = new ReminderScheduler(notifications, state);

            AvaloniaNoteDocument note = new AvaloniaNoteDocument
            {
                Id = "8",
                Content = "[!tick][]{* * * * * *}{ping}"
            };

            scheduler.RescheduleCron(note);
            scheduler.CancelCron(note.Id);
            await Task.Delay(1300).ConfigureAwait(true);

            Assert.Empty(notifications.Requests);
        }

        [Fact]
        public async Task OnceRuleFiresExactlyOnceAndDisablesItself()
        {
            RecordingNotificationService notifications = new RecordingNotificationService();
            RecordingContentWriter writer = new RecordingContentWriter();
            RecordingActivator activator = new RecordingActivator();
            ReminderStateStore state = new ReminderStateStore(statePath);
            using ReminderScheduler scheduler = new ReminderScheduler(notifications, state)
            {
                ContentWriter = writer,
                Activator = activator
            };

            AvaloniaNoteDocument note = new AvaloniaNoteDocument
            {
                Id = "10",
                Content = "[!tick][1]{* * * * * *}{once ping}"
            };
            string ruleId = NoteReminderParser.Parse(note.Content)[0].RuleId;

            scheduler.RescheduleCron(note);
            await WaitForAsync(() => writer.Reduced.Count > 0);

            // Wait out another two cron seconds to prove it does not re-fire.
            await Task.Delay(2200).ConfigureAwait(true);

            Assert.Single(notifications.Requests);
            Assert.Equal("once ping", notifications.Requests[0].Body);
            Assert.Equal(("10", ruleId), Assert.Single(writer.Reduced));
            Assert.Single(activator.Activated);
        }

        [Fact]
        public void CatchUpOnceRuleDisablesAndDoesNotArm()
        {
            RecordingNotificationService notifications = new RecordingNotificationService();
            RecordingContentWriter writer = new RecordingContentWriter();
            ReminderStateStore state = new ReminderStateStore(statePath);

            AvaloniaNoteDocument note = new AvaloniaNoteDocument
            {
                Id = "11",
                Content = "[!daily once][1]{0 9 * * *}{meds}"
            };
            string ruleId = NoteReminderParser.Parse(note.Content)[0].RuleId;
            state.SetLastFired("11", ruleId, new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

            DateTimeOffset now = new DateTimeOffset(2026, 1, 3, 12, 0, 0, TimeSpan.Zero);
            using ReminderScheduler scheduler = new ReminderScheduler(notifications, state, () => now)
            {
                ContentWriter = writer
            };

            scheduler.RescheduleCron(note);

            Assert.Single(notifications.Requests);
            Assert.Equal(("11", ruleId), Assert.Single(writer.Reduced));
        }

        [Fact]
        public async Task ForgetStopsCronDelivery()
        {
            RecordingNotificationService notifications = new RecordingNotificationService();
            ReminderStateStore state = new ReminderStateStore(statePath);
            using ReminderScheduler scheduler = new ReminderScheduler(notifications, state);

            AvaloniaNoteDocument note = new AvaloniaNoteDocument
            {
                Id = "12",
                Content = "[!tick][]{* * * * * *}{ping}"
            };

            scheduler.RescheduleCron(note);
            scheduler.Forget(note.Id);
            await Task.Delay(1300).ConfigureAwait(true);

            Assert.Empty(notifications.Requests);
        }

        [Fact]
        public void ForgetPurgesCatchUpStateSoDeletedNoteNeverReplays()
        {
            RecordingNotificationService notifications = new RecordingNotificationService();
            ReminderStateStore state = new ReminderStateStore(statePath);

            // A note that fired "yesterday" would normally catch up its missed 09:00 occurrence.
            AvaloniaNoteDocument note = new AvaloniaNoteDocument
            {
                Id = "13",
                Content = "[!daily][]{0 9 * * *}{standup}"
            };
            string ruleId = NoteReminderParser.Parse(note.Content)[0].RuleId;
            state.SetLastFired("13", ruleId, new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

            DateTimeOffset now = new DateTimeOffset(2026, 1, 3, 12, 0, 0, TimeSpan.Zero);
            using ReminderScheduler scheduler = new ReminderScheduler(notifications, state, () => now);

            // Deleting the note forgets its reminders. Re-arming (as a restart's RescheduleCron would)
            // must then find no fire history and replay nothing — the note stays deleted.
            scheduler.Forget(note.Id);
            scheduler.RescheduleCron(note);

            Assert.Empty(notifications.Requests);
            Assert.Null(state.GetLastFired("13", ruleId));
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            for (int attempt = 0; attempt < 100 && !condition(); attempt++)
            {
                await Task.Delay(20).ConfigureAwait(true);
            }
        }

        private sealed class RecordingNotificationService : INotificationService
        {
            internal List<NotificationRequest> Requests { get; } = new();

            public bool IsSupported => true;

            public Task<NotificationSendResult> SendAsync(NotificationRequest request)
            {
                lock (Requests)
                {
                    Requests.Add(request);
                }

                return Task.FromResult(NotificationSendResult.Sent);
            }
        }

        private sealed class RecordingContentWriter : INoteContentWriter
        {
            internal List<(string NoteId, string RuleId)> Reduced { get; } = new();

            public void ReduceReminderCounter(string noteId, string ruleId)
            {
                lock (Reduced)
                {
                    Reduced.Add((noteId, ruleId));
                }
            }

            public void SetTaskChecked(string noteId, int sourceLine, bool isChecked)
            {
            }
        }

        private sealed class RecordingActivator : IReminderActivator
        {
            internal List<string> Activated { get; } = new();

            public void Activate(AvaloniaNoteDocument note, NoteReminderRule? rule)
            {
                lock (Activated)
                {
                    Activated.Add(note.Id);
                }
            }
        }
    }
}
