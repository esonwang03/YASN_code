// Quick WebDAV Connection Tester
// Compile: csc /out:webdav_test.exe webdav_quick_test.cs
// Or just paste this into LINQPad/C# Interactive

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

public class WebDavQuickTest
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("WebDAV Quick Connection Tester");
        Console.WriteLine("========================================");
        Console.WriteLine();

        // Get input
        Console.Write("Server URL: ");
        var serverUrl = Console.ReadLine();
        
        Console.Write("Username: ");
        var username = Console.ReadLine();
        
        Console.Write("Password: ");
        var password = ReadPassword();
        Console.WriteLine();
        Console.WriteLine();

        // Test connection
        Console.WriteLine("Testing connection...");
        Console.WriteLine();

        try
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                ServerCertificateCustomValidationCallback = (m, c, ch, e) => true
            };

            using (var client = new HttpClient(handler))
            {
                // Set authentication
                var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
                client.DefaultRequestHeaders.Add("User-Agent", "YASN-Test/1.0");
                client.Timeout = TimeSpan.FromSeconds(30);

                Console.WriteLine($"[1/3] Testing OPTIONS {serverUrl}");
                try
                {
                    var optionsRequest = new HttpRequestMessage(HttpMethod.Options, serverUrl);
                    var optionsResponse = await client.SendAsync(optionsRequest);
                    Console.WriteLine($"      Status: {(int)optionsResponse.StatusCode} {optionsResponse.StatusCode}");
                    
                    if (optionsResponse.IsSuccessStatusCode)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("      ? OPTIONS succeeded");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("      ? OPTIONS failed");
                        Console.ResetColor();
                    }

                    if (optionsResponse.Headers.Contains("DAV"))
                    {
                        Console.WriteLine($"      DAV: {string.Join(", ", optionsResponse.Headers.GetValues("DAV"))}");
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"      ? Exception: {ex.Message}");
                    Console.ResetColor();
                }

                Console.WriteLine();
                Console.WriteLine($"[2/3] Testing GET {serverUrl}");
                try
                {
                    var getResponse = await client.GetAsync(serverUrl);
                    Console.WriteLine($"      Status: {(int)getResponse.StatusCode} {getResponse.StatusCode}");
                    
                    if (getResponse.IsSuccessStatusCode || getResponse.StatusCode == System.Net.HttpStatusCode.MultipleChoices)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("      ? GET succeeded");
                        Console.ResetColor();
                    }
                    else if (getResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("      ? Authentication failed (401 Unauthorized)");
                        Console.WriteLine("      ¡ú Check username and password");
                        Console.WriteLine("      ¡ú For Jianguoyun: Use App Password, not login password!");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"      ? Unexpected status: {getResponse.StatusCode}");
                        Console.ResetColor();
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"      ? Exception: {ex.Message}");
                    Console.ResetColor();
                }

                Console.WriteLine();
                Console.WriteLine($"[3/3] Testing PROPFIND {serverUrl}");
                try
                {
                    var propfindRequest = new HttpRequestMessage(new HttpMethod("PROPFIND"), serverUrl);
                    propfindRequest.Headers.Add("Depth", "0");
                    propfindRequest.Content = new StringContent(
                        "<?xml version=\"1.0\"?><D:propfind xmlns:D=\"DAV:\"><D:prop><D:resourcetype/></D:prop></D:propfind>",
                        Encoding.UTF8,
                        "application/xml"
                    );

                    var propfindResponse = await client.SendAsync(propfindRequest);
                    Console.WriteLine($"      Status: {(int)propfindResponse.StatusCode} {propfindResponse.StatusCode}");
                    
                    if (propfindResponse.IsSuccessStatusCode)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("      ? PROPFIND succeeded - WebDAV is working!");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("      ? PROPFIND failed");
                        Console.ResetColor();
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"      ? Exception: {ex.Message}");
                    Console.ResetColor();
                }
            }

            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine("Test completed!");
            Console.WriteLine("========================================");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Fatal error: {ex.Message}");
            Console.WriteLine($"Type: {ex.GetType().Name}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner: {ex.InnerException.Message}");
            }
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    private static string ReadPassword()
    {
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write("*");
            }
        }
        return password.ToString();
    }
}
