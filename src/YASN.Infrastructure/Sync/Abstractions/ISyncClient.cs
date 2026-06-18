namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// Base contract for sync backends so different providers can share the same orchestration code.
    /// Remote paths are treated as relative paths (no leading slash) to keep URI composition backend-specific.
    /// </summary>
    public interface ISyncClient : IDisposable
    {
        string BackendName { get; }

        /// <summary>
        /// Verifies that the configured backend is actually usable, not merely reachable. Classifies the
        /// endpoint/credentials via a metadata request, then proves read/write by round-tripping a small
        /// probe file (upload, download, byte-compare) under <paramref name="remotePath"/> and reports
        /// whether the server returned a usable ETag for it. See <see cref="SyncProbeResult"/>.
        /// </summary>
        /// <param name="remotePath">The relative remote directory the probe writes within.</param>
        Task<SyncProbeResult> ProbeConnectionAsync(string remotePath);
        Task<bool> EnsureDirectoryAsync(string remotePath);
        Task<bool> UploadFileAsync(string localFilePath, string remoteFilePath);
        Task<bool> DownloadFileAsync(string remoteFilePath, string localFilePath);

        /// <summary>
        /// Deletes a remote file. Returns true on success or when the file was already absent. Used for
        /// transient helper files (e.g. the ETag-capability probe), not for note tombstoning.
        /// </summary>
        /// <param name="remoteFilePath">The relative remote file path.</param>
        Task<bool> DeleteFileAsync(string remoteFilePath);

        Task<bool> FileExistsAsync(string remoteFilePath);
        Task<DateTime?> GetFileLastModifiedAsync(string remoteFilePath);
        Task<string?> GetFileHashAsync(string remoteFilePath);

        /// <summary>
        /// Gets the entity tag (ETag) for a remote file, or null when missing or unsupported.
        /// </summary>
        /// <param name="remoteFilePath">The relative remote file path.</param>
        Task<string?> GetFileETagAsync(string remoteFilePath);

        /// <summary>
        /// Probes whether the server returns usable ETags, by writing a small temporary file under the
        /// given directory, reading back its raw ETag, and deleting it. Returns true when a non-empty
        /// ETag came back, false when the server omitted it, and null when the probe could not run
        /// (e.g. the upload failed) so the caller cannot conclude either way.
        /// </summary>
        /// <param name="remoteDirectoryPath">The relative remote directory to probe within.</param>
        Task<bool?> SupportsETagsAsync(string remoteDirectoryPath);

        /// <summary>
        /// Lists immediate child file entries under a remote directory (depth 1), returning each
        /// child's relative path and ETag. Collections are excluded.
        /// </summary>
        /// <remarks>
        /// The result carries an explicit <see cref="RemoteListing.Ok"/> flag so callers can tell a
        /// genuinely empty directory (<c>Ok=true, []</c>) from a failed listing (<c>Ok=false</c>).
        /// This distinction is load-bearing: the sync engine must never infer remote deletions from a
        /// listing it could not actually read, or it would wipe local notes on a transient error.
        /// </remarks>
        /// <param name="remoteDirectoryPath">The relative remote directory path.</param>
        Task<RemoteListing> ListDirectoryAsync(string remoteDirectoryPath);
    }

    /// <summary>
    /// One remote file entry returned by <see cref="ISyncClient.ListDirectoryAsync"/>.
    /// </summary>
    /// <param name="RelativePath">The remote path relative to the configured remote root.</param>
    /// <param name="ETag">The entity tag, or null when the server omits it.</param>
    /// <param name="LastModified">
    /// The server's Last-Modified timestamp, or null when unavailable. Used for change detection when
    /// the server omits ETags (see <see cref="ChangeDetectionMode.LastModified"/>).
    /// </param>
    public sealed record RemoteEntry(string RelativePath, string? ETag, DateTimeOffset? LastModified = null);

    /// <summary>
    /// The outcome of <see cref="ISyncClient.ListDirectoryAsync"/>: whether the listing was actually
    /// read from the server, and the child entries when it was.
    /// </summary>
    /// <param name="Ok">
    /// <see langword="true"/> when the directory was read successfully (an empty <see cref="Entries"/>
    /// then means the directory is genuinely empty); <see langword="false"/> when the listing failed
    /// or could not be performed, in which case <see cref="Entries"/> says nothing about the remote.
    /// </param>
    /// <param name="Entries">The child file entries; empty when the listing failed.</param>
    public sealed record RemoteListing(bool Ok, IReadOnlyList<RemoteEntry> Entries)
    {
        /// <summary>Builds a successful listing from its entries.</summary>
        public static RemoteListing Success(IReadOnlyList<RemoteEntry> entries) => new(true, entries);

        /// <summary>Builds a failed listing carrying no entries.</summary>
        public static RemoteListing Failure() => new(false, Array.Empty<RemoteEntry>());
    }

    /// <summary>
    /// The classified outcome of <see cref="ISyncClient.ProbeConnectionAsync"/>. Each value names a
    /// distinct failure the user must act on differently, so the settings UI can give specific advice
    /// instead of a single "connection failed".
    /// </summary>
    public enum SyncProbeStatus
    {
        /// <summary>The endpoint, credentials, and a file read/write round-trip all succeeded.</summary>
        Ok,

        /// <summary>The server rejected the credentials (HTTP 401).</summary>
        BadCredentials,

        /// <summary>The server reached but WebDAV is not enabled/permitted for this account (HTTP 403).</summary>
        WebDavDisabled,

        /// <summary>No WebDAV endpoint at the configured URL (e.g. HTTP 404/405): the URL is likely wrong.</summary>
        EndpointNotFound,

        /// <summary>The remote directory could not be created or prepared for writing.</summary>
        DirectoryUnavailable,

        /// <summary>The endpoint answered but a write/read round-trip failed, so sync would not work.</summary>
        ReadWriteFailed,

        /// <summary>The server could not be reached at all (DNS, TLS, timeout, refused connection).</summary>
        Unreachable
    }

    /// <summary>
    /// The result of <see cref="ISyncClient.ProbeConnectionAsync"/>: the classified status, whether the
    /// server returned a usable ETag for the probe file (only meaningful when <see cref="Status"/> is
    /// <see cref="SyncProbeStatus.Ok"/>), and an optional backend detail string for logging.
    /// </summary>
    /// <param name="Status">The classified probe outcome.</param>
    /// <param name="ServerReturnsETags">
    /// <see langword="true"/> when the round-tripped probe file came back with a non-empty ETag;
    /// <see langword="false"/> when the server omitted it. Undefined unless <see cref="Status"/> is Ok.
    /// </param>
    /// <param name="Detail">Optional backend-specific message (status code, exception text) for logs.</param>
    public sealed record SyncProbeResult(SyncProbeStatus Status, bool ServerReturnsETags, string? Detail = null)
    {
        /// <summary>True only when the backend is fully usable for sync.</summary>
        public bool IsUsable => Status == SyncProbeStatus.Ok;
    }
}
