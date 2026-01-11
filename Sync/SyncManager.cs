using System;
using System.IO;
using System.Linq;
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
        private readonly string[] _syncFiles;

        public bool IsEnabled => _isEnabled;
        public bool IsConfigured => _client != null;
        public DateTime LastSyncTime { get; private set; }
        public string CurrentBackend => _client?.BackendName ?? string.Empty;

        public SyncManager()
        {
            _syncFiles = new[]
            {
                AppPaths.NotesFilePath,
                AppPaths.SyncSettingsPath
            };
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

        private void EnableAutoSync()
        {
            if (_client == null)
            {
                throw new InvalidOperationException("Sync client not configured");
            }

            _isEnabled = true;
            _syncTimer.Start();
        }

        private void DisableAutoSync()
        {
            _isEnabled = false;
            _syncTimer.Stop();
        }

        private async Task<SyncResult> SyncAsync()
        {
            if (_client == null || !_isEnabled)
            {
                return new SyncResult { Success = false, Message = "Sync not enabled" };
            }

            var result = new SyncResult { Success = true };

            try
            {
                var existingFiles = _syncFiles.Where(File.Exists).ToList();
                if (!existingFiles.Any())
                {
                    result.Success = false;
                    result.Message = "No syncable files found";
                    return result;
                }

                var messages = new System.Collections.Generic.List<string>();

                foreach (var filePath in existingFiles)
                {
                    var fileName = Path.GetFileName(filePath);
                    var remoteFilePath = BuildRemotePath(fileName);
                    var isNotesFile = string.Equals(fileName, "notes.json", StringComparison.OrdinalIgnoreCase);

                    var remoteExists = await _client.FileExistsAsync(remoteFilePath);
                    if (remoteExists)
                    {
                        var remoteLastModified = await _client.GetFileLastModifiedAsync(remoteFilePath);
                        var localLastModified = File.GetLastWriteTime(filePath);

                        if (remoteLastModified.HasValue && remoteLastModified.Value > localLastModified)
                        {
                            if (await _client.DownloadFileAsync(remoteFilePath, filePath))
                            {
                                messages.Add($"{fileName} downloaded");
                                result.FilesDownloaded += 1;
                                if (isNotesFile)
                                {
                                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                    {
                                        NoteManager.Instance.ReloadNotes();
                                    });
                                }
                            }
                        }
                        else
                        {
                            if (await _client.UploadFileAsync(filePath, remoteFilePath))
                            {
                                messages.Add($"{fileName} uploaded");
                                result.FilesUploaded += 1;
                            }
                        }
                    }
                    else
                    {
                        if (await _client.UploadFileAsync(filePath, remoteFilePath))
                        {
                            messages.Add($"{fileName} initial upload");
                            result.FilesUploaded += 1;
                        }
                    }
                }

                LastSyncTime = DateTime.Now;
                result.Message = messages.Count > 0 ? string.Join("; ", messages) : "No changes";
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
            if (_client == null)
            {
                return false;
            }

            var anyUploaded = false;
            foreach (var filePath in _syncFiles.Where(File.Exists))
            {
                var remoteFilePath = BuildRemotePath(Path.GetFileName(filePath));
                anyUploaded |= await _client.UploadFileAsync(filePath, remoteFilePath);
            }

            return anyUploaded;
        }

        public async Task<bool> ForceDownloadAsync()
        {
            if (_client == null)
            {
                return false;
            }

            var anyDownloaded = false;
            foreach (var filePath in _syncFiles)
            {
                var remoteFilePath = BuildRemotePath(Path.GetFileName(filePath));
                if (await _client.DownloadFileAsync(remoteFilePath, filePath))
                {
                    anyDownloaded = true;
                    if (string.Equals(Path.GetFileName(filePath), "notes.json", StringComparison.OrdinalIgnoreCase))
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            NoteManager.Instance.ReloadNotes();
                        });
                    }
                }
            }

            return anyDownloaded;
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
