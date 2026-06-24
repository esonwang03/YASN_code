namespace YASN.Cli
{
    /// <summary>
    /// The set of recognized command-line verbs. <see cref="None"/> marks an unparseable input.
    /// </summary>
    public enum CliVerb
    {
        /// <summary>No valid verb was parsed; the caller should print usage.</summary>
        None,

        /// <summary>Show usage text.</summary>
        Help,

        /// <summary>List all notes.</summary>
        NoteList,

        /// <summary>Open (raise) a note window by id.</summary>
        NoteOpen,

        /// <summary>Delete a note by id.</summary>
        NoteDelete,

        /// <summary>Print a note's metadata and a content preview.</summary>
        NoteInfo,

        /// <summary>Move a note's window onto a screen at an explicit rectangle, or raise the overlay.</summary>
        NoteLayout,

        /// <summary>Replace or append a note's Markdown content.</summary>
        NoteEdit,

        /// <summary>Print a note's Markdown content, optionally a line range.</summary>
        NoteGlance,

        /// <summary>List the desktop's screens (monitors) and their bounds.</summary>
        ListScreens,

        /// <summary>Trigger one sync pass.</summary>
        Sync,

        /// <summary>Reveal the data directory in the file manager.</summary>
        OpenData,

        /// <summary>Reveal the cache directory in the file manager.</summary>
        OpenCache,

        /// <summary>Raise the settings window.</summary>
        Settings,

        /// <summary>Raise the main (manage-notes) window.</summary>
        ShowMain
    }

    /// <summary>
    /// A parsed command-line request: a verb plus an optional note identifier, an option bag for
    /// verb-specific flags, and an optional content payload (resolved from stdin or <c>--text</c>
    /// outside <see cref="Parse"/>, which stays pure).
    /// </summary>
    /// <param name="Verb">The recognized verb.</param>
    /// <param name="NoteId">The note id for note-scoped verbs, otherwise null.</param>
    /// <param name="Error">A parse error message when <see cref="Verb"/> is <see cref="CliVerb.None"/>.</param>
    /// <param name="Options">Verb-specific flag values keyed by long-option name (without the <c>--</c>), or null.</param>
    /// <param name="Payload">Resolved content for content verbs (e.g. <c>note edit</c>), or null.</param>
    public sealed record CliCommand(
        CliVerb Verb,
        string? NoteId,
        string? Error,
        IReadOnlyDictionary<string, string>? Options = null,
        string? Payload = null)
    {
        /// <summary>The usage text printed for help and on parse errors.</summary>
        public const string Usage =
            "Usage: yasn [command]\n" +
            "\n" +
            "  (no command)              Start the YASN tray application.\n" +
            "\n" +
            "  note list                 List all notes.\n" +
            "  note open --note-id <id>  Open (raise) a note window.\n" +
            "  note del  --note-id <id>  Delete a note.\n" +
            "  note info --note-id <id>  Show a note's metadata, line/word/char counts, and a preview.\n" +
            "  note glance --note-id <id> [--lines <a-b>]\n" +
            "                            Print a note's Markdown (optionally a 1-based line range).\n" +
            "  note edit --note-id <id> [--append] [--text <s>]\n" +
            "                            Replace (default) or append Markdown; reads stdin unless --text.\n" +
            "  note layout --note-id <id> [--screen <i>] [--lt <x>,<y> --rb <x>,<y>]\n" +
            "                            Move a note onto screen <i> at a physical-pixel rectangle;\n" +
            "                            with no rectangle, raise the quick-layout overlay.\n" +
            "\n" +
            "  list screens              List the desktop's screens and their bounds.\n" +
            "  sync                      Trigger one sync pass.\n" +
            "  settings                  Open the settings window.\n" +
            "  show                      Open the manage-notes window.\n" +
            "  open-data                 Reveal the data directory in the file manager.\n" +
            "  open-cache                Reveal the cache directory in the file manager.\n" +
            "\n" +
            "  help, --help, -h          Show this help.";

        /// <summary>
        /// Parses raw process arguments into a <see cref="CliCommand"/>. Never throws; an
        /// unrecognized or incomplete input yields a <see cref="CliVerb.None"/> command whose
        /// <see cref="Error"/> describes the problem.
        /// </summary>
        /// <param name="args">The process arguments (excluding the executable name).</param>
        /// <returns>The parsed command.</returns>
        public static CliCommand Parse(string[] args)
        {
            if (args.Length == 0)
            {
                return new CliCommand(CliVerb.None, null, "No command given.");
            }

            switch (args[0])
            {
                case "help":
                case "--help":
                case "-h":
                    return new CliCommand(CliVerb.Help, null, null);
                case "sync":
                    return new CliCommand(CliVerb.Sync, null, null);
                case "settings":
                    return new CliCommand(CliVerb.Settings, null, null);
                case "show":
                    return new CliCommand(CliVerb.ShowMain, null, null);
                case "open-data":
                    return new CliCommand(CliVerb.OpenData, null, null);
                case "open-cache":
                    return new CliCommand(CliVerb.OpenCache, null, null);
                case "note":
                    return ParseNote(args);
                case "list":
                    return ParseList(args);
                default:
                    return new CliCommand(CliVerb.None, null, $"Unknown command '{args[0]}'.");
            }
        }

        private static CliCommand ParseList(string[] args)
        {
            if (args.Length < 2 || args[1] != "screens")
            {
                return new CliCommand(CliVerb.None, null, "Unknown 'list' subcommand (screens).");
            }

            return new CliCommand(CliVerb.ListScreens, null, null);
        }

        private static CliCommand ParseNote(string[] args)
        {
            if (args.Length < 2)
            {
                return new CliCommand(CliVerb.None, null, "Missing 'note' subcommand (list, open, del, info, edit, glance, layout).");
            }

            string subcommand = args[1];
            if (subcommand == "list")
            {
                return new CliCommand(CliVerb.NoteList, null, null);
            }

            CliVerb verb = subcommand switch
            {
                "open" => CliVerb.NoteOpen,
                "del" => CliVerb.NoteDelete,
                "info" => CliVerb.NoteInfo,
                "edit" => CliVerb.NoteEdit,
                "glance" => CliVerb.NoteGlance,
                "layout" => CliVerb.NoteLayout,
                _ => CliVerb.None
            };

            if (verb == CliVerb.None)
            {
                return new CliCommand(CliVerb.None, null, $"Unknown 'note' subcommand '{subcommand}'.");
            }

            Dictionary<string, string> options = ReadOptions(args, 2);
            string? noteId = Option(options, "note-id");
            if (string.IsNullOrWhiteSpace(noteId))
            {
                return new CliCommand(CliVerb.None, null, $"'note {subcommand}' requires --note-id <id>.");
            }

            return new CliCommand(verb, noteId, null, options);
        }

        /// <summary>
        /// Reads long options from <paramref name="args"/> starting at <paramref name="startIndex"/>.
        /// A <c>--key value</c> pair stores <c>value</c>; a bare <c>--flag</c> (followed by another
        /// option or end of input) stores <c>"true"</c>. Keys are stored without the leading <c>--</c>.
        /// </summary>
        /// <param name="args">The process arguments.</param>
        /// <param name="startIndex">The index to begin scanning from.</param>
        /// <returns>The collected options keyed by long-option name.</returns>
        private static Dictionary<string, string> ReadOptions(string[] args, int startIndex)
        {
            Dictionary<string, string> options = new(StringComparer.Ordinal);
            for (int i = startIndex; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                string key = args[i][2..];
                bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                options[key] = hasValue ? args[++i] : "true";
            }

            return options;
        }

        private static string? Option(IReadOnlyDictionary<string, string> options, string key)
        {
            return options.TryGetValue(key, out string? value) ? value : null;
        }
    }
}
