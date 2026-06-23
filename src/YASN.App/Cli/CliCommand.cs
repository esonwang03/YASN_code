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
    /// A parsed command-line request: a verb plus an optional note identifier.
    /// </summary>
    /// <param name="Verb">The recognized verb.</param>
    /// <param name="NoteId">The note id for note-scoped verbs, otherwise null.</param>
    /// <param name="Error">A parse error message when <see cref="Verb"/> is <see cref="CliVerb.None"/>.</param>
    public sealed record CliCommand(CliVerb Verb, string? NoteId, string? Error)
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
            "  note info --note-id <id>  Show a note's metadata and a content preview.\n" +
            "\n" +
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
                default:
                    return new CliCommand(CliVerb.None, null, $"Unknown command '{args[0]}'.");
            }
        }

        private static CliCommand ParseNote(string[] args)
        {
            if (args.Length < 2)
            {
                return new CliCommand(CliVerb.None, null, "Missing 'note' subcommand (list, open, del, info).");
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
                _ => CliVerb.None
            };

            if (verb == CliVerb.None)
            {
                return new CliCommand(CliVerb.None, null, $"Unknown 'note' subcommand '{subcommand}'.");
            }

            string? noteId = ReadNoteId(args, 2);
            if (string.IsNullOrWhiteSpace(noteId))
            {
                return new CliCommand(CliVerb.None, null, $"'note {subcommand}' requires --note-id <id>.");
            }

            return new CliCommand(verb, noteId, null);
        }

        private static string? ReadNoteId(string[] args, int startIndex)
        {
            for (int i = startIndex; i < args.Length - 1; i++)
            {
                if (args[i] == "--note-id")
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
