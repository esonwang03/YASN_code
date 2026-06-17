using YASN.Infrastructure.Sync;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// In-memory <see cref="ISyncClient"/> for engine tests: stores uploaded files keyed by remote
    /// path with a monotonically increasing ETag, so edits change the ETag like a real server.
    /// </summary>
    public sealed class FakeSyncClient : ISyncClient
    {
        private readonly Dictionary<string, (byte[] Data, string ETag)> files = new(StringComparer.Ordinal);
        private int etagCounter;

        public string BackendName => "fake";

        public Task<bool> TestConnectionAsync(string remotePath) => Task.FromResult(true);

        public Task<bool> EnsureDirectoryAsync(string remotePath) => Task.FromResult(true);

        public Task<bool> UploadFileAsync(string localFilePath, string remoteFilePath)
        {
            byte[] data = File.ReadAllBytes(localFilePath);
            files[Normalize(remoteFilePath)] = (data, $"etag-{++etagCounter}");
            return Task.FromResult(true);
        }

        public Task<bool> DownloadFileAsync(string remoteFilePath, string localFilePath)
        {
            if (!files.TryGetValue(Normalize(remoteFilePath), out (byte[] Data, string ETag) entry))
            {
                return Task.FromResult(false);
            }

            File.WriteAllBytes(localFilePath, entry.Data);
            return Task.FromResult(true);
        }

        public Task<bool> FileExistsAsync(string remoteFilePath) =>
            Task.FromResult(files.ContainsKey(Normalize(remoteFilePath)));

        public Task<DateTime?> GetFileLastModifiedAsync(string remoteFilePath) =>
            Task.FromResult<DateTime?>(null);

        public Task<string?> GetFileHashAsync(string remoteFilePath) =>
            Task.FromResult<string?>(null);

        public Task<string?> GetFileETagAsync(string remoteFilePath) =>
            Task.FromResult(files.TryGetValue(Normalize(remoteFilePath), out (byte[] Data, string ETag) entry) ? entry.ETag : null);

        public Task<IReadOnlyList<RemoteEntry>> ListDirectoryAsync(string remoteDirectoryPath)
        {
            string prefix = Normalize(remoteDirectoryPath) + "/";
            IReadOnlyList<RemoteEntry> entries = files
                .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(kvp => new RemoteEntry(kvp.Key, kvp.Value.ETag))
                .ToList();
            return Task.FromResult(entries);
        }

        /// <summary>Directly seeds a remote file (simulating another device's upload).</summary>
        public void SeedRemote(string remoteFilePath, byte[] data) =>
            files[Normalize(remoteFilePath)] = (data, $"etag-{++etagCounter}");

        /// <summary>Removes a remote file.</summary>
        public void RemoveRemote(string remoteFilePath) => files.Remove(Normalize(remoteFilePath));

        public void Dispose()
        {
        }

        private static string Normalize(string path) => path.Trim().Trim('/');
    }
}
