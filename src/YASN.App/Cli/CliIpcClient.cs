using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using YASN.Infrastructure;

namespace YASN.Cli
{
    /// <summary>
    /// Client side of the CLI inter-process channel. Connects to the running tray instance and sends
    /// a single request line, auto-launching the tray app and waiting for its server to come up when
    /// no instance is running. A named pipe on Windows, a Unix domain socket on macOS/Linux.
    /// </summary>
    public static class CliIpcClient
    {
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);

        /// <summary>
        /// Sends a request to the running instance and returns its response line. When no instance is
        /// running and <paramref name="autoLaunch"/> is set, launches a detached tray process and
        /// retries the connection until its server is ready or the timeout elapses.
        /// </summary>
        /// <param name="requestLine">The protocol request line to send.</param>
        /// <param name="autoLaunch">Whether to start the tray app if none is running.</param>
        /// <returns>The response line, or an <c>ERR</c>-prefixed message on failure.</returns>
        public static async Task<string> SendAsync(string requestLine, bool autoLaunch)
        {
            string? response = await TryConnectAndSendAsync(requestLine).ConfigureAwait(false);
            if (response is not null)
            {
                return response;
            }

            if (!autoLaunch)
            {
                return $"{CliProtocol.ErrorPrefix} YASN is not running.";
            }

            if (!TryLaunchTrayInstance())
            {
                return $"{CliProtocol.ErrorPrefix} Could not start YASN.";
            }

            return await WaitConnectAndSendAsync(requestLine).ConfigureAwait(false);
        }

        private static async Task<string> WaitConnectAndSendAsync(string requestLine)
        {
            DateTime deadline = DateTime.UtcNow + ConnectTimeout;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(RetryInterval).ConfigureAwait(false);
                string? response = await TryConnectAndSendAsync(requestLine).ConfigureAwait(false);
                if (response is not null)
                {
                    return response;
                }
            }

            return $"{CliProtocol.ErrorPrefix} Timed out waiting for YASN to start.";
        }

        /// <summary>
        /// Attempts one connect-and-send cycle. Returns the response line, or null when no server is
        /// reachable (the signal the caller uses to decide whether to auto-launch / keep retrying).
        /// </summary>
        private static async Task<string?> TryConnectAndSendAsync(string requestLine)
        {
            try
            {
                return OperatingSystem.IsWindows()
                    ? await SendOverPipeAsync(requestLine).ConfigureAwait(false)
                    : await SendOverSocketAsync(requestLine).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or SocketException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static async Task<string> SendOverPipeAsync(string requestLine)
        {
            using NamedPipeClientStream client = new NamedPipeClientStream(
                ".", AppPaths.CliPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            // A short connect timeout means a missing server fails fast and falls through to retry.
            await client.ConnectAsync((int)RetryInterval.TotalMilliseconds).ConfigureAwait(false);
            return await ExchangeAsync(client, requestLine).ConfigureAwait(false);
        }

        private static async Task<string> SendOverSocketAsync(string requestLine)
        {
            using Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(AppPaths.CliSocketPath)).ConfigureAwait(false);
            using NetworkStream stream = new NetworkStream(socket, ownsSocket: false);
            return await ExchangeAsync(stream, requestLine).ConfigureAwait(false);
        }

        private static async Task<string> ExchangeAsync(Stream stream, string requestLine)
        {
            using (StreamWriter writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true })
            {
                await writer.WriteLineAsync(requestLine).ConfigureAwait(false);
            }

            using StreamReader reader = new StreamReader(stream, leaveOpen: true);
            string? response = await reader.ReadLineAsync().ConfigureAwait(false);
            return response ?? $"{CliProtocol.ErrorPrefix} No response from YASN.";
        }

        private static bool TryLaunchTrayInstance()
        {
            string? executablePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executablePath))
            {
                return false;
            }

            try
            {
                // Launch with no arguments so the new process takes the GUI/tray path. Detached:
                // no redirected streams, and not tied to this CLI process's lifetime.
                ProcessStartInfo startInfo = new ProcessStartInfo(executablePath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using Process? process = Process.Start(startInfo);
                return process is not null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                AppLogger.Warn($"Could not launch YASN: {ex.Message}");
                return false;
            }
        }
    }
}
