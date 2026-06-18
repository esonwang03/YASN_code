using System.Net;
using WebDav;

namespace YASN.Infrastructure.Sync.WebDav
{
    /// <summary>
    /// WebDAV implementation backed by the WebDav.Client package.
    /// </summary>
    public class WebDavSyncClient : ISyncClient
    {
        private readonly IWebDavClient _client;
        private readonly HttpClientHandler _httpClientHandler;
        private readonly HttpClient _httpClient;

        public string BackendName => "WebDAV";
        public string? LastError { get; private set; }

        public WebDavSyncClient(WebDavOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.ServerUrl))
                throw new ArgumentException("ServerUrl is required", nameof(options));

            _httpClientHandler = CreateHttpClientHandler(options);
            _httpClient = new HttpClient(_httpClientHandler, disposeHandler: false)
            {
                BaseAddress = BuildBaseAddress(options.ServerUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("YASN-WebDav/1.0");

            _client = new WebDavClient(_httpClient);
        }

        public async Task<SyncProbeResult> ProbeConnectionAsync(string remotePath)
        {
            string path = NormalizeRemotePath(remotePath);

            try
            {
                // 1. Classify the endpoint/credentials with a metadata request before touching files.
                PropfindResponse meta = await _client.Propfind(path, new PropfindParameters
                {
                    ApplyTo = ApplyTo.Propfind.ResourceOnly
                }).ConfigureAwait(false);

                SyncProbeStatus? endpointFault = ClassifyEndpoint(meta.StatusCode);
                if (endpointFault is SyncProbeStatus fault)
                {
                    LastError = meta.Description ?? $"WebDAV status {meta.StatusCode}";
                    return new SyncProbeResult(fault, false, LastError);
                }

                // 2. Make sure the target directory exists (best effort; the round-trip is authoritative).
                if (path.Length > 0 && !await EnsureDirectoryAsync(path).ConfigureAwait(false))
                {
                    return new SyncProbeResult(SyncProbeStatus.DirectoryUnavailable, false, LastError);
                }

                // 3. Prove read/write end-to-end and read back the probe file's ETag in one round-trip.
                return await RoundTripProbeAsync(path).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                LastError = ex.Message;
                AppLogger.Warn($"WebDAV probe failed: {ex.Message}");
                return new SyncProbeResult(SyncProbeStatus.Unreachable, false, ex.Message);
            }
        }

        /// <summary>
        /// Maps a metadata-request status code to an endpoint-level fault, or null when the endpoint and
        /// credentials look healthy enough to proceed to the read/write round-trip. A 405 here means the
        /// URL is not a WebDAV endpoint (PROPFIND unsupported) — the exact wrong-URL case we must catch.
        /// </summary>
        private static SyncProbeStatus? ClassifyEndpoint(int statusCode)
        {
            return statusCode switch
            {
                (int)HttpStatusCode.Unauthorized => SyncProbeStatus.BadCredentials,
                (int)HttpStatusCode.Forbidden => SyncProbeStatus.WebDavDisabled,
                (int)HttpStatusCode.NotFound => SyncProbeStatus.EndpointNotFound,
                (int)HttpStatusCode.MethodNotAllowed => SyncProbeStatus.EndpointNotFound,
                _ => null
            };
        }

        /// <summary>
        /// Writes a unique probe file under <paramref name="dir"/>, downloads it back, byte-compares to
        /// prove read/write actually work, and reads its raw ETag to report server ETag support. The
        /// probe file is always deleted. Returns <see cref="SyncProbeStatus.ReadWriteFailed"/> if any
        /// step fails or the bytes round-trip incorrectly.
        /// </summary>
        private async Task<SyncProbeResult> RoundTripProbeAsync(string dir)
        {
            byte[] payload = Guid.NewGuid().ToByteArray();
            string probePath = dir.Length == 0 ? ".yasn-probe" : $"{dir}/.yasn-probe";
            string upTemp = Path.Combine(Path.GetTempPath(), $"yasn-probe-{Guid.NewGuid():N}.tmp");
            string downTemp = Path.Combine(Path.GetTempPath(), $"yasn-probe-{Guid.NewGuid():N}.bin");

            try
            {
                await File.WriteAllBytesAsync(upTemp, payload).ConfigureAwait(false);
                if (!await UploadFileAsync(upTemp, probePath).ConfigureAwait(false))
                {
                    return new SyncProbeResult(SyncProbeStatus.ReadWriteFailed, false, LastError);
                }

                if (!await DownloadFileAsync(probePath, downTemp).ConfigureAwait(false))
                {
                    return new SyncProbeResult(SyncProbeStatus.ReadWriteFailed, false, LastError);
                }

                byte[] readBack = await File.ReadAllBytesAsync(downTemp).ConfigureAwait(false);
                if (!readBack.AsSpan().SequenceEqual(payload))
                {
                    return new SyncProbeResult(SyncProbeStatus.ReadWriteFailed, false, "Round-trip payload mismatch.");
                }

                // Read the raw ETag (not the normalized "present" sentinel) so absence is detectable.
                PropfindResponse meta = await _client.Propfind(NormalizeRemotePath(probePath), new PropfindParameters
                {
                    ApplyTo = ApplyTo.Propfind.ResourceOnly
                }).ConfigureAwait(false);
                string? rawETag = meta.IsSuccessful ? meta.Resources?.FirstOrDefault()?.ETag : null;

                LastError = null;
                return new SyncProbeResult(SyncProbeStatus.Ok, !string.IsNullOrWhiteSpace(rawETag));
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException or IOException)
            {
                LastError = ex.Message;
                AppLogger.Warn($"WebDAV round-trip probe failed: {ex.Message}");
                return new SyncProbeResult(SyncProbeStatus.ReadWriteFailed, false, ex.Message);
            }
            finally
            {
                TryDeleteTemp(upTemp);
                TryDeleteTemp(downTemp);
                await DeleteFileAsync(probePath).ConfigureAwait(false);
            }
        }

        public async Task<bool> EnsureDirectoryAsync(string remotePath)
        {
            string normalized = NormalizeRemotePath(remotePath);

            if (string.IsNullOrEmpty(normalized))
            {
                return true;
            }

            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string current = string.Empty;

            foreach (string segment in segments)
            {
                current = string.IsNullOrEmpty(current) ? segment : $"{current}/{segment}";
                WebDavResponse response = await _client.Mkcol(current).ConfigureAwait(false);

                if (response.IsSuccessful || response.StatusCode == (int)HttpStatusCode.MethodNotAllowed)
                {
                    continue; // Already exists or created successfully
                }

                if (response.StatusCode == (int)HttpStatusCode.Conflict)
                {
                    LastError = "Parent directory is missing.";
                    return false;
                }

                LastError = response.Description ?? $"Failed to create {current}: {response.StatusCode}";
                return false;
            }

            LastError = null;
            return true;
        }

        public async Task<bool> UploadFileAsync(string localFilePath, string remoteFilePath)
        {
            string path = NormalizeRemotePath(remoteFilePath);

            if (!File.Exists(localFilePath))
            {
                LastError = "Local file not found.";
                return false;
            }

            try
            {
                FileStream stream = File.OpenRead(localFilePath);
                await using (stream.ConfigureAwait(false))
                {
                    WebDavResponse response = await _client.PutFile(path, stream, "application/octet-stream").ConfigureAwait(false);

                    if (response.IsSuccessful)
                    {
                        LastError = null;
                        return true;
                    }

                    LastError = response.Description ?? $"Upload failed with status {response.StatusCode}";
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                LastError = ex.Message;
                AppLogger.Debug($"WebDAV upload failed: {ex.Message}");
                return false;
            }
            catch (IOException ex)
            {
                LastError = ex.Message;
                AppLogger.Debug($"WebDAV upload failed: {ex.Message}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                LastError = ex.Message;
                AppLogger.Debug($"WebDAV upload failed: {ex.Message}");
                return false;
            }
            catch (TaskCanceledException ex)
            {
                LastError = ex.Message;
                AppLogger.Debug($"WebDAV upload failed: {ex.Message}");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                LastError = ex.Message;
                AppLogger.Debug($"WebDAV upload failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DownloadFileAsync(string remoteFilePath, string localFilePath)
        {
            string path = NormalizeRemotePath(remoteFilePath);

            try
            {
                WebDavStreamResponse response = await _client.GetRawFile(path).ConfigureAwait(false);
                if (!response.IsSuccessful || response.Stream == null)
                {
                    LastError = response.Description ?? $"Download failed with status {response.StatusCode}";
                    return false;
                }

                string? directory = Path.GetDirectoryName(localFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                Stream responseStream = response.Stream;
                FileStream fileStream = File.Create(localFilePath);
                await using (responseStream.ConfigureAwait(false))
                await using (fileStream.ConfigureAwait(false))
                {
                    await responseStream.CopyToAsync(fileStream).ConfigureAwait(false);
                }

                LastError = null;
                return true;
            }
            catch (HttpRequestException ex)
            {
                LastError = ex.Message;
                AppLogger.Debug($"WebDAV download failed: {ex.Message}");
                return false;
            }
            catch (IOException ex)
            {
                LastError = ex.Message;
                AppLogger.Debug($"WebDAV download failed: {ex.Message}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                LastError = ex.Message;
                AppLogger.Debug($"WebDAV download failed: {ex.Message}");
                return false;
            }
            catch (TaskCanceledException ex)
            {
                LastError = ex.Message;
                AppLogger.Debug($"WebDAV download failed: {ex.Message}");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                LastError = ex.Message;
                AppLogger.Debug($"WebDAV download failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteFileAsync(string remoteFilePath)
        {
            string path = NormalizeRemotePath(remoteFilePath);

            try
            {
                WebDavResponse response = await _client.Delete(path).ConfigureAwait(false);

                // Treat an already-absent file as success: the post-condition (file gone) holds.
                if (response.IsSuccessful || response.StatusCode == (int)HttpStatusCode.NotFound)
                {
                    LastError = null;
                    return true;
                }

                LastError = response.Description ?? $"Delete failed with status {response.StatusCode}";
                return false;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                LastError = ex.Message;
                AppLogger.Debug($"WebDAV delete failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> FileExistsAsync(string remoteFilePath)
        {
            string path = NormalizeRemotePath(remoteFilePath);

            try
            {
                PropfindResponse response = await _client.Propfind(path, new PropfindParameters
                {
                    ApplyTo = ApplyTo.Propfind.ResourceOnly
                }).ConfigureAwait(false);

                return response.IsSuccessful;
            }
            catch (HttpRequestException ex)
            {
                AppLogger.Debug($"WebDAV exists check failed: {ex.Message}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Debug($"WebDAV exists check failed: {ex.Message}");
                return false;
            }
            catch (TaskCanceledException ex)
            {
                AppLogger.Debug($"WebDAV exists check failed: {ex.Message}");
                return false;
            }
        }

        public async Task<DateTime?> GetFileLastModifiedAsync(string remoteFilePath)
        {
            string path = NormalizeRemotePath(remoteFilePath);

            try
            {
                PropfindResponse response = await _client.Propfind(path, new PropfindParameters
                {
                    ApplyTo = ApplyTo.Propfind.ResourceOnly
                }).ConfigureAwait(false);

                if (!response.IsSuccessful)
                {
                    return null;
                }

                WebDavResource? resource = response.Resources?.FirstOrDefault();
                return resource?.LastModifiedDate;
            }
            catch (HttpRequestException ex)
            {
                AppLogger.Debug($"WebDAV last-modified check failed: {ex.Message}");
                return null;
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Debug($"WebDAV last-modified check failed: {ex.Message}");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                AppLogger.Debug($"WebDAV last-modified check failed: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> GetFileHashAsync(string remoteFilePath)
        {
            string path = NormalizeRemotePath(remoteFilePath);

            try
            {
                WebDavStreamResponse response = await _client.GetRawFile(path).ConfigureAwait(false);
                if (!response.IsSuccessful || response.Stream == null)
                {
                    return null;
                }

                await using (response.Stream)
                {
                    return FileHashUtil.ComputeStreamHash(response.Stream);
                }
            }
            catch (HttpRequestException ex)
            {
                AppLogger.Debug($"WebDAV hash failed: {ex.Message}");
                return null;
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Debug($"WebDAV hash failed: {ex.Message}");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                AppLogger.Debug($"WebDAV hash failed: {ex.Message}");
                return null;
            }
        }

        public async Task<bool?> SupportsETagsAsync(string remoteDirectoryPath)
        {
            string dir = NormalizeRemotePath(remoteDirectoryPath);
            if (!await EnsureDirectoryAsync(dir).ConfigureAwait(false))
            {
                return null;
            }

            string probePath = dir.Length == 0 ? ".yasn-etag-probe" : $"{dir}/.yasn-etag-probe";
            string tempPath = Path.Combine(Path.GetTempPath(), $"yasn-probe-{Guid.NewGuid():N}.tmp");

            try
            {
                await File.WriteAllBytesAsync(tempPath, new byte[] { 0x59 }).ConfigureAwait(false);
                if (!await UploadFileAsync(tempPath, probePath).ConfigureAwait(false))
                {
                    return null;
                }

                // Read the raw ETag (not the normalized "present" sentinel) so absence is detectable.
                PropfindResponse response = await _client.Propfind(NormalizeRemotePath(probePath), new PropfindParameters
                {
                    ApplyTo = ApplyTo.Propfind.ResourceOnly
                }).ConfigureAwait(false);

                string? rawETag = response.IsSuccessful ? response.Resources?.FirstOrDefault()?.ETag : null;
                return !string.IsNullOrWhiteSpace(rawETag);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException or IOException)
            {
                AppLogger.Debug($"WebDAV ETag probe failed: {ex.Message}");
                return null;
            }
            finally
            {
                TryDeleteTemp(tempPath);
                await DeleteFileAsync(probePath).ConfigureAwait(false);
            }
        }

        private static void TryDeleteTemp(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLogger.Debug($"WebDAV probe temp cleanup failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _client.Dispose();
            _httpClient.Dispose();
            _httpClientHandler.Dispose();
        }

        public async Task<string?> GetFileETagAsync(string remoteFilePath)
        {
            string path = NormalizeRemotePath(remoteFilePath);

            try
            {
                PropfindResponse response = await _client.Propfind(path, new PropfindParameters
                {
                    ApplyTo = ApplyTo.Propfind.ResourceOnly
                }).ConfigureAwait(false);

                if (!response.IsSuccessful)
                {
                    return null;
                }

                return NormalizeETag(response.Resources?.FirstOrDefault()?.ETag);
            }
            catch (HttpRequestException ex)
            {
                AppLogger.Debug($"WebDAV etag check failed: {ex.Message}");
                return null;
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Debug($"WebDAV etag check failed: {ex.Message}");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                AppLogger.Debug($"WebDAV etag check failed: {ex.Message}");
                return null;
            }
        }

        public async Task<RemoteListing> ListDirectoryAsync(string remoteDirectoryPath)
        {
            string dir = NormalizeRemotePath(remoteDirectoryPath);

            try
            {
                PropfindResponse response = await _client.Propfind(dir, new PropfindParameters
                {
                    ApplyTo = ApplyTo.Propfind.ResourceAndChildren
                }).ConfigureAwait(false);

                if (!response.IsSuccessful || response.Resources is null)
                {
                    LastError = response.Description ?? $"List failed with status {response.StatusCode}";
                    AppLogger.Warn($"WebDAV list of '{dir}' failed: {LastError}");
                    return RemoteListing.Failure();
                }

                List<RemoteEntry> entries = new List<RemoteEntry>();
                foreach (WebDavResource resource in response.Resources)
                {
                    if (resource.IsCollection)
                    {
                        continue;
                    }

                    string relative = ToRelativeRemotePath(resource.Uri);
                    if (relative.Length == 0 || string.Equals(relative.TrimEnd('/'), dir, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    entries.Add(new RemoteEntry(relative, NormalizeETag(resource.ETag), resource.LastModifiedDate));
                }

                LastError = null;
                return RemoteListing.Success(entries);
            }
            catch (HttpRequestException ex)
            {
                LastError = ex.Message;
                AppLogger.Warn($"WebDAV list failed: {ex.Message}");
                return RemoteListing.Failure();
            }
            catch (InvalidOperationException ex)
            {
                LastError = ex.Message;
                AppLogger.Warn($"WebDAV list failed: {ex.Message}");
                return RemoteListing.Failure();
            }
            catch (TaskCanceledException ex)
            {
                LastError = ex.Message;
                AppLogger.Warn($"WebDAV list failed: {ex.Message}");
                return RemoteListing.Failure();
            }
        }

        private static HttpClientHandler CreateHttpClientHandler(WebDavOptions options)
        {
            HttpClientHandler handler = new HttpClientHandler
            {
                PreAuthenticate = true,
                UseDefaultCredentials = false,
                UseProxy = false,
                CheckCertificateRevocationList = true
            };

            if (options.AllowInvalidCertificates)
            {
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            if (!string.IsNullOrEmpty(options.Username))
            {
                handler.Credentials = new NetworkCredential(options.Username, options.Password);
            }

            return handler;
        }

        private static Uri BuildBaseAddress(string baseAddress)
        {
            string formatted = baseAddress.TrimEnd('/') + "/";
            return new Uri(formatted);
        }

        private static string NormalizeRemotePath(string? remotePath)
        {
            return (remotePath ?? string.Empty).Trim().Trim('/');
        }

        private static string? NormalizeETag(string? etag)
        {
            if (string.IsNullOrWhiteSpace(etag))
            {
                return null;
            }

            // Strip weak-validator prefix and surrounding quotes for stable comparison.
            string trimmed = etag.Trim();
            if (trimmed.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(2);
            }

            return trimmed.Trim('"');
        }

        private string ToRelativeRemotePath(string? resourceUri)
        {
            if (string.IsNullOrWhiteSpace(resourceUri))
            {
                return string.Empty;
            }

            string path = resourceUri;
            if (Uri.TryCreate(resourceUri, UriKind.Absolute, out Uri? absolute))
            {
                path = absolute.AbsolutePath;
            }

            path = Uri.UnescapeDataString(path);

            // Drop the server's base path prefix so the result is relative to the configured root.
            string basePath = _client is not null ? _httpClient.BaseAddress?.AbsolutePath ?? "/" : "/";
            if (basePath.Length > 1 && path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(basePath.Length);
            }

            return path.Trim('/');
        }
    }
}


