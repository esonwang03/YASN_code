namespace YASN.Cli
{
    /// <summary>
    /// The line-based wire protocol for the CLI inter-process channel. One request line and one
    /// response line per connection. A request is <c>VERB [arg]</c>; a response starts with
    /// <see cref="OkPrefix"/> or <see cref="ErrorPrefix"/> followed by a human-readable message.
    /// Text rather than JSON so the channel carries no reflection/serialization cost and stays
    /// trivially AOT-safe and debuggable.
    /// </summary>
    public static class CliProtocol
    {
        /// <summary>Response prefix marking a successful command.</summary>
        public const string OkPrefix = "OK";

        /// <summary>Response prefix marking a failed command.</summary>
        public const string ErrorPrefix = "ERR";

        /// <summary>
        /// Builds the request line for an IPC-routed command, or null when the verb is not one that
        /// routes over IPC (read-only and folder-reveal verbs are handled client-side).
        /// </summary>
        /// <param name="command">The parsed command.</param>
        /// <returns>The request line, or null when the command does not route over IPC.</returns>
        public static string? ToRequestLine(CliCommand command)
        {
            return command.Verb switch
            {
                CliVerb.NoteOpen => $"note-open {command.NoteId}",
                CliVerb.NoteDelete => $"note-del {command.NoteId}",
                CliVerb.Sync => "sync",
                CliVerb.Settings => "settings",
                CliVerb.ShowMain => "show",
                _ => null
            };
        }

        /// <summary>
        /// Executes a server-side request line against the router and formats the response line.
        /// Runs on the UI thread (the caller marshals it there). Never throws to the transport.
        /// </summary>
        /// <param name="line">The request line received from a client.</param>
        /// <param name="router">The live command router.</param>
        /// <returns>The response line to send back.</returns>
        public static async Task<string> HandleAsync(string line, CliCommandRouter router)
        {
            string trimmed = line.Trim();
            int space = trimmed.IndexOf(' ', StringComparison.Ordinal);
            string verb = space < 0 ? trimmed : trimmed[..space];
            string arg = space < 0 ? string.Empty : trimmed[(space + 1)..].Trim();

            try
            {
                string message = verb switch
                {
                    "note-open" => router.OpenNote(arg),
                    "note-del" => router.DeleteNote(arg),
                    "sync" => await router.SyncNowAsync().ConfigureAwait(true),
                    "settings" => router.ShowSettings(),
                    "show" => router.ShowMain(),
                    _ => throw new InvalidOperationException($"Unknown request '{verb}'.")
                };

                return $"{OkPrefix} {message}";
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                AppLogger.Warn($"CLI request '{verb}' failed: {ex.Message}");
                return $"{ErrorPrefix} {ex.Message}";
            }
        }
    }
}
