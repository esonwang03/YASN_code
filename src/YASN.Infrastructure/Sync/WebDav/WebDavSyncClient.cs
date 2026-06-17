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

        public async Task<bool> TestConnectionAsync(string remotePath)
        {
            string path = NormalizeRemotePath(remotePath);

            try
            {
                PropfindResponse response = await _client.Propfind(path, new PropfindParameters
                {
                    ApplyTo = ApplyTo.Propfind.ResourceOnly
                }).ConfigureAwait(false);

                if (response.IsSuccessful || !string.IsNullOrEmpty(path) && await EnsureDirectoryAsync(path).ConfigureAwait(false))
                {
                    LastError = null;
                    return true;
                }

                LastError = response.Description ?? $"WebDAV status {response.StatusCode}";
                return false;
            }
            catch (HttpRequestException ex)
            {
                LastError = ex.Message;
                AppLogger.Warn($"WebDAV test failed: {ex.Message}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                LastError = ex.Message;
                AppLogger.Warn($"WebDAV test failed: {ex.Message}");
                return false;
            }
            catch (TaskCanceledException ex)
            {
                LastError = ex.Message;
                AppLogger.Warn($"WebDAV test failed: {ex.Message}");
                return false;
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

        public async Task<IReadOnlyList<RemoteEntry>> ListDirectoryAsync(string remoteDirectoryPath)
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
                    return Array.Empty<RemoteEntry>();
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

                    entries.Add(new RemoteEntry(relative, NormalizeETag(resource.ETag)));
                }

                return entries;
            }
            catch (HttpRequestException ex)
            {
                AppLogger.Debug($"WebDAV list failed: {ex.Message}");
                return Array.Empty<RemoteEntry>();
            }
            catch (InvalidOperationException ex)
            {
                AppLogger.Debug($"WebDAV list failed: {ex.Message}");
                return Array.Empty<RemoteEntry>();
            }
            catch (TaskCanceledException ex)
            {
                AppLogger.Debug($"WebDAV list failed: {ex.Message}");
                return Array.Empty<RemoteEntry>();
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


