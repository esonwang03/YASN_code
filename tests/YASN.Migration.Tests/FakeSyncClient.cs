using YASN.Infrastructure.Sync;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// In-memory <see cref="ISyncClient"/> for engine tests: stores uploaded files keyed by remote
    /// path with a monotonically increasing ETag, so edits change the ETag like a real server.
    /// </summary>
    public sealed class FakeSyncClient : ISyncClient
    {
        private readonly Dictionary<string, (byte[] Data, string ETag, DateTimeOffset LastModified)> files = new(StringComparer.Ordinal);
        private int etagCounter;

        public string BackendName => "fake";

        /// <summary>
        /// When true, <see cref="ListDirectoryAsync"/> reports a failed listing (as a real client does
        /// on a transient error or unreadable directory) rather than an empty success.
        /// </summary>
        public bool FailListing { get; set; }

        /// <summary>
        /// When true, <see cref="EnsureDirectoryAsync"/> reports failure.
        /// </summary>
        public bool FailEnsureDirectory { get; set; }

        /// <summary>
        /// When true, the client behaves like a WebDAV server that omits ETags: listing entries and
        /// <see cref="GetFileETagAsync"/> both return null. Exercises the engine's ETag normalization.
        /// </summary>
        public bool SuppressETags { get; set; }

        public Task<bool> TestConnectionAsync(string remotePath) => Task.FromResult(true);

        public Task<bool> EnsureDirectoryAsync(string remotePath) => Task.FromResult(!FailEnsureDirectory);

        public Task<bool> UploadFileAsync(string localFilePath, string remoteFilePath)
        {
            byte[] data = File.ReadAllBytes(localFilePath);
            Store(remoteFilePath, data);
            return Task.FromResult(true);
        }

        public Task<bool> DownloadFileAsync(string remoteFilePath, string localFilePath)
        {
            if (!files.TryGetValue(Normalize(remoteFilePath), out (byte[] Data, string ETag, DateTimeOffset LastModified) entry))
            {
                return Task.FromResult(false);
            }

            File.WriteAllBytes(localFilePath, entry.Data);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteFileAsync(string remoteFilePath) =>
            Task.FromResult(files.Remove(Normalize(remoteFilePath)) || true);

        public Task<bool> FileExistsAsync(string remoteFilePath) =>
            Task.FromResult(files.ContainsKey(Normalize(remoteFilePath)));

        public Task<DateTime?> GetFileLastModifiedAsync(string remoteFilePath) =>
            Task.FromResult(files.TryGetValue(Normalize(remoteFilePath), out (byte[] Data, string ETag, DateTimeOffset LastModified) entry)
                ? entry.LastModified.UtcDateTime
                : (DateTime?)null);

        public Task<string?> GetFileHashAsync(string remoteFilePath) =>
            Task.FromResult<string?>(null);

        public Task<string?> GetFileETagAsync(string remoteFilePath) =>
            Task.FromResult(SuppressETags
                ? null
                : files.TryGetValue(Normalize(remoteFilePath), out (byte[] Data, string ETag, DateTimeOffset LastModified) entry) ? entry.ETag : null);

        public Task<bool?> SupportsETagsAsync(string remoteDirectoryPath) =>
            Task.FromResult<bool?>(!SuppressETags);

        public Task<RemoteListing> ListDirectoryAsync(string remoteDirectoryPath)
        {
            if (FailListing)
            {
                return Task.FromResult(RemoteListing.Failure());
            }

            string prefix = Normalize(remoteDirectoryPath) + "/";
            IReadOnlyList<RemoteEntry> entries = files
                .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(kvp => new RemoteEntry(kvp.Key, SuppressETags ? null : kvp.Value.ETag, kvp.Value.LastModified))
                .ToList();
            return Task.FromResult(RemoteListing.Success(entries));
        }

        /// <summary>Directly seeds a remote file (simulating another device's upload).</summary>
        public void SeedRemote(string remoteFilePath, byte[] data) => Store(remoteFilePath, data);

        /// <summary>Removes a remote file.</summary>
        public void RemoveRemote(string remoteFilePath) => files.Remove(Normalize(remoteFilePath));

        public void Dispose()
        {
        }

        // Each write advances a counter that drives both the ETag and a monotonic Last-Modified stamp,
        // so an edit changes both validators like a real server (no wall-clock dependency in tests).
        private void Store(string remoteFilePath, byte[] data)
        {
            int version = ++etagCounter;
            DateTimeOffset stamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(version);
            files[Normalize(remoteFilePath)] = (data, $"etag-{version}", stamp);
        }

        private static string Normalize(string path) => path.Trim().Trim('/');
    }
}
