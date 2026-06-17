using YASN.Infrastructure.Reminders;

namespace YASN.Migration.Tests
{
    /// <summary>Verifies the machine-local reminder fire-state store.</summary>
    public sealed class ReminderStateStoreTests : IDisposable
    {
        private readonly string path = Path.Combine(
            Path.GetTempPath(), "yasn-reminder-state", Guid.NewGuid().ToString("N"), "state.json");

        public void Dispose()
        {
            string? dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void UnsetRuleReturnsNull()
        {
            ReminderStateStore store = new ReminderStateStore(path);
            Assert.Null(store.GetLastFired(1, "abc"));
        }

        [Fact]
        public void RoundTripsAcrossInstances()
        {
            DateTimeOffset fired = new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero);
            new ReminderStateStore(path).SetLastFired(3, "rule1", fired);

            ReminderStateStore reopened = new ReminderStateStore(path);
            Assert.Equal(fired, reopened.GetLastFired(3, "rule1"));
        }

        [Fact]
        public void CorruptFileIsToleratedAsEmpty()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ this is not valid json");

            ReminderStateStore store = new ReminderStateStore(path);
            Assert.Null(store.GetLastFired(1, "x"));
        }

        [Fact]
        public void DistinguishesNotesAndRules()
        {
            DateTimeOffset t = DateTimeOffset.UtcNow;
            ReminderStateStore store = new ReminderStateStore(path);
            store.SetLastFired(1, "a", t);

            Assert.NotNull(store.GetLastFired(1, "a"));
            Assert.Null(store.GetLastFired(1, "b"));
            Assert.Null(store.GetLastFired(2, "a"));
        }
    }
}
