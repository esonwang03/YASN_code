using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using WebDav;
using YASN.Logging;

namespace YASN.Sync.WebDav
{
    /// <summary>
    /// WebDAV implementation backed by the WebDav.Client package.
    /// </summary>
    public class WebDavSyncClient : ISyncClient
    {
        private readonly IWebDavClient _client;

        public string BackendName => "WebDAV";
        public string LastError { get; private set; }

        public WebDavSyncClient(WebDavOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.ServerUrl))
                throw new ArgumentException("ServerUrl is required", nameof(options));

            var handler = new HttpClientHandler
            {
                PreAuthenticate = true,
                UseDefaultCredentials = false,
                UseProxy = false
            };

            if (options.AllowInvalidCertificates)
            {
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            if (!string.IsNullOrEmpty(options.Username))
            {
                handler.Credentials = new NetworkCredential(options.Username, options.Password);
            }

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = BuildBaseAddress(options.ServerUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };

            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("YASN-WebDav/1.0");

            _client = new WebDavClient(httpClient);
        }

        public async Task<bool> TestConnectionAsync(string remotePath)
        {
            var path = NormalizeRemotePath(remotePath);

            try
            {
                var response = await _client.Propfind(path, new PropfindParameters
                {
                    ApplyTo = ApplyTo.Propfind.ResourceOnly
                });

                if (response.IsSuccessful)
                {
                    LastError = null;
                    return true;
                }

                if (!string.IsNullOrEmpty(path) && await EnsureDirectoryAsync(path))
                {
                    LastError = null;
                    return true;
                }

                LastError = response.Description ?? $"WebDAV status {response.StatusCode}";
                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AppLogger.Debug($"WebDAV test failed: {ex}");
                return false;
            }
        }

        public async Task<bool> EnsureDirectoryAsync(string remotePath)
        {
            var normalized = NormalizeRemotePath(remotePath);

            if (string.IsNullOrEmpty(normalized))
            {
                return true;
            }

            var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = string.Empty;

            foreach (var segment in segments)
            {
                current = string.IsNullOrEmpty(current) ? segment : $"{current}/{segment}";
                var response = await _client.Mkcol(current);

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
            var path = NormalizeRemotePath(remoteFilePath);

            if (!File.Exists(localFilePath))
            {
                LastError = "Local file not found.";
                return false;
            }

            try
            {
                await using var stream = File.OpenRead(localFilePath);
                var response = await _client.PutFile(path, stream, "application/octet-stream");

                if (response.IsSuccessful)
                {
                    LastError = null;
                    return true;
                }

                LastError = response.Description ?? $"Upload failed with status {response.StatusCode}";
                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AppLogger.Debug($"WebDAV upload failed: {ex}");
                return false;
            }
        }

        public async Task<bool> DownloadFileAsync(string remoteFilePath, string localFilePath)
        {
            var path = NormalizeRemotePath(remoteFilePath);

            try
            {
                var response = await _client.GetRawFile(path);
                if (!response.IsSuccessful || response.Stream == null)
                {
                    LastError = response.Description ?? $"Download failed with status {response.StatusCode}";
                    return false;
                }

                var directory = Path.GetDirectoryName(localFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await using var responseStream = response.Stream;
                await using var fileStream = File.Create(localFilePath);
                await responseStream.CopyToAsync(fileStream);

                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AppLogger.Debug($"WebDAV download failed: {ex}");
                return false;
            }
        }

        public async Task<bool> FileExistsAsync(string remoteFilePath)
        {
            var path = NormalizeRemotePath(remoteFilePath);

            try
            {
                var response = await _client.Propfind(path, new PropfindParameters
                {
                    ApplyTo = ApplyTo.Propfind.ResourceOnly
                });

                return response.IsSuccessful;
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"WebDAV exists check failed: {ex.Message}");
                return false;
            }
        }

        public async Task<DateTime?> GetFileLastModifiedAsync(string remoteFilePath)
        {
            var path = NormalizeRemotePath(remoteFilePath);

            try
            {
                var response = await _client.Propfind(path, new PropfindParameters
                {
                    ApplyTo = ApplyTo.Propfind.ResourceOnly
                });

                if (!response.IsSuccessful)
                {
                    return null;
                }

                var resource = response.Resources?.FirstOrDefault();
                return resource?.LastModifiedDate;
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"WebDAV last-modified check failed: {ex.Message}");
                return null;
            }
        }

        public async Task<string> GetFileHashAsync(string remoteFilePath)
        {
            var path = NormalizeRemotePath(remoteFilePath);

            try
            {
                var response = await _client.GetRawFile(path);
                if (!response.IsSuccessful || response.Stream == null)
                {
                    return null;
                }

                await using (response.Stream)
                {
                    return FileHashUtil.ComputeStreamHash(response.Stream);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"WebDAV hash failed: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }

        private static Uri BuildBaseAddress(string baseAddress)
        {
            var formatted = baseAddress.TrimEnd('/') + "/";
            return new Uri(formatted);
        }

        private static string NormalizeRemotePath(string remotePath)
        {
            return (remotePath ?? string.Empty).Trim().Trim('/');
        }
    }
}


