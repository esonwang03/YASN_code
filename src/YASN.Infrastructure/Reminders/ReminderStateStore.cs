using System.Text.Json;

namespace YASN.Infrastructure.Reminders
{
    /// <summary>
    /// Persists the last time each note reminder rule fired, so recurring reminders can replay a
    /// missed occurrence after the app restarts. Machine-local and intentionally excluded from sync:
    /// each device tracks its own delivery. Stored as a small JSON map keyed by
    /// <c>"{noteId}:{ruleId}"</c>.
    /// </summary>
    public sealed class ReminderStateStore
    {
        private readonly string filePath;
        private readonly Lock gate = new();
        private readonly Dictionary<string, DateTimeOffset> lastFired;

        /// <summary>
        /// Loads (or starts) the reminder state at the given path.
        /// </summary>
        /// <param name="filePath">The JSON file backing the store.</param>
        public ReminderStateStore(string filePath)
        {
            this.filePath = filePath;
            lastFired = Load(filePath);
        }

        /// <summary>
        /// Gets the last time the rule fired, or <see langword="null"/> when it never has.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        /// <param name="ruleId">The stable rule identifier.</param>
        public DateTimeOffset? GetLastFired(int noteId, string ruleId)
        {
            lock (gate)
            {
                return lastFired.TryGetValue(Key(noteId, ruleId), out DateTimeOffset value) ? value : null;
            }
        }

        /// <summary>
        /// Records that the rule fired at <paramref name="firedAt"/> and persists the change.
        /// </summary>
        /// <param name="noteId">The note identifier.</param>
        /// <param name="ruleId">The stable rule identifier.</param>
        /// <param name="firedAt">The fire timestamp.</param>
        public void SetLastFired(int noteId, string ruleId, DateTimeOffset firedAt)
        {
            lock (gate)
            {
                lastFired[Key(noteId, ruleId)] = firedAt;
                Save();
            }
        }

        private static string Key(int noteId, string ruleId) => $"{noteId}:{ruleId}";

        private static Dictionary<string, DateTimeOffset> Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    Dictionary<string, DateTimeOffset>? data =
                        JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(json);
                    if (data is not null)
                    {
                        return new Dictionary<string, DateTimeOffset>(data, StringComparer.Ordinal);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Corrupt or unreadable state is non-fatal: start fresh.
            }

            return new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        }

        private void Save()
        {
            try
            {
                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(filePath, JsonSerializer.Serialize(lastFired));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort persistence; losing fire state only risks a duplicate catch-up nudge.
            }
        }
    }
}
