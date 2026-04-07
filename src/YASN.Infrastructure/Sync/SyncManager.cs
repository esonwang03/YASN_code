using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using YASN.App.Notes;
using YASN.Core;
using YASN.Infrastructure;
using YASN.Infrastructure.Logging;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// Coordinates local/remote synchronization on a fixed interval by delegating
    /// storage operations to an <see cref="ISyncClient"/> backend.
    /// </summary>
    public class SyncManager : IDisposable
    {
        private readonly System.Timers.Timer _syncTimer;
        private readonly SignatureStore _signatureStore;
        private readonly SemaphoreSlim _syncGate = new SemaphoreSlim(1, 1);
        private ISyncClient _client;
        private bool _isEnabled;
        private string _remoteDirectory = string.Empty;
        private Dictionary<string, string> _remoteSignatures = new();
        // Remote manifest file that stores content hashes by sync key.
        private const string ManifestFileName = "sync.manifest.json";

        /// <summary>
        /// Gets whether periodic synchronization is currently enabled.
        /// </summary>
        public bool IsEnabled => _isEnabled;

        /// <summary>
        /// Gets whether a sync backend has been configured.
        /// </summary>
        public bool IsConfigured => _client != null;

        /// <summary>
        /// Gets the timestamp of the most recent successful sync attempt.
        /// </summary>
        public DateTime LastSyncTime { get; private set; }

        /// <summary>
        /// Gets the current backend display name, or empty if not configured.
        /// </summary>
        public string CurrentBackend => _client?.BackendName ?? string.Empty;

        private int _intervalSeconds = 300;

        /// <summary>
        /// Creates a sync manager with default 5-minute auto-sync interval.
        /// </summary>
        public SyncManager()
        {
            _signatureStore = new SignatureStore(AppPaths.SignatureFilePath);
            _syncTimer = new System.Timers.Timer
            {
                AutoReset = true
            };
            SetIntervalSeconds(_intervalSeconds);

            _syncTimer.Elapsed += async (_, __) => await SyncAsync(requireEnabled: true, progress: null).ConfigureAwait(false);
        }

        /// <summary>
        /// Configures the sync backend and auto-sync settings.
        /// </summary>
        /// <param name="client">Backend client implementation.</param>
        /// <param name="remoteDirectory">Optional remote base directory.</param>
        /// <param name="enableAutoSync">Whether periodic sync should start immediately.</param>
        /// <param name="intervalSeconds">Auto-sync interval in seconds (minimum 10).</param>
        /// <returns><c>true</c> if configuration and remote directory checks succeed.</returns>
        public async Task<bool> ConfigureAsync(ISyncClient client, string remoteDirectory, bool enableAutoSync, int intervalSeconds)
        {
            string normalizedRemoteDirectory = NormalizeRemoteDirectory(remoteDirectory);
            SetIntervalSeconds(intervalSeconds);

            if (!await client.EnsureDirectoryAsync(normalizedRemoteDirectory).ConfigureAwait(false))
            {
                client.Dispose();
                return false;
            }

            _client?.Dispose();
            _client = client;
            _remoteDirectory = normalizedRemoteDirectory;

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

        /// <summary>
        /// Starts periodic synchronization.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when backend is not configured.</exception>
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

        /// <summary>
        /// Runs one sync pass immediately, even when periodic auto-sync is disabled.
        /// </summary>
        /// <returns>The sync result.</returns>
        public Task<SyncResult> RunSyncNowAsync(IProgress<SyncProgressInfo>? progress = null)
        {
            return SyncAsync(requireEnabled: false, progress);
        }

        /// <summary>
        /// Runs one sync pass and resolves upload/download direction per file.
        /// </summary>
        /// <remarks>
        /// Conflict handling is timestamp-driven: if remote is newer and hashes differ,
        /// user input is required to pick remote download vs local overwrite upload.
        /// </remarks>
        private async Task<SyncResult> SyncAsync(bool requireEnabled, IProgress<SyncProgressInfo>? progress = null)
        {
            if (_client == null || requireEnabled && !_isEnabled)
            {
                return new SyncResult
                {
                    Success = false,
                    Message = _client == null ? "Sync backend not configured" : "Sync not enabled"
                };
            }

            await _syncGate.WaitAsync().ConfigureAwait(false);
            SyncResult result = new SyncResult { Success = true };

            try
            {
                if (_client == null || requireEnabled && !_isEnabled)
                {
                    return new SyncResult
                    {
                        Success = false,
                        Message = _client == null ? "Sync backend not configured" : "Sync not enabled"
                    };
                }

                _remoteSignatures = await LoadRemoteSignaturesAsync().ConfigureAwait(false);
                Dictionary<string, string> localEntries = BuildLocalSyncEntries();
                HashSet<string> allKeys = new HashSet<string>(localEntries.Keys, StringComparer.OrdinalIgnoreCase);
                foreach (string key in _remoteSignatures.Keys.Where(key => !string.Equals(key, ManifestFileName, StringComparison.OrdinalIgnoreCase) && IsSyncableKey(key)))
                {
                    allKeys.Add(key);
                }

                if (allKeys.Count == 0)
                {
                    result.Success = false;
                    result.Message = "No syncable files found";
                    ReportProgress(progress, 1, 1, result.Message);
                    return result;
                }

                List<string> messages = new List<string>();
                bool signatureDirty = false;
                bool shouldReloadNotes = false;
                bool shouldRepairNoteIndex = false;
                bool shouldRefreshPreviewStyles = false;
                int totalSteps = allKeys.Count + 1;
                int completedSteps = 0;

                ReportProgress(progress, completedSteps, totalSteps, "Preparing sync");

                foreach (string? key in allKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                {
                    ReportProgress(progress, completedSteps, totalSteps, $"Syncing {key}");
                    string localPath = localEntries.TryGetValue(key, out string? existingPath)
                        ? existingPath
                        : ToLocalPath(key);
                    string remotePath = BuildRemotePath(key);
                    bool localExists = File.Exists(localPath);
                    bool remoteExists = _remoteSignatures.ContainsKey(key) || await _client.FileExistsAsync(remotePath).ConfigureAwait(false);

                    switch (localExists)
                    {
                        case true when remoteExists:
                        {
                            string localHash = FileHashUtil.ComputeFileHash(localPath);
                            if (!string.IsNullOrEmpty(localHash))
                            {
                                _signatureStore.Set(key, localHash);
                                signatureDirty = true;
                            }

                            _remoteSignatures.TryGetValue(key, out string? remoteHash);
                            DateTime? remoteLastModified = await _client.GetFileLastModifiedAsync(remotePath).ConfigureAwait(false);
                            DateTime localLastModified = File.GetLastWriteTime(localPath);
                            bool remoteIsNewer = remoteLastModified.HasValue && remoteLastModified.Value > localLastModified;

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

                            switch (remoteIsNewer)
                            {
                                // Both sides changed and remote timestamp wins -> explicit conflict prompt.
                                case true when
                                    !string.IsNullOrEmpty(localHash) &&
                                    !string.IsNullOrEmpty(remoteHash) &&
                                    !string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase):
                                {
                                    AppLogger.Warn($"Sync conflict detected for {key} (local {localLastModified}, remote {remoteLastModified.Value}).");
                                    ConflictResolutionAction resolution = await PromptConflictAsync(key, localLastModified, remoteLastModified.Value).ConfigureAwait(false);
                                    if (resolution == ConflictResolutionAction.DownloadRemote)
                                    {
                                        if (await DownloadFileAsync(remotePath, localPath).ConfigureAwait(false))
                                        {
                                            messages.Add($"{key} downloaded (conflict)");
                                            result.FilesDownloaded += 1;
                                            UpdateLocalSignature(localPath, key, ref signatureDirty);
                                            shouldReloadNotes |= ShouldReloadNotes(key);
                                            shouldRepairNoteIndex |= ShouldRepairNoteIndex(key);
                                            shouldRefreshPreviewStyles |= ShouldRefreshPreviewStyles(key);
                                        }
                                    }
                                    else
                                    {
                                        if (await UploadFileAsync(localPath, key).ConfigureAwait(false))
                                        {
                                            messages.Add($"{key} uploaded (conflict)");
                                            UpdateLocalSignature(localPath, key, ref signatureDirty);
                                            result.FilesUploaded += 1;
                                        }
                                    }

                                    continue;
                                }
                                // No hard conflict: choose newer side.
                                case true:
                                {
                                    if (await DownloadFileAsync(remotePath, localPath).ConfigureAwait(false))
                                    {
                                        messages.Add($"{key} downloaded");
                                        result.FilesDownloaded += 1;
                                        UpdateLocalSignature(localPath, key, ref signatureDirty);
                                        shouldReloadNotes |= ShouldReloadNotes(key);
                                        shouldRepairNoteIndex |= ShouldRepairNoteIndex(key);
                                        shouldRefreshPreviewStyles |= ShouldRefreshPreviewStyles(key);
                                    }

                                    break;
                                }
                                default:
                                {
                                    if (await UploadFileAsync(localPath, key).ConfigureAwait(false))
                                    {
                                        messages.Add($"{key} uploaded");
                                        UpdateLocalSignature(localPath, key, ref signatureDirty);
                                        result.FilesUploaded += 1;
                                    }

                                    break;
                                }
                            }

                            continue;
                        }
                        // First-time publish from local.
                        case true when !remoteExists:
                        {
                            if (await UploadFileAsync(localPath, key).ConfigureAwait(false))
                            {
                                messages.Add($"{key} initial upload");
                                UpdateLocalSignature(localPath, key, ref signatureDirty);
                                result.FilesUploaded += 1;
                            }

                            continue;
                        }
                        // First-time materialization from remote.
                        case false when remoteExists:
                        {
                            if (!await DownloadFileAsync(remotePath, localPath).ConfigureAwait(false)) continue;
                            messages.Add($"{key} downloaded (new)");
                            result.FilesDownloaded += 1;
                            UpdateLocalSignature(localPath, key, ref signatureDirty);
                            shouldReloadNotes |= ShouldReloadNotes(key);
                            shouldRepairNoteIndex |= ShouldRepairNoteIndex(key);
                            shouldRefreshPreviewStyles |= ShouldRefreshPreviewStyles(key);
                            break;
                        }
                    }

                    completedSteps += 1;
                    ReportProgress(progress, completedSteps, totalSteps, $"Synced {key}");
                }

                if (shouldReloadNotes)
                {
                    ReportProgress(progress, completedSteps, totalSteps, "Reloading notes");
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        NoteManager.Instance.ReloadNotes();
                    });
                }

                if (shouldRepairNoteIndex)
                {
                    ReportProgress(progress, completedSteps, totalSteps, "Rebuilding notes.index.json");
                    NoteIndexRepairResult repairResult = await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        return NoteManager.Instance.RepairIndexFromLocalMarkdownFiles();
                    });

                    if (repairResult.WasChanged)
                    {
                        string indexKey = ToSyncKey(AppPaths.NotesIndexPath);
                        if (await UploadFileAsync(AppPaths.NotesIndexPath, indexKey).ConfigureAwait(false))
                        {
                            UpdateLocalSignature(AppPaths.NotesIndexPath, indexKey, ref signatureDirty);
                            result.FilesUploaded += 1;
                            messages.Add("notes.index.json repaired and uploaded");
                            AppLogger.Warn($"{repairResult.Message} Uploaded repaired notes.index.json to keep remote data aligned.");
                        }
                        else
                        {
                            messages.Add("notes.index.json repaired locally but upload failed");
                            AppLogger.Warn($"{repairResult.Message} Failed to upload repaired notes.index.json.");
                        }
                    }
                }

                if (shouldRefreshPreviewStyles)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (NoteData note in NoteManager.Instance.Notes)
                        {
                            note.Window?.RefreshPreviewStyleFromSettings();
                        }
                    });
                }

                if (signatureDirty)
                {
                    _signatureStore.Save();
                    await UploadSignatureFileAsync().ConfigureAwait(false);
                }

                LastSyncTime = DateTime.Now;
                result.Message = messages.Count > 0 ? string.Join("; ", messages) : "No changes";
                completedSteps = totalSteps;
                ReportProgress(progress, completedSteps, totalSteps, result.Success ? result.Message : "Sync failed");
                AppLogger.Debug($"Sync completed: {result.Message} (Uploaded: {result.FilesUploaded}, Downloaded: {result.FilesDownloaded})");
            }
            catch (HttpRequestException ex)
            {
                result.Success = false;
                result.Message = $"Sync failed: {ex.Message}";
                AppLogger.Warn($"Sync error: {ex.Message}");
            }
            catch (IOException ex)
            {
                result.Success = false;
                result.Message = $"Sync failed: {ex.Message}";
                AppLogger.Warn($"Sync error: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                result.Success = false;
                result.Message = $"Sync failed: {ex.Message}";
                AppLogger.Warn($"Sync error: {ex.Message}");
            }
            catch (JsonException ex)
            {
                result.Success = false;
                result.Message = $"Sync failed: {ex.Message}";
                AppLogger.Warn($"Sync error: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                result.Success = false;
                result.Message = $"Sync failed: {ex.Message}";
                AppLogger.Warn($"Sync error: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Success = false;
                result.Message = $"Sync failed: {ex.Message}";
                AppLogger.Warn($"Sync error: {ex.Message}");
            }
            finally
            {
                _syncGate.Release();
            }

            if (!result.Success)
            {
                ReportProgress(progress, 1, 1, result.Message);
            }

            return result;
        }

        /// <summary>
        /// Uploads all local sync files regardless of timestamp comparison.
        /// </summary>
        /// <returns><c>true</c> if at least one file upload succeeds.</returns>
        public async Task<bool> ForceUploadAsync()
        {
            if (_client == null)
            {
                return false;
            }

            bool anyUploaded = false;
            bool signatureDirty = false;
            foreach ((string key, string filePath) in BuildLocalSyncEntries())
            {
                anyUploaded |= await UploadFileAsync(filePath, key).ConfigureAwait(false);
                UpdateLocalSignature(filePath, key, ref signatureDirty);
            }

            if (signatureDirty)
            {
                _signatureStore.Save();
                await UploadSignatureFileAsync().ConfigureAwait(false);
            }

            return anyUploaded;
        }

        /// <summary>
        /// Downloads all files listed in the remote manifest regardless of timestamps.
        /// </summary>
        /// <returns><c>true</c> if at least one file download succeeds.</returns>
        public async Task<bool> ForceDownloadAsync()
        {
            if (_client == null)
            {
                return false;
            }

            bool anyDownloaded = false;
            bool signatureDirty = false;
            bool shouldReloadNotes = false;
            bool shouldRefreshPreviewStyles = false;
            _remoteSignatures = await LoadRemoteSignaturesAsync().ConfigureAwait(false);
            foreach (KeyValuePair<string, string> kv in _remoteSignatures)
            {
                if (string.Equals(kv.Key, ManifestFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!IsSyncableKey(kv.Key))
                {
                    continue;
                }

                string localPath = ToLocalPath(kv.Key);
                string remotePath = BuildRemotePath(kv.Key);
                if (!await DownloadFileAsync(remotePath, localPath).ConfigureAwait(false)) continue;
                anyDownloaded = true;
                _signatureStore.Set(kv.Key, kv.Value);
                signatureDirty = true;
                shouldReloadNotes |= ShouldReloadNotes(kv.Key);
                shouldRefreshPreviewStyles |= ShouldRefreshPreviewStyles(kv.Key);
            }

            if (shouldReloadNotes)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    NoteManager.Instance.ReloadNotes();
                });
            }

            if (shouldRefreshPreviewStyles)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (NoteData note in NoteManager.Instance.Notes)
                    {
                        note.Window?.RefreshPreviewStyleFromSettings();
                    }
                });
            }

            if (signatureDirty)
            {
                _signatureStore.Save();
                await UploadSignatureFileAsync().ConfigureAwait(false);
            }

            return anyDownloaded;
        }

        /// <summary>
        /// Stops timers and disposes backend resources.
        /// </summary>
        public void Dispose()
        {
            _syncTimer?.Stop();
            _syncTimer?.Dispose();
            _client?.Dispose();
        }

        /// <summary>
        /// Normalizes a remote directory by trimming whitespace and slashes.
        /// </summary>
        private static string NormalizeRemoteDirectory(string directory)
        {
            return (directory ?? string.Empty).Trim().Trim('/');
        }

        /// <summary>
        /// Joins sync relative path with configured remote base directory.
        /// </summary>
        private string BuildRemotePath(string relativePath)
        {
            return string.IsNullOrEmpty(_remoteDirectory) ? relativePath : $"{_remoteDirectory}/{relativePath}";
        }

        /// <summary>
        /// Recomputes and stores local file hash into signature manifest state.
        /// </summary>
        private void UpdateLocalSignature(string filePath, string key, ref bool signatureDirty)
        {
            string hash = FileHashUtil.ComputeFileHash(filePath);
            if (string.IsNullOrEmpty(hash)) return;
            _signatureStore.Set(key, hash);
            signatureDirty = true;
        }

        /// <summary>
        /// Loads hash manifest from remote storage into memory.
        /// </summary>
        private async Task<Dictionary<string, string>> LoadRemoteSignaturesAsync()
        {
            Dictionary<string, string> signatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (_client == null)
            {
                return signatures;
            }

            string remoteSigPath = BuildRemotePath(ManifestFileName);
            string tempFile = Path.GetTempFileName();
            try
            {
                if (await _client.FileExistsAsync(remoteSigPath).ConfigureAwait(false) &&
                    await _client.DownloadFileAsync(remoteSigPath, tempFile).ConfigureAwait(false))
                {
                    signatures = SignatureStore.LoadFromFile(tempFile);
                }
            }
            catch (HttpRequestException ex)
            {
                AppLogger.Warn($"Failed to load remote signature: {ex.Message}");
            }
            catch (IOException ex)
            {
                AppLogger.Warn($"Failed to load remote signature: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Warn($"Failed to load remote signature: {ex.Message}");
            }
            catch (JsonException ex)
            {
                AppLogger.Warn($"Failed to load remote signature: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                AppLogger.Warn($"Failed to load remote signature: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Warn($"Failed to load remote signature: {ex.Message}");
            }
            finally
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch (IOException ex)
                {
                    AppLogger.Debug($"Failed to delete temp signature file '{tempFile}': {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    AppLogger.Debug($"Failed to delete temp signature file '{tempFile}': {ex.Message}");
                }
            }

            return signatures;
        }

        /// <summary>
        /// Uploads the local signature manifest file to remote storage.
        /// </summary>
        private async Task UploadSignatureFileAsync()
        {
            if (_client == null || !File.Exists(AppPaths.SignatureFilePath))
            {
                return;
            }

            string remoteSigPath = BuildRemotePath(ManifestFileName);
            await _client.UploadFileAsync(AppPaths.SignatureFilePath, remoteSigPath).ConfigureAwait(false);
        }

        /// <summary>
        /// Prompts user to resolve a content conflict when local and remote differ.
        /// </summary>
        private async Task<ConflictResolutionAction> PromptConflictAsync(string fileName, DateTime localTime, DateTime remoteTime)
        {
            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                string message =
                    $"远端的 {fileName} 已更新，但内容与本地不同。\n本地修改时间: {localTime:G}\n远端修改时间: {remoteTime:G}\n选择“是”下载远端覆盖本地，选择“否”保留本地并覆盖远端。";
                MessageBoxResult result = MessageBox.Show(message, "同步冲突", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                return result == System.Windows.MessageBoxResult.Yes
                    ? ConflictResolutionAction.DownloadRemote
                    : ConflictResolutionAction.KeepLocal;
            });
        }

        /// <summary>
        /// User-selected direction for resolving conflicting file versions.
        /// </summary>
        private enum ConflictResolutionAction
        {
            DownloadRemote,
            KeepLocal
        }

        /// <summary>
        /// Updates auto-sync interval; values under 10 seconds are clamped.
        /// </summary>
        public void SetIntervalSeconds(int seconds)
        {
            if (seconds < 10)
            {
                seconds = 10;
            }

            _intervalSeconds = seconds;
            _syncTimer.Interval = TimeSpan.FromSeconds(_intervalSeconds).TotalMilliseconds;
        }

        /// <summary>
        /// Builds all local file entries participating in sync.
        /// </summary>
        private Dictionary<string, string> BuildLocalSyncEntries()
        {
            Dictionary<string, string> entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            AddIfExists(entries, AppPaths.NotesIndexPath);
            AddDirectoryFiles(entries, AppPaths.NotesMarkdownRoot);
            AddDirectoryFiles(entries, AppPaths.NoteAssetsRoot);
            AddDirectoryFiles(entries, AppPaths.StyleRoot);

            return entries;
        }

        /// <summary>
        /// Adds file to sync map when it exists.
        /// </summary>
        private static void AddIfExists(IDictionary<string, string> map, string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                return;
            }

            string key = ToSyncKey(fullPath);
            map[key] = fullPath;
        }

        /// <summary>
        /// Recursively adds all files under a directory into sync map.
        /// </summary>
        private static void AddDirectoryFiles(IDictionary<string, string> map, string root)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string key = ToSyncKey(file);
                map[key] = file;
            }
        }

        /// <summary>
        /// Converts absolute local path to normalized sync key.
        /// </summary>
        private static string ToSyncKey(string fullPath)
        {
            string relative = Path.GetRelativePath(AppPaths.DataDirectory, fullPath);
            return relative.Replace('\\', '/');
        }

        /// <summary>
        /// Converts normalized sync key back to absolute local path.
        /// </summary>
        private static string ToLocalPath(string key)
        {
            string localRelative = key.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(AppPaths.DataDirectory, localRelative);
        }

        /// <summary>
        /// Uploads one local file to remote, creating parent directory when needed.
        /// </summary>
        private async Task<bool> UploadFileAsync(string localPath, string key)
        {
            if (_client == null)
            {
                return false;
            }

            int idx = key.LastIndexOf('/');
            if (idx <= 0) return await _client.UploadFileAsync(localPath, BuildRemotePath(key)).ConfigureAwait(false);
            string remoteDir = key.Substring(0, idx);
            if (!await _client.EnsureDirectoryAsync(BuildRemotePath(remoteDir)).ConfigureAwait(false))
            {
                return false;
            }

            return await _client.UploadFileAsync(localPath, BuildRemotePath(key)).ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads one remote file to local disk, creating local directory if needed.
        /// </summary>
        private async Task<bool> DownloadFileAsync(string remotePath, string localPath)
        {
            if (_client == null)
            {
                return false;
            }

            string? directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return await _client.DownloadFileAsync(remotePath, localPath).ConfigureAwait(false);
        }

        /// <summary>
        /// Determines whether a changed file should trigger note cache reload.
        /// </summary>
        private static bool ShouldReloadNotes(string key)
        {
            return key.Equals("notes.index.json", StringComparison.OrdinalIgnoreCase)
                   || key.StartsWith("notes/", StringComparison.OrdinalIgnoreCase)
                   || key.StartsWith("note-assets/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a changed file should trigger note-index repair after reload.
        /// </summary>
        private static bool ShouldRepairNoteIndex(string key)
        {
            return key.Equals("notes.index.json", StringComparison.OrdinalIgnoreCase)
                   || key.StartsWith("notes/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldRefreshPreviewStyles(string key)
        {
            return key.StartsWith("style/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a sync key belongs to the supported note data set.
        /// </summary>
        private static bool IsSyncableKey(string key)
        {
            return key.Equals("notes.index.json", StringComparison.OrdinalIgnoreCase)
                   || key.StartsWith("notes/", StringComparison.OrdinalIgnoreCase)
                   || key.StartsWith("note-assets/", StringComparison.OrdinalIgnoreCase)
                   || key.StartsWith("style/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reports a sync progress snapshot to any registered observer.
        /// </summary>
        private static void ReportProgress(IProgress<SyncProgressInfo>? progress, int completedSteps, int totalSteps, string statusText)
        {
            progress?.Report(new SyncProgressInfo
            {
                CompletedSteps = completedSteps,
                TotalSteps = totalSteps,
                StatusText = statusText
            });
        }
    }
}
