using YASN.AvaloniaNotes;

namespace YASN.Infrastructure.Sync
{
    /// <summary>
    /// Asset replication for <see cref="ThreeWaySyncEngine"/>: mirrors the <c>note-assets/…</c> files
    /// (pasted images and copied attachments) that note contents reference, so they follow their notes
    /// across devices instead of arriving as broken links.
    ///
    /// Asset blobs are immutable — <c>NoteAssetInserter</c> names each file with a fresh GUID, so a
    /// given relative path always denotes the same bytes everywhere. That makes the mirror a pure
    /// existence reconciliation per referenced path: upload when only the local copy exists, download
    /// when only the remote copy exists, and do nothing when both do. There is no content conflict to
    /// resolve and — deliberately, in this first version — no deletion: an asset no longer referenced is
    /// left in place on both sides rather than risking the removal of a blob another replica still needs.
    ///
    /// Linked (over-threshold) attachments are NOT synced: <c>NoteAssetInserter.LinkAttachment</c> emits
    /// an absolute <c>file://</c> URI pointing at the original file, which is machine-local and never
    /// lives under <c>note-assets/</c>, so the scanner does not match it. Only copied images and
    /// attachments (those under the size threshold) carry a relative <c>note-assets/</c> path and sync.
    ///
    /// The set of paths is derived from note contents (see <see cref="AssetReferenceScanner"/>) rather
    /// than a directory walk, because the remote listing API is depth-1 and cannot enumerate the nested
    /// asset tree. Running after note convergence means freshly downloaded notes contribute their asset
    /// references in the same pass.
    /// </summary>
    public sealed partial class ThreeWaySyncEngine
    {
        private const string RemoteAssetsDir = "note-assets";

        // Marks a copied attachment path (vs. a pasted image) so transfers can be reported per kind. Must
        // match NoteAssetInserter: attachments live under note-assets/attachments/, images directly under
        // note-assets/<noteId>/.
        private const string AttachmentsPathPrefix = "note-assets/attachments/";

        /// <summary>
        /// Mirrors every asset referenced by the current local notes between the data directory and the
        /// remote, by existence, logging per note how many images and attachments were transferred.
        /// Best-effort: a failure on one asset is logged and skipped so it never aborts note sync.
        /// Returns the total number of assets transferred.
        /// </summary>
        /// <param name="client">The live sync client for the pass.</param>
        /// <param name="cancellationToken">Cancels the asset mirror.</param>
        /// <returns>The count of assets uploaded or downloaded.</returns>
        private async Task<int> SyncAssetsAsync(ISyncClient client, CancellationToken cancellationToken)
        {
            List<AvaloniaNoteDocument> notes = repository.LoadAll().ToList();

            // Track parents already MKCOL'd and assets already mirrored this pass, so a path referenced by
            // several notes (or several assets sharing a folder) is not re-checked or re-created.
            HashSet<string> ensuredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, AssetMirrorResult> mirrored = new Dictionary<string, AssetMirrorResult>(StringComparer.OrdinalIgnoreCase);
            bool assetsRootEnsured = false;
            int totalTransferred = 0;

            foreach (AvaloniaNoteDocument note in notes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyCollection<string> referenced = AssetReferenceScanner.Collect(new[] { note.Content });
                if (referenced.Count == 0)
                {
                    continue;
                }

                int imagesRef = 0, attachmentsRef = 0;
                int imagesXfer = 0, attachmentsXfer = 0, missing = 0;

                foreach (string relativePath in referenced)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool isAttachment = relativePath.StartsWith(AttachmentsPathPrefix, StringComparison.OrdinalIgnoreCase);
                    if (isAttachment)
                    {
                        attachmentsRef++;
                    }
                    else
                    {
                        imagesRef++;
                    }

                    // Ensure the assets root once, lazily, only when a note actually references something.
                    if (!assetsRootEnsured)
                    {
                        if (!await client.EnsureDirectoryAsync(RemotePath(RemoteAssetsDir)).ConfigureAwait(false))
                        {
                            Logging.AppLogger.Warn("Asset sync skipped: could not ensure the remote note-assets directory.");
                            return totalTransferred;
                        }

                        assetsRootEnsured = true;
                    }

                    AssetMirrorResult result;
                    if (mirrored.TryGetValue(relativePath, out AssetMirrorResult cached))
                    {
                        result = cached;
                    }
                    else
                    {
                        try
                        {
                            result = await MirrorAssetAsync(client, relativePath, ensuredDirs).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
                        {
                            Logging.AppLogger.Warn($"Asset sync for '{relativePath}' failed: {ex.Message}");
                            result = AssetMirrorResult.Failed;
                        }

                        mirrored[relativePath] = result;
                    }

                    switch (result)
                    {
                        case AssetMirrorResult.Uploaded:
                        case AssetMirrorResult.Downloaded:
                            totalTransferred++;
                            if (isAttachment)
                            {
                                attachmentsXfer++;
                            }
                            else
                            {
                                imagesXfer++;
                            }

                            break;
                        case AssetMirrorResult.MissingBothSides:
                            missing++;
                            break;
                        default:
                            break;
                    }
                }

                Logging.AppLogger.Info(
                    $"Asset sync for note '{note.Title}' [{note.SyncKey}]: " +
                    $"{imagesRef} image(s) ({imagesXfer} transferred), " +
                    $"{attachmentsRef} attachment(s) ({attachmentsXfer} transferred)" +
                    (missing > 0 ? $", {missing} missing on both sides" : string.Empty) + ".");
            }

            if (totalTransferred > 0)
            {
                Logging.AppLogger.Info($"Asset sync complete: {totalTransferred} asset(s) transferred across {notes.Count} note(s).");
            }

            return totalTransferred;
        }

        /// <summary>The outcome of mirroring one asset path.</summary>
        private enum AssetMirrorResult
        {
            /// <summary>Already present on both sides (or transfer not needed).</summary>
            NoChange,

            /// <summary>The local-only asset was uploaded to the remote.</summary>
            Uploaded,

            /// <summary>The remote-only asset was downloaded locally.</summary>
            Downloaded,

            /// <summary>Referenced by content but absent on both sides (a broken link).</summary>
            MissingBothSides,

            /// <summary>A transfer was attempted but failed.</summary>
            Failed
        }

        /// <summary>
        /// Reconciles one asset path by existence. The local copy lives under the data directory at the
        /// same relative path the remote uses, so the two are mirror images. Uploads a local-only asset
        /// (creating its remote parent collection first, which WebDAV PUT does not do), downloads a
        /// remote-only asset, and leaves a path present on both untouched (immutable bytes).
        /// </summary>
        /// <param name="client">The live sync client.</param>
        /// <param name="relativePath">The asset path relative to the data directory (forward slashes).</param>
        /// <param name="ensuredDirs">Remote parent directories already created this pass.</param>
        /// <returns>The mirror outcome.</returns>
        private async Task<AssetMirrorResult> MirrorAssetAsync(ISyncClient client, string relativePath, HashSet<string> ensuredDirs)
        {
            string localPath = Path.Combine(AppPaths.DataDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string remotePath = RemotePath(relativePath);
            bool localExists = File.Exists(localPath);
            bool remoteExists = await client.FileExistsAsync(remotePath).ConfigureAwait(false);

            if (localExists && !remoteExists)
            {
                if (!await EnsureRemoteParentAsync(client, relativePath, ensuredDirs).ConfigureAwait(false))
                {
                    Logging.AppLogger.Warn($"Asset upload skipped: could not create remote parent for '{relativePath}'.");
                    return AssetMirrorResult.Failed;
                }

                if (await client.UploadFileAsync(localPath, remotePath).ConfigureAwait(false))
                {
                    Logging.AppLogger.Debug($"Asset uploaded: {relativePath}");
                    return AssetMirrorResult.Uploaded;
                }

                return AssetMirrorResult.Failed;
            }

            if (!localExists && remoteExists)
            {
                string? directory = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (await client.DownloadFileAsync(remotePath, localPath).ConfigureAwait(false))
                {
                    Logging.AppLogger.Debug($"Asset downloaded: {relativePath}");
                    return AssetMirrorResult.Downloaded;
                }

                return AssetMirrorResult.Failed;
            }

            if (!localExists && !remoteExists)
            {
                Logging.AppLogger.Warn($"Asset referenced but missing on both sides: {relativePath}");
                return AssetMirrorResult.MissingBothSides;
            }

            return AssetMirrorResult.NoChange;
        }

        /// <summary>
        /// Ensures the remote parent collection of an asset exists (WebDAV PUT will not create missing
        /// intermediate collections). Walks from the assets root down to the asset's immediate parent,
        /// skipping directories already created this pass.
        /// </summary>
        /// <param name="client">The live sync client.</param>
        /// <param name="relativePath">The asset path relative to the data directory (forward slashes).</param>
        /// <param name="ensuredDirs">Remote parent directories already created this pass.</param>
        /// <returns><see langword="true"/> when the parent exists or was created.</returns>
        private async Task<bool> EnsureRemoteParentAsync(ISyncClient client, string relativePath, HashSet<string> ensuredDirs)
        {
            int lastSlash = relativePath.LastIndexOf('/');
            if (lastSlash <= 0)
            {
                return true; // No parent beyond the assets root (already ensured by the caller).
            }

            string parentRelative = relativePath[..lastSlash];
            if (!ensuredDirs.Add(parentRelative))
            {
                return true; // Already ensured this pass.
            }

            // EnsureDirectoryAsync creates the full segment chain, so one call on the deepest parent
            // covers every intermediate collection.
            if (await client.EnsureDirectoryAsync(RemotePath(parentRelative)).ConfigureAwait(false))
            {
                return true;
            }

            ensuredDirs.Remove(parentRelative); // Allow a retry on a later asset.
            return false;
        }
    }
}
