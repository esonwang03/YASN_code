using Microsoft.Data.Sqlite;

namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// Machine-local SQLite store for the three-way sync baseline, the pending-change queue, and the
    /// set of sync keys currently in conflict. All access is parameterized; the schema is created on
    /// first open with WAL journaling.
    /// </summary>
    public sealed class SyncStateStore : IDisposable
    {
        private readonly SqliteConnection connection;
        private readonly Lock gate = new();

        /// <summary>
        /// Opens (creating if needed) the sync database at the given path.
        /// </summary>
        /// <param name="databasePath">The SQLite file path.</param>
        public SyncStateStore(string databasePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

            string? dir = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());
            connection.Open();
            Initialize();
        }

        private void Initialize()
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS baseline(
                    sync_key TEXT PRIMARY KEY,
                    local_hash TEXT,
                    remote_etag TEXT,
                    remote_path TEXT NOT NULL,
                    last_sync_utc TEXT NOT NULL,
                    deleted INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE IF NOT EXISTS queue(
                    sync_key TEXT PRIMARY KEY,
                    op TEXT NOT NULL,
                    enqueued_utc TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS conflict(
                    sync_key TEXT PRIMARY KEY,
                    detected_utc TEXT NOT NULL,
                    note TEXT);
                CREATE TABLE IF NOT EXISTS meta(
                    k TEXT PRIMARY KEY,
                    v TEXT NOT NULL);
                """;
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Reads the baseline for a sync key, or null when none exists.
        /// </summary>
        /// <param name="syncKey">The note sync key.</param>
        /// <returns>The baseline row, or null.</returns>
        public SyncBaseline? GetBaseline(string syncKey)
        {
            lock (gate)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT sync_key, local_hash, remote_etag, remote_path, last_sync_utc, deleted FROM baseline WHERE sync_key = $k";
                command.Parameters.AddWithValue("$k", syncKey);
                using SqliteDataReader reader = command.ExecuteReader();
                return reader.Read() ? ReadBaseline(reader) : null;
            }
        }

        /// <summary>
        /// Returns all baseline rows.
        /// </summary>
        /// <returns>The baseline rows.</returns>
        public IReadOnlyList<SyncBaseline> GetAllBaselines()
        {
            lock (gate)
            {
                List<SyncBaseline> rows = new List<SyncBaseline>();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT sync_key, local_hash, remote_etag, remote_path, last_sync_utc, deleted FROM baseline";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    rows.Add(ReadBaseline(reader));
                }

                return rows;
            }
        }

        /// <summary>
        /// Inserts or updates a baseline row.
        /// </summary>
        /// <param name="baseline">The baseline to persist.</param>
        public void UpsertBaseline(SyncBaseline baseline)
        {
            ArgumentNullException.ThrowIfNull(baseline);
            lock (gate)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO baseline(sync_key, local_hash, remote_etag, remote_path, last_sync_utc, deleted)
                    VALUES($k, $h, $e, $p, $t, $d)
                    ON CONFLICT(sync_key) DO UPDATE SET
                        local_hash=$h, remote_etag=$e, remote_path=$p, last_sync_utc=$t, deleted=$d;
                    """;
                command.Parameters.AddWithValue("$k", baseline.SyncKey);
                command.Parameters.AddWithValue("$h", (object?)baseline.LocalHash ?? DBNull.Value);
                command.Parameters.AddWithValue("$e", (object?)baseline.RemoteETag ?? DBNull.Value);
                command.Parameters.AddWithValue("$p", baseline.RemotePath);
                command.Parameters.AddWithValue("$t", baseline.LastSyncUtc.ToString("O"));
                command.Parameters.AddWithValue("$d", baseline.Deleted ? 1 : 0);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Removes a baseline row.
        /// </summary>
        /// <param name="syncKey">The note sync key.</param>
        public void DeleteBaseline(string syncKey)
        {
            lock (gate)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "DELETE FROM baseline WHERE sync_key = $k";
                command.Parameters.AddWithValue("$k", syncKey);
                command.ExecuteNonQuery();
            }
        }

        private static SyncBaseline ReadBaseline(SqliteDataReader reader)
        {
            return new SyncBaseline(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.GetInt64(5) != 0);
        }

        /// <summary>
        /// Enqueues (coalescing by key) a pending local change.
        /// </summary>
        /// <param name="syncKey">The changed note's sync key.</param>
        /// <param name="operation">Either <c>upsert</c> or <c>delete</c>.</param>
        public void Enqueue(string syncKey, string operation)
        {
            lock (gate)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO queue(sync_key, op, enqueued_utc) VALUES($k, $o, $t)
                    ON CONFLICT(sync_key) DO UPDATE SET op=$o, enqueued_utc=$t;
                    """;
                command.Parameters.AddWithValue("$k", syncKey);
                command.Parameters.AddWithValue("$o", operation);
                command.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Returns all queued changes.
        /// </summary>
        /// <returns>The pending queue items.</returns>
        public IReadOnlyList<SyncQueueItem> GetQueue()
        {
            lock (gate)
            {
                List<SyncQueueItem> items = new List<SyncQueueItem>();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT sync_key, op FROM queue";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    items.Add(new SyncQueueItem(reader.GetString(0), reader.GetString(1)));
                }

                return items;
            }
        }

        /// <summary>
        /// Removes a key from the queue.
        /// </summary>
        /// <param name="syncKey">The note sync key.</param>
        public void Dequeue(string syncKey)
        {
            lock (gate)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "DELETE FROM queue WHERE sync_key = $k";
                command.Parameters.AddWithValue("$k", syncKey);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Marks a sync key as conflicted (excluded from sync until resolved).
        /// </summary>
        /// <param name="syncKey">The conflicted note's sync key.</param>
        /// <param name="note">An optional human-readable note.</param>
        public void MarkConflict(string syncKey, string? note)
        {
            lock (gate)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO conflict(sync_key, detected_utc, note) VALUES($k, $t, $n)
                    ON CONFLICT(sync_key) DO NOTHING;
                    """;
                command.Parameters.AddWithValue("$k", syncKey);
                command.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
                command.Parameters.AddWithValue("$n", (object?)note ?? DBNull.Value);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Clears the conflict flag for a sync key.
        /// </summary>
        /// <param name="syncKey">The note sync key.</param>
        public void ClearConflict(string syncKey)
        {
            lock (gate)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "DELETE FROM conflict WHERE sync_key = $k";
                command.Parameters.AddWithValue("$k", syncKey);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Returns the set of sync keys currently in conflict.
        /// </summary>
        /// <returns>The conflicted sync keys.</returns>
        public IReadOnlyCollection<string> GetConflictedKeys()
        {
            lock (gate)
            {
                HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT sync_key FROM conflict";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    keys.Add(reader.GetString(0));
                }

                return keys;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            connection.Close();
            connection.Dispose();
            SqliteConnection.ClearAllPools();
        }
    }
}
