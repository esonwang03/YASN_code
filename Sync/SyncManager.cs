using System;
using System.IO;
using System.Threading.Tasks;
using System.Timers;
using YASN;

namespace YASN.Sync
{
    /// <summary>
    /// Orchestrates sync operations on a timer while delegating storage to a backend.
    /// </summary>
    public class SyncManager : IDisposable
    {
        private readonly System.Timers.Timer _syncTimer;
        private ISyncClient _client;
        private bool _isEnabled;
        private string _remoteDirectory = string.Empty;
        private readonly string _localNotesPath;

        public bool IsEnabled => _isEnabled;
        public bool IsConfigured => _client != null;
        public DateTime LastSyncTime { get; private set; }
        public string CurrentBackend => _client?.BackendName ?? string.Empty;

        public SyncManager()
        {
            _localNotesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "notes.json");
            _syncTimer = new System.Timers.Timer
            {
                Interval = TimeSpan.FromMinutes(5).TotalMilliseconds,
                AutoReset = true
            };

            _syncTimer.Elapsed += async (_, __) => await SyncAsync();
        }

        public async Task<bool> ConfigureAsync(ISyncClient client, string remoteDirectory, bool enableAutoSync)
        {
            _client?.Dispose();
            _client = client;
            _remoteDirectory = NormalizeRemoteDirectory(remoteDirectory);

            if (!await _client.EnsureDirectoryAsync(_remoteDirectory))
            {
                return false;
            }

            if (enableAutoSync)
            {
                EnableAutoSync();
            }
            else
            {
                DisableAutoSync();
            }

            return true;
        }

        public void EnableAutoSync()
        {
            if (_client == null)
            {
                throw new InvalidOperationException("Sync client not configured");
            }

            _isEnabled = true;
            _syncTimer.Start();
        }

        public void DisableAutoSync()
        {
            _isEnabled = false;
            _syncTimer.Stop();
        }

        public async Task<SyncResult> SyncAsync()
        {
            if (_client == null || !_isEnabled)
            {
                return new SyncResult { Success = false, Message = "Sync not enabled" };
            }

            var result = new SyncResult { Success = true };

            try
            {
                if (!File.Exists(_localNotesPath))
                {
                    result.Success = false;
                    result.Message = "Notes file not found";
                    return result;
                }

                var remoteFilePath = BuildRemotePath("notes.json");

                var remoteExists = await _client.FileExistsAsync(remoteFilePath);
                if (remoteExists)
                {
                    var remoteLastModified = await _client.GetFileLastModifiedAsync(remoteFilePath);
                    var localLastModified = File.GetLastWriteTime(_localNotesPath);

                    if (remoteLastModified.HasValue && remoteLastModified.Value > localLastModified)
                    {
                        if (await _client.DownloadFileAsync(remoteFilePath, _localNotesPath))
                        {
                            result.Message = "Downloaded from cloud";
                            result.FilesDownloaded = 1;
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                NoteManager.Instance.ReloadNotes();
                            });
                        }
                    }
                    else
                    {
                        if (await _client.UploadFileAsync(_localNotesPath, remoteFilePath))
                        {
                            result.Message = "Uploaded to cloud";
                            result.FilesUploaded = 1;
                        }
                    }
                }
                else
                {
                    if (await _client.UploadFileAsync(_localNotesPath, remoteFilePath))
                    {
                        result.Message = "Initial upload to cloud";
                        result.FilesUploaded = 1;
                    }
                }

                LastSyncTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Sync failed: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Sync error: {ex}");
            }

            return result;
        }

        public async Task<bool> ForceUploadAsync()
        {
            if (_client == null || !File.Exists(_localNotesPath))
            {
                return false;
            }

            var remoteFilePath = BuildRemotePath("notes.json");
            return await _client.UploadFileAsync(_localNotesPath, remoteFilePath);
        }

        public async Task<bool> ForceDownloadAsync()
        {
            if (_client == null)
            {
                return false;
            }

            var remoteFilePath = BuildRemotePath("notes.json");
            var downloaded = await _client.DownloadFileAsync(remoteFilePath, _localNotesPath);

            if (downloaded)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    NoteManager.Instance.ReloadNotes();
                });
            }

            return downloaded;
        }

        public void Dispose()
        {
            _syncTimer?.Stop();
            _syncTimer?.Dispose();
            _client?.Dispose();
        }

        private static string NormalizeRemoteDirectory(string directory)
        {
            return (directory ?? string.Empty).Trim().Trim('/');
        }

        private string BuildRemotePath(string fileName)
        {
            if (string.IsNullOrEmpty(_remoteDirectory))
            {
                return fileName;
            }

            return $"{_remoteDirectory}/{fileName}";
        }
    }
}
