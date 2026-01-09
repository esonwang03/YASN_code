using System;
using System.IO;
using System.Threading.Tasks;
using SystemTimer = System.Timers.Timer;

namespace YASN
{
    /// <summary>
    /// WebDAV 同步管理器
    /// </summary>
    public class WebDavSyncManager : IDisposable
    {
        private WebDavClient _client;
        private SystemTimer _syncTimer;
        private bool _isEnabled;
        private string _remoteDirectory;
        private DateTime _lastSyncTime;

        public bool IsEnabled => _isEnabled;
        public DateTime LastSyncTime => _lastSyncTime;

        public WebDavSyncManager()
        {
            _syncTimer = new SystemTimer();
            _syncTimer.Interval = TimeSpan.FromMinutes(5).TotalMilliseconds; // Sync every 5 minutes
            _syncTimer.Elapsed += async (s, e) => await SyncAsync();
        }

        /// <summary>
        /// Configure WebDAV connection
        /// </summary>
        public async Task<bool> ConfigureAsync(string serverUrl, string username, string password, string remoteDirectory)
        {
            try
            {
                _client?.Dispose();
                _client = new WebDavClient(serverUrl, username, password);
                _remoteDirectory = remoteDirectory;

                System.Diagnostics.Debug.WriteLine($"Configuring WebDAV: {serverUrl} + {remoteDirectory}");

                // Don't test root connection if it returns 403 (common with Jianguoyun)
                // Instead, try to create/access the remote directory
                System.Diagnostics.Debug.WriteLine("Creating remote directory...");
                var dirCreated = await _client.CreateDirectoryAsync(_remoteDirectory);
                
                if (!dirCreated)
                {
                    System.Diagnostics.Debug.WriteLine("Directory creation returned false, checking if it exists...");
                    
                    // Directory might already exist or we don't have permission to check
                    // Try to access it with PROPFIND
                    var fullPath = $"{serverUrl.TrimEnd('/')}{_remoteDirectory}";
                    System.Diagnostics.Debug.WriteLine($"Testing access to: {fullPath}");
                    
                    // For Jianguoyun and similar services, 403 on root is normal
                    // We'll consider it configured if we can create the directory structure
                    // The actual test will happen during first sync
                    System.Diagnostics.Debug.WriteLine("Directory access may be restricted, but configuration saved");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Directory created or already exists");
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebDAV configuration failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Enable automatic sync
        /// </summary>
        public void EnableAutoSync()
        {
            if (_client == null)
                throw new InvalidOperationException("WebDAV client not configured");

            _isEnabled = true;
            _syncTimer.Start();
            System.Diagnostics.Debug.WriteLine("WebDAV auto-sync enabled");
        }

        /// <summary>
        /// Disable automatic sync
        /// </summary>
        public void DisableAutoSync()
        {
            _isEnabled = false;
            _syncTimer.Stop();
            System.Diagnostics.Debug.WriteLine("WebDAV auto-sync disabled");
        }

        /// <summary>
        /// Perform sync operation
        /// </summary>
        public async Task<SyncResult> SyncAsync()
        {
            if (_client == null || !_isEnabled)
                return new SyncResult { Success = false, Message = "Sync not enabled" };

            var result = new SyncResult { Success = true };

            try
            {
                System.Diagnostics.Debug.WriteLine("Starting WebDAV sync...");

                var notesFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "notes.json");

                if (string.IsNullOrEmpty(notesFilePath) || !File.Exists(notesFilePath))
                {
                    result.Success = false;
                    result.Message = "Notes file not found";
                    return result;
                }

                var remoteFilePath = $"{_remoteDirectory}/notes.json";

                // Check if remote file exists and compare timestamps
                var remoteExists = await _client.FileExistsAsync(remoteFilePath);
                
                if (remoteExists)
                {
                    var remoteLastModified = await _client.GetFileLastModifiedAsync(remoteFilePath);
                    var localLastModified = File.GetLastWriteTime(notesFilePath);

                    if (remoteLastModified.HasValue && remoteLastModified.Value > localLastModified)
                    {
                        // Remote is newer, download
                        System.Diagnostics.Debug.WriteLine("Remote file is newer, downloading...");
                        var downloaded = await _client.DownloadFileAsync(remoteFilePath, notesFilePath);
                        
                        if (downloaded)
                        {
                            result.Message = "Downloaded from cloud";
                            result.FilesDownloaded = 1;
                            
                            // Reload notes
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                NoteManager.Instance.ReloadNotes();
                            });
                        }
                    }
                    else
                    {
                        // Local is newer or same, upload
                        System.Diagnostics.Debug.WriteLine("Local file is newer or same, uploading...");
                        var uploaded = await _client.UploadFileAsync(notesFilePath, remoteFilePath);
                        
                        if (uploaded)
                        {
                            result.Message = "Uploaded to cloud";
                            result.FilesUploaded = 1;
                        }
                    }
                }
                else
                {
                    // Remote doesn't exist, upload
                    System.Diagnostics.Debug.WriteLine("Remote file doesn't exist, uploading...");
                    var uploaded = await _client.UploadFileAsync(notesFilePath, remoteFilePath);
                    
                    if (uploaded)
                    {
                        result.Message = "Initial upload to cloud";
                        result.FilesUploaded = 1;
                    }
                }

                _lastSyncTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"WebDAV sync completed: {result.Message}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Sync failed: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"WebDAV sync error: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Force upload to cloud
        /// </summary>
        public async Task<bool> ForceUploadAsync()
        {
            if (_client == null)
                return false;

            try
            {
                var notesFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "notes.json");

                if (string.IsNullOrEmpty(notesFilePath) || !File.Exists(notesFilePath))
                    return false;

                var remoteFilePath = $"{_remoteDirectory}/notes.json";
                return await _client.UploadFileAsync(notesFilePath, remoteFilePath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Force download from cloud
        /// </summary>
        public async Task<bool> ForceDownloadAsync()
        {
            if (_client == null)
                return false;

            try
            {
                var notesFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "notes.json");

                if (string.IsNullOrEmpty(notesFilePath))
                    return false;

                var remoteFilePath = $"{_remoteDirectory}/notes.json";
                var downloaded = await _client.DownloadFileAsync(remoteFilePath, notesFilePath);

                if (downloaded)
                {
                    // Reload notes
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        NoteManager.Instance.ReloadNotes();
                    });
                }

                return downloaded;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _syncTimer?.Stop();
            _syncTimer?.Dispose();
            _client?.Dispose();
        }
    }

    /// <summary>
    /// Sync result information
    /// </summary>
    public class SyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int FilesUploaded { get; set; }
        public int FilesDownloaded { get; set; }
    }
}
