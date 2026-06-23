using System.IO.Pipes;
using System.Net.Sockets;
using Avalonia.Threading;
using YASN.Infrastructure;

namespace YASN.Cli
{
    /// <summary>
    /// Hosts the CLI inter-process channel inside the primary (tray) instance: a named pipe on
    /// Windows, a Unix domain socket on macOS/Linux. Accepts one connection at a time on a
    /// background loop, reads a single request line, executes it on the UI thread via
    /// <see cref="CliProtocol"/>, and writes back one response line. Owned by the application and
    /// disposed at shutdown.
    /// </summary>
    public sealed class CliIpcServer : IDisposable
    {
        private readonly CliCommandRouter router;
        private readonly CancellationTokenSource cancellation = new();
        private Socket? unixListener;
        private Task? loop;

        /// <summary>
        /// Initializes the server over the live command router.
        /// </summary>
        /// <param name="router">The router that executes incoming requests.</param>
        public CliIpcServer(CliCommandRouter router)
        {
            this.router = router;
        }

        /// <summary>
        /// Starts the accept loop on a background task. Returns immediately.
        /// </summary>
        public void Start()
        {
            loop = OperatingSystem.IsWindows()
                ? Task.Run(() => RunNamedPipeLoopAsync(cancellation.Token))
                : Task.Run(() => RunUnixSocketLoopAsync(cancellation.Token));
        }

        private async Task RunNamedPipeLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using NamedPipeServerStream server = new NamedPipeServerStream(
                        AppPaths.CliPipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    await ServeAsync(server, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException ex)
                {
                    AppLogger.Debug($"CLI pipe connection dropped: {ex.Message}");
                }
            }
        }

        private async Task RunUnixSocketLoopAsync(CancellationToken token)
        {
            string socketPath = AppPaths.CliSocketPath;
            DeleteSocketFile(socketPath);

            unixListener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            unixListener.Bind(new UnixDomainSocketEndPoint(socketPath));
            unixListener.Listen(backlog: 4);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using Socket connection = await unixListener.AcceptAsync(token).ConfigureAwait(false);
                    using NetworkStream stream = new NetworkStream(connection, ownsSocket: false);
                    await ServeAsync(stream, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or SocketException)
                {
                    AppLogger.Debug($"CLI socket connection dropped: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Reads one request line, dispatches it onto the UI thread, and writes the response line.
        /// </summary>
        private async Task ServeAsync(Stream stream, CancellationToken token)
        {
            using StreamReader reader = new StreamReader(stream, leaveOpen: true);
            using StreamWriter writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };

            string? request = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(request))
            {
                return;
            }

            // Marshal the handler onto the UI thread (window/index work must run there). The
            // InvokeAsync overload taking a Func<Task<string>> returns a flattened Task<string>.
            string response = await Dispatcher.UIThread
                .InvokeAsync(() => CliProtocol.HandleAsync(request, router))
                .ConfigureAwait(false);

            await writer.WriteLineAsync(response.AsMemory(), token).ConfigureAwait(false);
        }

        private static void DeleteSocketFile(string socketPath)
        {
            try
            {
                if (File.Exists(socketPath))
                {
                    File.Delete(socketPath);
                }
            }
            catch (IOException ex)
            {
                AppLogger.Debug($"Could not remove stale CLI socket '{socketPath}': {ex.Message}");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            cancellation.Cancel();
            unixListener?.Dispose();
            if (!OperatingSystem.IsWindows())
            {
                DeleteSocketFile(AppPaths.CliSocketPath);
            }

            cancellation.Dispose();
        }
    }
}
