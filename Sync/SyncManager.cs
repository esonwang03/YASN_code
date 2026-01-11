using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using YASN;
using YASN.Logging;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace YASN.Sync
{
    /// <summary>
    /// Orchestrates sync operations on a timer while delegating storage to a backend.
    /// </summary>
    public class SyncManager : IDisposable
    {
        private readonly System.Timers.Timer _syncTimer;
        private readonly SignatureStore _signatureStore;
        private ISyncClient _client;
        private bool _isEnabled;
        private string _remoteDirectory = string.Empty;
        private readonly string[] _syncFiles;
        private Dictionary<string, string> _remoteSignatures = new();
        private const string SignatureFileName = "yasn.sig";

        public bool IsEnabled => _isEnabled;
        public bool IsConfigured => _client != null;
        public DateTime LastSyncTime { get; private set; }
        public string CurrentBackend => _client?.BackendName ?? string.Empty;

        private int _intervalSeconds = 300;

        public SyncManager()
        {
            _syncFiles = new[]
            {
                AppPaths.NotesFilePath,
                AppPaths.SyncSettingsPath
            };
            _signatureStore = new SignatureStore(AppPaths.SignatureFilePath);
            _syncTimer = new System.Timers.Timer
            {
                AutoReset = true
            };
            SetIntervalSeconds(_intervalSeconds);

            _syncTimer.Elapsed += async (_, __) => await SyncAsync();
        }

        public async Task<bool> ConfigureAsync(ISyncClient client, string remoteDirectory, bool enableAutoSync, int intervalSeconds)
        {
            _client?.Dispose();
            _client = client;
            _remoteDirectory = NormalizeRemoteDirectory(remoteDirectory);
            SetIntervalSeconds(intervalSeconds);

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

                var messages = new List<string>();
                _remoteSignatures = await LoadRemoteSignaturesAsync();
                var signatureDirty = false;

                foreach (var filePath in existingFiles)
                {
                    var fileName = Path.GetFileName(filePath);
                    var remoteFilePath = BuildRemotePath(fileName);
                    var isNotesFile = string.Equals(fileName, "notes.json", StringComparison.OrdinalIgnoreCase);
                    var localHash = _signatureStore.Get(fileName);
                    if (string.IsNullOrEmpty(localHash))
                    {
                        localHash = FileHashUtil.ComputeFileHash(filePath);
                        if (!string.IsNullOrEmpty(localHash))
                        {
                            _signatureStore.Set(fileName, localHash);
                            signatureDirty = true;
                        }
                    }
                    _remoteSignatures.TryGetValue(fileName, out var remoteHash);

                    var remoteExists = await _client.FileExistsAsync(remoteFilePath);
                    if (remoteExists)
                    {
                        var remoteLastModified = await _client.GetFileLastModifiedAsync(remoteFilePath);
                        var localLastModified = File.GetLastWriteTime(filePath);
                        var remoteIsNewer = remoteLastModified.HasValue && remoteLastModified.Value > localLastModified;
                        if (remoteIsNewer)
                        {
                            if (!string.IsNullOrEmpty(localHash) &&
                                !string.IsNullOrEmpty(remoteHash))
                            {
                                if (!string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
                                {
                                    AppLogger.Warn($"Sync conflict detected for {fileName} (local {localLastModified}, remote {remoteLastModified.Value}).");
                                    var resolution = await PromptConflictAsync(fileName, localLastModified, remoteLastModified.Value);

                                    if (resolution == ConflictResolutionAction.DownloadRemote)
                                    {
                                        if (await _client.DownloadFileAsync(remoteFilePath, filePath))
                                        {
                                            messages.Add($"{fileName} downloaded (conflict)");
                                            result.FilesDownloaded += 1;
                                            UpdateLocalSignature(filePath, fileName, ref signatureDirty);
                                            if (isNotesFile)
                                            {
                                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                                {
                                                    NoteManager.Instance.ReloadNotes();
                                                });
                                            }
                                            continue;
                                        }
                                    }
                                    else
                                    {
                                        if (await _client.UploadFileAsync(filePath, remoteFilePath))
                                        {
                                            messages.Add($"{fileName} uploaded (conflict)");
                                            UpdateLocalSignature(filePath, fileName, ref signatureDirty);
                                            result.FilesUploaded += 1;
                                            continue;
                                        }
                                    }
                                }
                                else
                                {
                                    File.SetLastWriteTime(filePath, remoteLastModified.Value);
                                    continue;
                                }
                            }
                        }

                        if (remoteIsNewer)
                        {
                            if (await _client.DownloadFileAsync(remoteFilePath, filePath))
                            {
                                messages.Add($"{fileName} downloaded");
                                result.FilesDownloaded += 1;
                                UpdateLocalSignature(filePath, fileName, ref signatureDirty);
                                if (isNotesFile)
                                {
                                    await Application.Current.Dispatcher.InvokeAsync(() =>
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
                                UpdateLocalSignature(filePath, fileName, ref signatureDirty);
                                result.FilesUploaded += 1;
                            }
                        }
                    }
                    else
                    {
                        if (await _client.UploadFileAsync(filePath, remoteFilePath))
                        {
                            messages.Add($"{fileName} initial upload");
                            UpdateLocalSignature(filePath, fileName, ref signatureDirty);
                            result.FilesUploaded += 1;
                        }
                    }
                }

                if (signatureDirty)
                {
                    _signatureStore.Save();
                    await UploadSignatureFileAsync();
                }

                LastSyncTime = DateTime.Now;
                result.Message = messages.Count > 0 ? string.Join("; ", messages) : "No changes";
                AppLogger.Debug($"Sync completed: {result.Message} (Uploaded: {result.FilesUploaded}, Downloaded: {result.FilesDownloaded})");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Sync failed: {ex.Message}";
                AppLogger.Warn($"Sync error: {ex.Message}");
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
            var signatureDirty = false;
            foreach (var filePath in _syncFiles.Where(File.Exists))
            {
                var remoteFilePath = BuildRemotePath(Path.GetFileName(filePath));
                anyUploaded |= await _client.UploadFileAsync(filePath, remoteFilePath);
                UpdateLocalSignature(filePath, Path.GetFileName(filePath), ref signatureDirty);
            }

            if (signatureDirty)
            {
                _signatureStore.Save();
                await UploadSignatureFileAsync();
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
            var signatureDirty = false;
            _remoteSignatures = await LoadRemoteSignaturesAsync();
            foreach (var kv in _remoteSignatures)
            {
                _signatureStore.Set(kv.Key, kv.Value);
                signatureDirty = true;
            }

            foreach (var filePath in _syncFiles)
            {
                var remoteFilePath = BuildRemotePath(Path.GetFileName(filePath));
                if (await _client.DownloadFileAsync(remoteFilePath, filePath))
                {
                    anyDownloaded = true;
                    UpdateLocalSignature(filePath, Path.GetFileName(filePath), ref signatureDirty);
                    if (string.Equals(Path.GetFileName(filePath), "notes.json", StringComparison.OrdinalIgnoreCase))
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            NoteManager.Instance.ReloadNotes();
                        });
                    }
                }
            }

            if (signatureDirty)
            {
                _signatureStore.Save();
                await UploadSignatureFileAsync();
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

        private void UpdateLocalSignature(string filePath, string fileName, ref bool signatureDirty)
        {
            var hash = FileHashUtil.ComputeFileHash(filePath);
            if (!string.IsNullOrEmpty(hash))
            {
                _signatureStore.Set(fileName, hash);
                signatureDirty = true;
            }
        }

        private async Task<Dictionary<string, string>> LoadRemoteSignaturesAsync()
        {
            var signatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (_client == null)
            {
                return signatures;
            }

            var remoteSigPath = BuildRemotePath(SignatureFileName);
            var tempFile = Path.GetTempFileName();
            try
            {
                if (await _client.FileExistsAsync(remoteSigPath) &&
                    await _client.DownloadFileAsync(remoteSigPath, tempFile))
                {
                    signatures = SignatureStore.LoadFromFile(tempFile);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"加载远端签名文件失败：{ex.Message}");
            }
            finally
            {
                try { File.Delete(tempFile); } catch { /* ignored */ }
            }

            return signatures;
        }

        private async Task UploadSignatureFileAsync()
        {
            if (_client == null || !File.Exists(AppPaths.SignatureFilePath))
            {
                return;
            }

            var remoteSigPath = BuildRemotePath(SignatureFileName);
            await _client.UploadFileAsync(AppPaths.SignatureFilePath, remoteSigPath);
        }

        private async Task<ConflictResolutionAction> PromptConflictAsync(string fileName, DateTime localTime, DateTime remoteTime)
        {
            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var message =
                    $"远端的 {fileName} 已更新，但内容与本地不同。\n本地修改时间: {localTime:G}\n远端修改时间: {remoteTime:G}\n选择“是”下载远端覆盖本地，选择“否”保留本地并覆盖远端。";
                var result = MessageBox.Show(message, "同步冲突", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                return result == MessageBoxResult.Yes
                    ? ConflictResolutionAction.DownloadRemote
                    : ConflictResolutionAction.KeepLocal;
            });
        }

        private enum ConflictResolutionAction
        {
            DownloadRemote,
            KeepLocal
        }

        public void SetIntervalSeconds(int seconds)
        {
            if (seconds < 10)
            {
                seconds = 10;
            }

            _intervalSeconds = seconds;
            _syncTimer.Interval = TimeSpan.FromSeconds(_intervalSeconds).TotalMilliseconds;
        }
    }
}
