namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// Base contract for sync backends so different providers can share the same orchestration code.
    /// Remote paths are treated as relative paths (no leading slash) to keep URI composition backend-specific.
    /// </summary>
    public interface ISyncClient : IDisposable
    {
        string BackendName { get; }

        Task<bool> TestConnectionAsync(string remotePath);
        Task<bool> EnsureDirectoryAsync(string remotePath);
        Task<bool> UploadFileAsync(string localFilePath, string remoteFilePath);
        Task<bool> DownloadFileAsync(string remoteFilePath, string localFilePath);
        Task<bool> FileExistsAsync(string remoteFilePath);
        Task<DateTime?> GetFileLastModifiedAsync(string remoteFilePath);
        Task<string?> GetFileHashAsync(string remoteFilePath);

        /// <summary>
        /// Gets the entity tag (ETag) for a remote file, or null when missing or unsupported.
        /// </summary>
        /// <param name="remoteFilePath">The relative remote file path.</param>
        Task<string?> GetFileETagAsync(string remoteFilePath);

        /// <summary>
        /// Lists immediate child file entries under a remote directory (depth 1), returning each
        /// child's relative path and ETag. Collections are excluded.
        /// </summary>
        /// <param name="remoteDirectoryPath">The relative remote directory path.</param>
        Task<IReadOnlyList<RemoteEntry>> ListDirectoryAsync(string remoteDirectoryPath);
    }

    /// <summary>
    /// One remote file entry returned by <see cref="ISyncClient.ListDirectoryAsync"/>.
    /// </summary>
    /// <param name="RelativePath">The remote path relative to the configured remote root.</param>
    /// <param name="ETag">The entity tag, or null when the server omits it.</param>
    public sealed record RemoteEntry(string RelativePath, string? ETag);
}
