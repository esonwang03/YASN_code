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
        public DateTimeOffset? GetLastFired(string noteId, string ruleId)
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
        public void SetLastFired(string noteId, string ruleId, DateTimeOffset firedAt)
        {
            lock (gate)
            {
                lastFired[Key(noteId, ruleId)] = firedAt;
                Save();
            }
        }

        /// <summary>
        /// Removes all recorded fire history for a note. Called when the note is deleted so its
        /// orphaned cron state cannot trigger a catch-up replay after a later restart.
        /// </summary>
        /// <param name="noteId">The identifier of the deleted note.</param>
        public void Remove(string noteId)
        {
            string prefix = noteId + ":";
            lock (gate)
            {
                List<string> keys = lastFired.Keys
                    .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                    .ToList();
                if (keys.Count == 0)
                {
                    return;
                }

                foreach (string key in keys)
                {
                    lastFired.Remove(key);
                }

                Save();
            }
        }

        private static string Key(string noteId, string ruleId) => $"{noteId}:{ruleId}";

        private static Dictionary<string, DateTimeOffset> Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    Dictionary<string, DateTimeOffset>? data =
                        JsonSerializer.Deserialize(json, InfrastructureJsonContext.Default.DictionaryStringDateTimeOffset);
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

                File.WriteAllText(filePath, JsonSerializer.Serialize(lastFired, InfrastructureJsonContext.Default.DictionaryStringDateTimeOffset));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort persistence; losing fire state only risks a duplicate catch-up nudge.
            }
        }
    }
}
