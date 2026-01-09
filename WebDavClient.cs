using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace YASN
{
    /// <summary>
    /// WebDAV 客户端，用于连接和操作 WebDAV 服务器
    /// </summary>
    public class WebDavClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _username;
        private readonly string _password;
        
        public string LastError { get; private set; }

        public WebDavClient(string baseUrl, string username, string password)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _username = username;
            _password = password;

            // Force TLS 1.2 or higher (required by many servers)
            System.Net.ServicePointManager.SecurityProtocol = 
                System.Net.SecurityProtocolType.Tls12 | 
                System.Net.SecurityProtocolType.Tls13;
            
            // Disable proxy - this might be interfering
            System.Net.WebRequest.DefaultWebProxy = null;
            System.Net.ServicePointManager.Expect100Continue = false;
            
            // Also set certificate validation callback globally for HttpWebRequest
            System.Net.ServicePointManager.ServerCertificateValidationCallback = 
                (sender, certificate, chain, sslPolicyErrors) => true;

            // Create HttpClientHandler to handle redirects and SSL
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                PreAuthenticate = true,
                UseDefaultCredentials = false,
                UseProxy = false,  // Disable proxy for HttpClient too
                Proxy = null,
                // For development/testing - in production, should verify certificates
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            _httpClient = new HttpClient(handler);
            
            // Set basic authentication
            var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "YASN/1.0");
            _httpClient.DefaultRequestHeaders.ExpectContinue = false;
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            System.Diagnostics.Debug.WriteLine($"=== WebDAV Client Initialization ===");
            System.Diagnostics.Debug.WriteLine($"Base URL: {_baseUrl}");
            System.Diagnostics.Debug.WriteLine($"Username: {_username}");
            System.Diagnostics.Debug.WriteLine($"Password length: {password?.Length ?? 0} characters");
            System.Diagnostics.Debug.WriteLine($"Auth token (Base64): {authToken}");
            System.Diagnostics.Debug.WriteLine($"Auth header: Basic {authToken}");
            System.Diagnostics.Debug.WriteLine($"PreAuthenticate: True");
            System.Diagnostics.Debug.WriteLine($"UseDefaultCredentials: False");
            System.Diagnostics.Debug.WriteLine($"UseProxy: False");
            System.Diagnostics.Debug.WriteLine($"TLS Version: TLS 1.2/1.3");
            System.Diagnostics.Debug.WriteLine($"Expect100Continue: False");
            
            // Verify encoding
            var reconstructed = Encoding.UTF8.GetString(Convert.FromBase64String(authToken));
            System.Diagnostics.Debug.WriteLine($"Reconstructed: {reconstructed.Substring(0, Math.Min(20, reconstructed.Length))}...");
        }

        /// <summary>
        /// Test connection to WebDAV server
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== Starting Connection Test ===");
                System.Diagnostics.Debug.WriteLine($"Testing connection to: {_baseUrl}");
                
                // Try method 3: Minimal HttpWebRequest (absolutely minimal, like PowerShell default)
                System.Diagnostics.Debug.WriteLine("Attempting MINIMAL HttpWebRequest (PowerShell default style)...");
                try
                {
                    var minimalRequest = (HttpWebRequest)WebRequest.Create(_baseUrl);
                    minimalRequest.Method = "PROPFIND";
                    minimalRequest.Headers.Add("Authorization", $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}"))}");
                    minimalRequest.Headers.Add("Depth", "0");
                    minimalRequest.ContentType = "application/xml";
                    // Don't set anything else - let it use defaults
                    
                    var propfindXml = @"<?xml version=""1.0""?><D:propfind xmlns:D=""DAV:""><D:prop><D:resourcetype/></D:prop></D:propfind>";
                    var bytes = Encoding.UTF8.GetBytes(propfindXml);
                    minimalRequest.ContentLength = bytes.Length;
                    
                    using (var requestStream = minimalRequest.GetRequestStream())
                    {
                        requestStream.Write(bytes, 0, bytes.Length);
                    }
                    
                    using (var response = (HttpWebResponse)minimalRequest.GetResponse())
                    {
                        System.Diagnostics.Debug.WriteLine($"MINIMAL HttpWebRequest response: {(int)response.StatusCode} {response.StatusCode}");
                        
                        if (response.StatusCode == HttpStatusCode.MultiStatus || response.StatusCode == HttpStatusCode.OK)
                        {
                            LastError = null;
                            System.Diagnostics.Debug.WriteLine("? MINIMAL HttpWebRequest succeeded!");
                            return true;
                        }
                    }
                }
                catch (WebException minEx)
                {
                    if (minEx.Response is HttpWebResponse minErrorResponse)
                    {
                        System.Diagnostics.Debug.WriteLine($"MINIMAL HttpWebRequest failed: {(int)minErrorResponse.StatusCode}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"MINIMAL HttpWebRequest exception: {minEx.Message}");
                    }
                }
                
                // Debug: Check if auth header is set
                if (_httpClient.DefaultRequestHeaders.Authorization != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Auth header present: {_httpClient.DefaultRequestHeaders.Authorization.Scheme} [token]");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("WARNING: No auth header set!");
                }
                
                // ALTERNATIVE: Try using HttpWebRequest instead of HttpClient
                // This is what PowerShell uses successfully
                System.Diagnostics.Debug.WriteLine("Attempting HttpWebRequest method (same as PowerShell)...");
                HttpWebRequest request = null;
                try
                {
                    request = (HttpWebRequest)WebRequest.Create(_baseUrl);
                    request.Method = "PROPFIND";
                    
                    // Set TLS version explicitly
                    System.Diagnostics.Debug.WriteLine($"ServicePointManager.SecurityProtocol: {System.Net.ServicePointManager.SecurityProtocol}");
                    System.Diagnostics.Debug.WriteLine($"Expect100Continue: {System.Net.ServicePointManager.Expect100Continue}");
                    
                    request.Headers.Add("Authorization", $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}"))}");
                    request.Headers.Add("Depth", "0");
                    request.ContentType = "application/xml";
                    request.UserAgent = "YASN/1.0";
                    request.AllowAutoRedirect = true;
                    request.PreAuthenticate = true;
                    request.Proxy = null;  // Disable proxy
                    
                    // Certificate validation is already set globally in constructor
                    
                    var propfindXml = @"<?xml version=""1.0""?><D:propfind xmlns:D=""DAV:""><D:prop><D:resourcetype/></D:prop></D:propfind>";
                    var bytes = Encoding.UTF8.GetBytes(propfindXml);
                    request.ContentLength = bytes.Length;
                    
                    System.Diagnostics.Debug.WriteLine("HttpWebRequest: Getting request stream...");
                    using (var requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(bytes, 0, bytes.Length);
                    }
                    
                    System.Diagnostics.Debug.WriteLine("HttpWebRequest: Getting response...");
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        System.Diagnostics.Debug.WriteLine($"HttpWebRequest PROPFIND response: {(int)response.StatusCode} {response.StatusCode}");
                        
                        if (response.StatusCode == HttpStatusCode.MultiStatus || 
                            response.StatusCode == HttpStatusCode.OK)
                        {
                            LastError = null;
                            System.Diagnostics.Debug.WriteLine("? HttpWebRequest succeeded - connection is working!");
                            return true;
                        }
                    }
                }
                catch (WebException webEx)
                {
                    System.Diagnostics.Debug.WriteLine($"HttpWebRequest WebException: {webEx.Message}");
                    System.Diagnostics.Debug.WriteLine($"WebException Status: {webEx.Status}");
                    
                    if (webEx.InnerException != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Inner exception: {webEx.InnerException.Message}");
                    }
                    
                    if (webEx.Response is HttpWebResponse errorResponse)
                    {
                        System.Diagnostics.Debug.WriteLine($"HttpWebRequest failed: {(int)errorResponse.StatusCode} {errorResponse.StatusCode}");
                        
                        // Read response body for more details
                        try
                        {
                            using (var reader = new System.IO.StreamReader(errorResponse.GetResponseStream()))
                            {
                                var responseBody = reader.ReadToEnd();
                                if (!string.IsNullOrEmpty(responseBody))
                                {
                                    System.Diagnostics.Debug.WriteLine($"Response body: {responseBody.Substring(0, Math.Min(200, responseBody.Length))}");
                                }
                            }
                        }
                        catch { }
                        
                        if (errorResponse.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            LastError = "Unauthorized via HttpWebRequest";
                            System.Diagnostics.Debug.WriteLine(LastError);
                            
                            // Check if auth header was actually sent
                            if (request != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"Request had Authorization header: {request.Headers["Authorization"] != null}");
                            }
                        }
                    }
                    else
                    {
                        // No response - likely SSL or network issue
                        System.Diagnostics.Debug.WriteLine("No HTTP response received - SSL or network issue");
                        LastError = $"Network error: {webEx.Message}";
                        
                        if (webEx.Status == WebExceptionStatus.SecureChannelFailure ||
                            webEx.Status == WebExceptionStatus.TrustFailure)
                        {
                            System.Diagnostics.Debug.WriteLine("SSL/TLS error detected!");
                            LastError = "SSL/TLS connection failed. Check TLS version support.";
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"HttpWebRequest unexpected exception: {ex.GetType().Name}");
                    System.Diagnostics.Debug.WriteLine($"Message: {ex.Message}");
                }
                
                // Original HttpClient method
                System.Diagnostics.Debug.WriteLine("Attempting HttpClient method...");
                
                // Try PROPFIND - this is the most reliable test for WebDAV with authentication
                var propfindRequest = new HttpRequestMessage(new HttpMethod("PROPFIND"), _baseUrl);
                propfindRequest.Headers.Add("Depth", "0");
                
                var propfindXml2 = @"<?xml version=""1.0"" encoding=""utf-8""?>
<D:propfind xmlns:D=""DAV:"">
    <D:prop>
        <D:resourcetype/>
    </D:prop>
</D:propfind>";
                
                propfindRequest.Content = new StringContent(propfindXml2, Encoding.UTF8, "application/xml");
                
                System.Diagnostics.Debug.WriteLine("Sending PROPFIND request...");
                var propfindResponse = await _httpClient.SendAsync(propfindRequest);
                System.Diagnostics.Debug.WriteLine($"PROPFIND response: {(int)propfindResponse.StatusCode} {propfindResponse.StatusCode}");
                
                // Check response headers
                if (propfindResponse.Headers.Contains("WWW-Authenticate"))
                {
                    var authChallenge = string.Join(", ", propfindResponse.Headers.GetValues("WWW-Authenticate"));
                    System.Diagnostics.Debug.WriteLine($"Auth challenge: {authChallenge}");
                }
                
                // 207 Multi-Status is the correct response for PROPFIND
                if (propfindResponse.StatusCode == HttpStatusCode.MultiStatus || propfindResponse.IsSuccessStatusCode)
                {
                    LastError = null;
                    System.Diagnostics.Debug.WriteLine("? PROPFIND succeeded - connection is working");
                    return true;
                }
                
                // If PROPFIND returns 401, authentication definitely failed
                if (propfindResponse.StatusCode == HttpStatusCode.Unauthorized)
                {
                    LastError = "Unauthorized - Check username and password. For Jianguoyun, use App Password!";
                    System.Diagnostics.Debug.WriteLine($"? {LastError}");
                    System.Diagnostics.Debug.WriteLine("Hint: Double-check that the password is correct and doesn't contain hidden characters");
                    return false;
                }
                
                // If PROPFIND returns 403, might be path issue, try OPTIONS
                if (propfindResponse.StatusCode == HttpStatusCode.Forbidden)
                {
                    System.Diagnostics.Debug.WriteLine("PROPFIND returned 403, trying OPTIONS...");
                    
                    var optionsRequest = new HttpRequestMessage(HttpMethod.Options, _baseUrl);
                    var optionsResponse = await _httpClient.SendAsync(optionsRequest);
                    System.Diagnostics.Debug.WriteLine($"OPTIONS response: {(int)optionsResponse.StatusCode} {optionsResponse.StatusCode}");
                    
                    if (optionsResponse.IsSuccessStatusCode)
                    {
                        // OPTIONS succeeded, so authentication is OK
                        // 403 on PROPFIND is just a path permission issue
                        LastError = null;
                        System.Diagnostics.Debug.WriteLine("? OPTIONS succeeded - authentication is working (403 on root is normal)");
                        return true;
                    }
                    
                    if (optionsResponse.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        LastError = "Unauthorized - Check username and password. For Jianguoyun, use App Password!";
                        System.Diagnostics.Debug.WriteLine($"? {LastError}");
                        return false;
                    }
                }
                
                LastError = $"Connection test failed: {propfindResponse.StatusCode} - {propfindResponse.ReasonPhrase}";
                System.Diagnostics.Debug.WriteLine(LastError);
                return false;
            }
            catch (HttpRequestException ex)
            {
                LastError = $"HTTP Error: {ex.Message}";
                if (ex.InnerException != null)
                {
                    LastError += $"\nInner: {ex.InnerException.Message}";
                }
                System.Diagnostics.Debug.WriteLine($"? TestConnection failed: {LastError}");
                return false;
            }
            catch (TaskCanceledException ex)
            {
                LastError = "Connection timeout. Please check your network or server URL.";
                System.Diagnostics.Debug.WriteLine($"? TestConnection timeout: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                LastError = $"Unexpected error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"? TestConnection exception: {ex.GetType().Name} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Upload file to WebDAV server
        /// </summary>
        public async Task<bool> UploadFileAsync(string localFilePath, string remoteFilePath)
        {
            try
            {
                if (!File.Exists(localFilePath))
                    return false;

                var fileContent = await File.ReadAllBytesAsync(localFilePath);
                var content = new ByteArrayContent(fileContent);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                var url = $"{_baseUrl}/{remoteFilePath.TrimStart('/')}";
                var request = new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Content = content
                };

                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Upload failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Download file from WebDAV server
        /// </summary>
        public async Task<bool> DownloadFileAsync(string remoteFilePath, string localFilePath)
        {
            try
            {
                var url = $"{_baseUrl}/{remoteFilePath.TrimStart('/')}";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return false;

                var content = await response.Content.ReadAsByteArrayAsync();
                
                // Ensure directory exists
                var directory = Path.GetDirectoryName(localFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllBytesAsync(localFilePath, content);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Download failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if remote file exists
        /// </summary>
        public async Task<bool> FileExistsAsync(string remoteFilePath)
        {
            try
            {
                var url = $"{_baseUrl}/{remoteFilePath.TrimStart('/')}";
                var request = new HttpRequestMessage(HttpMethod.Head, url);
                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get remote file last modified time
        /// </summary>
        public async Task<DateTime?> GetFileLastModifiedAsync(string remoteFilePath)
        {
            try
            {
                var url = $"{_baseUrl}/{remoteFilePath.TrimStart('/')}";
                var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
                request.Headers.Add("Depth", "0");
                
                var propfindXml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<D:propfind xmlns:D=""DAV:"">
    <D:prop>
        <D:getlastmodified/>
    </D:prop>
</D:propfind>";
                
                request.Content = new StringContent(propfindXml, Encoding.UTF8, "application/xml");
                
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return null;

                var responseContent = await response.Content.ReadAsStringAsync();
                var xml = XDocument.Parse(responseContent);
                
                var ns = XNamespace.Get("DAV:");
                var lastModified = xml.Descendants(ns + "getlastmodified").FirstOrDefault()?.Value;
                
                if (DateTime.TryParse(lastModified, out var result))
                    return result;
                    
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Get last modified failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Create directory on WebDAV server
        /// </summary>
        public async Task<bool> CreateDirectoryAsync(string remoteDirectoryPath)
        {
            try
            {
                var url = $"{_baseUrl}/{remoteDirectoryPath.TrimStart('/')}";
                var request = new HttpRequestMessage(new HttpMethod("MKCOL"), url);
                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MethodNotAllowed; // Already exists
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Test connection to a specific path (more useful than testing root for some services)
        /// </summary>
        public async Task<bool> TestPathAccessAsync(string remotePath)
        {
            try
            {
                var url = $"{_baseUrl}/{remotePath.TrimStart('/')}";
                System.Diagnostics.Debug.WriteLine($"Testing path access: {url}");
                
                var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
                request.Headers.Add("Depth", "0");
                
                var propfindXml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<D:propfind xmlns:D=""DAV:"">
    <D:prop>
        <D:resourcetype/>
    </D:prop>
</D:propfind>";
                
                request.Content = new StringContent(propfindXml, Encoding.UTF8, "application/xml");
                
                var response = await _httpClient.SendAsync(request);
                
                System.Diagnostics.Debug.WriteLine($"Path access test result: {response.StatusCode}");
                
                if (response.IsSuccessStatusCode)
                {
                    LastError = null;
                    return true;
                }
                
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    LastError = "Path not found. Please create the folder first.";
                    System.Diagnostics.Debug.WriteLine(LastError);
                    return false;
                }
                
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    LastError = "Access forbidden. Check folder permissions or create folder in web UI first.";
                    System.Diagnostics.Debug.WriteLine(LastError);
                    return false;
                }
                
                LastError = $"Path test failed: {response.StatusCode} - {response.ReasonPhrase}";
                System.Diagnostics.Debug.WriteLine(LastError);
                return false;
            }
            catch (Exception ex)
            {
                LastError = $"Path test error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"TestPathAccess failed: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
