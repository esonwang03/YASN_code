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
        /// Marks a response message whose body is Base64-encoded UTF-8, used so multi-line output
        /// (e.g. <c>list screens</c>) survives the single-line wire protocol. Plain messages omit it.
        /// </summary>
        public const string PayloadPrefix = "b64:";

        /// <summary>Encodes arbitrary text as a single space- and newline-free Base64 token.</summary>
        /// <param name="text">The text to encode.</param>
        /// <returns>The Base64 representation of the UTF-8 bytes.</returns>
        public static string EncodePayload(string text)
        {
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty));
        }

        /// <summary>Decodes a Base64 token produced by <see cref="EncodePayload"/> back to text.</summary>
        /// <param name="token">The Base64 token.</param>
        /// <returns>The decoded UTF-8 text.</returns>
        public static string DecodePayload(string token)
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        }

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
                CliVerb.NoteEdit => BuildEditRequest(command),
                CliVerb.NoteLayout => BuildLayoutRequest(command),
                CliVerb.ListScreens => "list-screens",
                CliVerb.Sync => "sync",
                CliVerb.Settings => "settings",
                CliVerb.ShowMain => "show",
                _ => null
            };
        }

        // note-edit <id> <append|replace> <base64Content>. The content is Base64 so it carries
        // newlines and spaces across the single request line and survives the server's space split.
        private static string BuildEditRequest(CliCommand command)
        {
            bool append = command.Options is { } o && o.ContainsKey("append");
            string mode = append ? "append" : "replace";
            return $"note-edit {command.NoteId} {mode} {EncodePayload(command.Payload ?? string.Empty)}";
        }

        // note-layout <id> <screen> <ltx> <lty> <rbx> <rby>. Coordinate tokens are "-" when no
        // rectangle was given, signalling the server to raise the quick-layout overlay instead.
        private static string BuildLayoutRequest(CliCommand command)
        {
            IReadOnlyDictionary<string, string> options =
                command.Options ?? new Dictionary<string, string>(StringComparer.Ordinal);
            string screen = options.TryGetValue("screen", out string? s) ? s : "0";

            if (!options.TryGetValue("lt", out string? lt) || !options.TryGetValue("rb", out string? rb))
            {
                return $"note-layout {command.NoteId} {screen} - - - -";
            }

            (string ltx, string lty) = SplitPair(lt);
            (string rbx, string rby) = SplitPair(rb);
            return $"note-layout {command.NoteId} {screen} {ltx} {lty} {rbx} {rby}";
        }

        private static (string, string) SplitPair(string pair)
        {
            int comma = pair.IndexOf(',', StringComparison.Ordinal);
            return comma < 0 ? (pair, pair) : (pair[..comma], pair[(comma + 1)..]);
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
                    "note-edit" => HandleEdit(arg, router),
                    "note-layout" => HandleLayout(arg, router),
                    "list-screens" => PayloadPrefix + EncodePayload(router.ListScreens()),
                    "sync" => await router.SyncNowAsync().ConfigureAwait(true),
                    "settings" => router.ShowSettings(),
                    "show" => router.ShowMain(),
                    _ => throw new InvalidOperationException($"Unknown request '{verb}'.")
                };

                return $"{OkPrefix} {message}";
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException or ArgumentException)
            {
                AppLogger.Warn($"CLI request '{verb}' failed: {ex.Message}");
                return $"{ErrorPrefix} {ex.Message}";
            }
        }

        // Parses "note-edit" args: <id> <append|replace> <base64Content>.
        private static string HandleEdit(string arg, CliCommandRouter router)
        {
            string[] parts = arg.Split(' ', 3, StringSplitOptions.None);
            if (parts.Length < 3)
            {
                throw new InvalidOperationException("Malformed note-edit request.");
            }

            bool append = parts[1] == "append";
            string content = DecodePayload(parts[2]);
            return router.EditNote(parts[0], append, content);
        }

        // Parses "note-layout" args: <id> <screen> <ltx> <lty> <rbx> <rby>, where each coordinate is
        // "-" to signal the overlay path.
        private static string HandleLayout(string arg, CliCommandRouter router)
        {
            string[] parts = arg.Split(' ');
            if (parts.Length < 6)
            {
                throw new InvalidOperationException("Malformed note-layout request.");
            }

            string id = parts[0];
            int screen = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            if (parts[2] == "-")
            {
                return router.LayoutNote(id, screen, null);
            }

            CliLayoutCoords coords = new(
                Parse(parts[2]), Parse(parts[3]), Parse(parts[4]), Parse(parts[5]));
            return router.LayoutNote(id, screen, coords);
        }

        private static double Parse(string token)
        {
            return double.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
