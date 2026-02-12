using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
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
        private Dictionary<string, string> _remoteSignatures = new();
        private const string ManifestFileName = "sync.manifest.json";

        public bool IsEnabled => _isEnabled;
        public bool IsConfigured => _client != null;
        public DateTime LastSyncTime { get; private set; }
        public string CurrentBackend => _client?.BackendName ?? string.Empty;

        private int _intervalSeconds = 300;

        public SyncManager()
        {
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
                _remoteSignatures = await LoadRemoteSignaturesAsync();
                var localEntries = BuildLocalSyncEntries();
                var allKeys = new HashSet<string>(localEntries.Keys, StringComparer.OrdinalIgnoreCase);
                foreach (var key in _remoteSignatures.Keys)
                {
                    if (!string.Equals(key, ManifestFileName, StringComparison.OrdinalIgnoreCase) && IsSyncableKey(key))
                    {
                        allKeys.Add(key);
                    }
                }

                if (allKeys.Count == 0)
                {
                    result.Success = false;
                    result.Message = "No syncable files found";
                    return result;
                }

                var messages = new List<string>();
                var signatureDirty = false;
                var shouldReloadNotes = false;

                foreach (var key in allKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                {
                    var localPath = localEntries.TryGetValue(key, out var existingPath)
                        ? existingPath
                        : ToLocalPath(key);
                    var remotePath = BuildRemotePath(key);
                    var localExists = File.Exists(localPath);
                    var remoteExists = _remoteSignatures.ContainsKey(key) || await _client.FileExistsAsync(remotePath);

                    if (localExists && remoteExists)
                    {
                        var localHash = FileHashUtil.ComputeFileHash(localPath);
                        if (!string.IsNullOrEmpty(localHash))
                        {
                            _signatureStore.Set(key, localHash);
                            signatureDirty = true;
                        }

                        _remoteSignatures.TryGetValue(key, out var remoteHash);
                        var remoteLastModified = await _client.GetFileLastModifiedAsync(remotePath);
                        var localLastModified = File.GetLastWriteTime(localPath);
                        var remoteIsNewer = remoteLastModified.HasValue && remoteLastModified.Value > localLastModified;

                        if (!string.IsNullOrEmpty(localHash) &&
                            !string.IsNullOrEmpty(remoteHash) &&
                            string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
                        {
                            if (remoteLastModified.HasValue && remoteLastModified.Value > localLastModified)
                            {
                                File.SetLastWriteTime(localPath, remoteLastModified.Value);
                            }

                            continue;
                        }

                        if (remoteIsNewer &&
                            !string.IsNullOrEmpty(localHash) &&
                            !string.IsNullOrEmpty(remoteHash) &&
                            !string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
                        {
                            AppLogger.Warn($"Sync conflict detected for {key} (local {localLastModified}, remote {remoteLastModified.Value}).");
                            var resolution = await PromptConflictAsync(key, localLastModified, remoteLastModified.Value);
                            if (resolution == ConflictResolutionAction.DownloadRemote)
                            {
                                if (await DownloadFileAsync(remotePath, localPath))
                                {
                                    messages.Add($"{key} downloaded (conflict)");
                                    result.FilesDownloaded += 1;
                                    UpdateLocalSignature(localPath, key, ref signatureDirty);
                                    shouldReloadNotes |= ShouldReloadNotes(key);
                                }
                            }
                            else
                            {
                                if (await UploadFileAsync(localPath, key))
                                {
                                    messages.Add($"{key} uploaded (conflict)");
                                    UpdateLocalSignature(localPath, key, ref signatureDirty);
                                    result.FilesUploaded += 1;
                                }
                            }

                            continue;
                        }

                        if (remoteIsNewer)
                        {
                            if (await DownloadFileAsync(remotePath, localPath))
                            {
                                messages.Add($"{key} downloaded");
                                result.FilesDownloaded += 1;
                                UpdateLocalSignature(localPath, key, ref signatureDirty);
                                shouldReloadNotes |= ShouldReloadNotes(key);
                            }
                        }
                        else
                        {
                            if (await UploadFileAsync(localPath, key))
                            {
                                messages.Add($"{key} uploaded");
                                UpdateLocalSignature(localPath, key, ref signatureDirty);
                                result.FilesUploaded += 1;
                            }
                        }

                        continue;
                    }

                    if (localExists && !remoteExists)
                    {
                        if (await UploadFileAsync(localPath, key))
                        {
                            messages.Add($"{key} initial upload");
                            UpdateLocalSignature(localPath, key, ref signatureDirty);
                            result.FilesUploaded += 1;
                        }

                        continue;
                    }

                    if (!localExists && remoteExists)
                    {
                        if (await DownloadFileAsync(remotePath, localPath))
                        {
                            messages.Add($"{key} downloaded (new)");
                            result.FilesDownloaded += 1;
                            UpdateLocalSignature(localPath, key, ref signatureDirty);
                            shouldReloadNotes |= ShouldReloadNotes(key);
                        }
                    }
                }

                if (shouldReloadNotes)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        NoteManager.Instance.ReloadNotes();
                    });
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
            foreach (var (key, filePath) in BuildLocalSyncEntries())
            {
                anyUploaded |= await UploadFileAsync(filePath, key);
                UpdateLocalSignature(filePath, key, ref signatureDirty);
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
            var shouldReloadNotes = false;
            _remoteSignatures = await LoadRemoteSignaturesAsync();
            foreach (var kv in _remoteSignatures)
            {
                if (string.Equals(kv.Key, ManifestFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!IsSyncableKey(kv.Key))
                {
                    continue;
                }

                var localPath = ToLocalPath(kv.Key);
                var remotePath = BuildRemotePath(kv.Key);
                if (await DownloadFileAsync(remotePath, localPath))
                {
                    anyDownloaded = true;
                    _signatureStore.Set(kv.Key, kv.Value);
                    signatureDirty = true;
                    shouldReloadNotes |= ShouldReloadNotes(kv.Key);
                }
            }

            if (shouldReloadNotes)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    NoteManager.Instance.ReloadNotes();
                });
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

        private string BuildRemotePath(string relativePath)
        {
            if (string.IsNullOrEmpty(_remoteDirectory))
            {
                return relativePath;
            }

            return $"{_remoteDirectory}/{relativePath}";
        }

        private void UpdateLocalSignature(string filePath, string key, ref bool signatureDirty)
        {
            var hash = FileHashUtil.ComputeFileHash(filePath);
            if (!string.IsNullOrEmpty(hash))
            {
                _signatureStore.Set(key, hash);
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

            var remoteSigPath = BuildRemotePath(ManifestFileName);
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
                try { File.Delete(tempFile); } catch { }
            }

            return signatures;
        }

        private async Task UploadSignatureFileAsync()
        {
            if (_client == null || !File.Exists(AppPaths.SignatureFilePath))
            {
                return;
            }

            var remoteSigPath = BuildRemotePath(ManifestFileName);
            await _client.UploadFileAsync(AppPaths.SignatureFilePath, remoteSigPath);
        }

        private async Task<ConflictResolutionAction> PromptConflictAsync(string fileName, DateTime localTime, DateTime remoteTime)
        {
            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var message =
                    $"远端的 {fileName} 已更新，但内容与本地不同。\n本地修改时间: {localTime:G}\n远端修改时间: {remoteTime:G}\n选择“是”下载远端覆盖本地，选择“否”保留本地并覆盖远端。";
                var result = MessageBox.Show(message, "同步冲突", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                return result == System.Windows.MessageBoxResult.Yes
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

        private Dictionary<string, string> BuildLocalSyncEntries()
        {
            var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            AddIfExists(entries, AppPaths.NotesIndexPath);
            AddDirectoryFiles(entries, AppPaths.NotesMarkdownRoot);
            AddDirectoryFiles(entries, AppPaths.NoteAssetsRoot);

            return entries;
        }

        private static void AddIfExists(IDictionary<string, string> map, string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                return;
            }

            var key = ToSyncKey(fullPath);
            map[key] = fullPath;
        }

        private static void AddDirectoryFiles(IDictionary<string, string> map, string root)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var key = ToSyncKey(file);
                map[key] = file;
            }
        }

        private static string ToSyncKey(string fullPath)
        {
            var relative = Path.GetRelativePath(AppPaths.DataDirectory, fullPath);
            return relative.Replace('\\', '/');
        }

        private static string ToLocalPath(string key)
        {
            var localRelative = key.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(AppPaths.DataDirectory, localRelative);
        }

        private async Task<bool> UploadFileAsync(string localPath, string key)
        {
            if (_client == null)
            {
                return false;
            }

            var idx = key.LastIndexOf('/');
            if (idx > 0)
            {
                var remoteDir = key.Substring(0, idx);
                if (!await _client.EnsureDirectoryAsync(BuildRemotePath(remoteDir)))
                {
                    return false;
                }
            }

            return await _client.UploadFileAsync(localPath, BuildRemotePath(key));
        }

        private async Task<bool> DownloadFileAsync(string remotePath, string localPath)
        {
            if (_client == null)
            {
                return false;
            }

            var directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return await _client.DownloadFileAsync(remotePath, localPath);
        }

        private static bool ShouldReloadNotes(string key)
        {
            return key.Equals("notes.index.json", StringComparison.OrdinalIgnoreCase)
                   || key.StartsWith("notes/", StringComparison.OrdinalIgnoreCase)
                   || key.StartsWith("note-assets/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSyncableKey(string key)
        {
            return key.Equals("notes.index.json", StringComparison.OrdinalIgnoreCase)
                   || key.StartsWith("notes/", StringComparison.OrdinalIgnoreCase)
                   || key.StartsWith("note-assets/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
