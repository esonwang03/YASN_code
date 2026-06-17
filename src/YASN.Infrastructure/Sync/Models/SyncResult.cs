namespace YASN.Infrastructure.Sync
{
    public class SyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int FilesUploaded { get; set; }
        public int FilesDownloaded { get; set; }

        /// <summary>Gets whether the pass changed local state.</summary>
        public bool Changed { get; set; }

        /// <summary>Builds a successful result.</summary>
        public static SyncResult Completed(bool changed) =>
            new SyncResult { Success = true, Changed = changed, Message = changed ? "changed" : "no-op" };

        /// <summary>Builds a skipped result (sync disabled or a pass already running).</summary>
        public static SyncResult Skipped(string reason) =>
            new SyncResult { Success = true, Changed = false, Message = reason };

        /// <summary>Builds a failed result.</summary>
        public static SyncResult Failed(string reason) =>
            new SyncResult { Success = false, Changed = false, Message = reason };
    }
}
