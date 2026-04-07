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
    }
}
