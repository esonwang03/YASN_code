namespace YASN.Infrastructure.Sync
{
    public class SyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int FilesUploaded { get; set; }
        public int FilesDownloaded { get; set; }

        /// <summary>Gets the number of notes deleted (local or remote) during the pass.</summary>
        public int FilesDeleted { get; set; }

        /// <summary>Gets whether the pass changed local state.</summary>
        public bool Changed { get; set; }

        /// <summary>Builds a successful result with per-action counts.</summary>
        public static SyncResult Completed(bool changed, int uploaded = 0, int downloaded = 0, int deleted = 0) =>
            new SyncResult
            {
                Success = true,
                Changed = changed,
                FilesUploaded = uploaded,
                FilesDownloaded = downloaded,
                FilesDeleted = deleted,
                Message = changed ? "changed" : "no-op"
            };

        /// <summary>Builds a skipped result (sync disabled or a pass already running).</summary>
        public static SyncResult Skipped(string reason) =>
            new SyncResult { Success = true, Changed = false, Message = reason };

        /// <summary>Builds a failed result.</summary>
        public static SyncResult Failed(string reason) =>
            new SyncResult { Success = false, Changed = false, Message = reason };
    }
}
