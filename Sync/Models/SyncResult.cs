namespace YASN.Sync
{
    public class SyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int FilesUploaded { get; set; }
        public int FilesDownloaded { get; set; }
    }
}
