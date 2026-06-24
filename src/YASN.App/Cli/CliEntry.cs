using YASN.AvaloniaNotes;
using YASN.Infrastructure;

namespace YASN.Cli
{
    /// <summary>
    /// Entry point for the command-line path. Parses arguments, then either serves read-only and
    /// folder-reveal verbs directly (no running instance required) or routes state/UI-affecting
    /// verbs to the running tray instance over IPC, auto-launching it when needed. Returns a process
    /// exit code: 0 success, 1 operational failure, 2 usage error.
    /// </summary>
    public static class CliEntry
    {
        /// <summary>
        /// Runs the CLI for the given process arguments and returns the process exit code.
        /// </summary>
        /// <param name="args">The process arguments (excluding the executable name).</param>
        /// <returns>0 on success, 1 on operational failure, 2 on a usage error.</returns>
        public static int Run(string[] args)
        {
            ConsoleInterop.AttachToParentConsole();
            TrySetUtf8Output();

            CliCommand command = CliCommand.Parse(args);
            int exitCode = Dispatch(command);

            // The GUI-subsystem shell has already returned to its prompt; a trailing newline keeps
            // our last line from being visually glued to the next prompt on Windows.
            Console.Out.Flush();
            return exitCode;
        }

        private static void TrySetUtf8Output()
        {
            // Note content is arbitrary Unicode; force UTF-8 so previews and titles render correctly
            // regardless of the console's active code page. Best-effort: a redirected or closed
            // stream can reject the change, which is harmless.
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch (IOException)
            {
            }
        }

        private static int Dispatch(CliCommand command)
        {
            switch (command.Verb)
            {
                case CliVerb.None:
                    Console.Error.WriteLine(command.Error);
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(CliCommand.Usage);
                    return 2;
                case CliVerb.Help:
                    Console.WriteLine(CliCommand.Usage);
                    return 0;
                case CliVerb.NoteList:
                    return ListNotes();
                case CliVerb.NoteInfo:
                    return ShowNoteInfo(command.NoteId!);
                case CliVerb.NoteGlance:
                    return GlanceNote(command);
                case CliVerb.NoteEdit:
                    return EditNote(command);
                case CliVerb.OpenData:
                    return ShellFolderOpener.Open(AppPaths.DataDirectory) ? 0 : 1;
                case CliVerb.OpenCache:
                    return ShellFolderOpener.Open(AppPaths.CacheRoot) ? 0 : 1;
                case CliVerb.NoteDelete:
                    return DeleteNote(command);
                default:
                    return RouteToInstance(command, autoLaunch: true);
            }
        }

        private static int ListNotes()
        {
            IReadOnlyList<AvaloniaNoteDocument> notes = new NoteRepository().LoadAll();
            if (notes.Count == 0)
            {
                Console.WriteLine("No notes.");
                return 0;
            }

            foreach (AvaloniaNoteDocument note in notes)
            {
                string openMark = note.IsOpen ? "*" : " ";
                Console.WriteLine($"{openMark} {note.Id}  {note.Title}");
            }

            return 0;
        }

        private static int ShowNoteInfo(string noteId)
        {
            AvaloniaNoteDocument? note = new NoteRepository().LoadAll().FirstOrDefault(n => n.Id == noteId);
            if (note is null)
            {
                Console.Error.WriteLine($"No note with id '{noteId}'.");
                return 1;
            }

            Console.WriteLine($"Id:        {note.Id}");
            Console.WriteLine($"Title:     {note.Title}");
            Console.WriteLine($"Open:      {note.IsOpen}");
            Console.WriteLine($"Level:     {note.Level}");
            Console.WriteLine($"Modified:  {Format(note.ContentModifiedAt)}");
            Console.WriteLine($"Reminder:  {Format(note.ReminderAt)}");
            Console.WriteLine($"Bounds:    {note.Left},{note.Top} {note.Width}x{note.Height}");
            Console.WriteLine($"Display:   {note.DisplayMode}");

            // Counts read from disk; an open note's last few in-flight keystrokes may not yet be saved.
            string[] lines = CliText.SplitLines(note.Content);
            int words = note.Content.Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            Console.WriteLine($"Lines:     {lines.Length}");
            Console.WriteLine($"Words:     {words}");
            Console.WriteLine($"Chars:     {note.Content.Length}");
            Console.WriteLine("Preview:");
            Console.WriteLine(Preview(note.Content));
            return 0;
        }

        /// <summary>
        /// Prints a note's Markdown to stdout, optionally restricted to a 1-based inclusive line range
        /// (<c>--lines a-b</c>). Reads from disk so it works whether or not an instance is running.
        /// Output is raw (no line numbers) so it pipes cleanly.
        /// </summary>
        private static int GlanceNote(CliCommand command)
        {
            AvaloniaNoteDocument? note = new NoteRepository().LoadAll().FirstOrDefault(n => n.Id == command.NoteId);
            if (note is null)
            {
                Console.Error.WriteLine($"No note with id '{command.NoteId}'.");
                return 1;
            }

            string? rangeText = command.Options is { } o && o.TryGetValue("lines", out string? r) ? r : null;
            if (rangeText is null)
            {
                Console.WriteLine(note.Content);
                return 0;
            }

            if (!CliText.TryParseLineRange(rangeText, out (int Start, int End) range))
            {
                Console.Error.WriteLine($"Invalid line range '{rangeText}'. Use <start>-<end> (1-based).");
                return 2;
            }

            string[] lines = CliText.SplitLines(note.Content);
            int start = Math.Min(range.Start, lines.Length);
            int end = Math.Min(range.End, lines.Length);
            for (int i = start; i <= end; i++)
            {
                Console.WriteLine(lines[i - 1]);
            }

            return 0;
        }

        /// <summary>
        /// Replaces or appends a note's Markdown. The content comes from <c>--text</c> when given,
        /// otherwise from stdin. Routed to a running instance so the edit composes with an open editor;
        /// when no instance answers (hence no open windows) it falls back to a direct repository write,
        /// which cannot clobber a live autosave.
        /// </summary>
        private static int EditNote(CliCommand command)
        {
            bool append = command.Options is { } o && o.ContainsKey("append");
            string content = command.Options is { } opts && opts.TryGetValue("text", out string? text)
                ? text
                : Console.In.ReadToEnd();

            CliCommand resolved = command with { Payload = content };
            string? requestLine = CliProtocol.ToRequestLine(resolved);
            string response = CliIpcClient.SendAsync(requestLine!, autoLaunch: false).GetAwaiter().GetResult();
            if (response.StartsWith(CliProtocol.OkPrefix, StringComparison.Ordinal))
            {
                return Report(response);
            }

            // No running instance: edit on disk directly. With the app down there are no open windows,
            // so no live autosave can overwrite this write.
            NoteRepository repository = new();
            AvaloniaNoteDocument? note = repository.LoadAll().FirstOrDefault(n => n.Id == command.NoteId);
            if (note is null)
            {
                Console.Error.WriteLine($"No note with id '{command.NoteId}'.");
                return 1;
            }

            note.Content = append ? CliText.AppendContent(note.Content, content) : content;
            repository.Save(note);
            Console.WriteLine($"{(append ? "Appended to" : "Replaced")} note '{command.NoteId}'.");
            return 0;
        }

        /// <summary>
        /// Deletes a note. When an instance is running the delete is routed over IPC so its in-memory
        /// index is not overwritten on its next save; otherwise the repository is mutated directly.
        /// </summary>
        private static int DeleteNote(CliCommand command)
        {
            string? requestLine = CliProtocol.ToRequestLine(command);
            string response = CliIpcClient.SendAsync(requestLine!, autoLaunch: false).GetAwaiter().GetResult();
            if (response.StartsWith(CliProtocol.OkPrefix, StringComparison.Ordinal))
            {
                return Report(response);
            }

            // No running instance: delete directly. The on-disk index is authoritative when the app
            // is down, so this cannot clobber live state.
            if (!new NoteRepository().LoadAll().Any(n => n.Id == command.NoteId))
            {
                Console.Error.WriteLine($"No note with id '{command.NoteId}'.");
                return 1;
            }

            new NoteRepository().Delete(command.NoteId!);
            Console.WriteLine($"Deleted note '{command.NoteId}'.");
            return 0;
        }

        private static int RouteToInstance(CliCommand command, bool autoLaunch)
        {
            string? requestLine = CliProtocol.ToRequestLine(command);
            if (requestLine is null)
            {
                Console.Error.WriteLine("Command cannot be routed.");
                return 1;
            }

            string response = CliIpcClient.SendAsync(requestLine, autoLaunch).GetAwaiter().GetResult();
            return Report(response);
        }

        private static int Report(string response)
        {
            bool ok = response.StartsWith(CliProtocol.OkPrefix, StringComparison.Ordinal);
            string message = StripPrefix(response);
            if (message.StartsWith(CliProtocol.PayloadPrefix, StringComparison.Ordinal))
            {
                message = CliProtocol.DecodePayload(message[CliProtocol.PayloadPrefix.Length..]);
            }

            if (ok)
            {
                Console.WriteLine(message);
                return 0;
            }

            Console.Error.WriteLine(message);
            return 1;
        }

        private static string StripPrefix(string response)
        {
            int space = response.IndexOf(' ', StringComparison.Ordinal);
            return space < 0 ? string.Empty : response[(space + 1)..];
        }

        private static string Format(DateTimeOffset? value)
        {
            return value is { } v ? v.ToString("u") : "(none)";
        }

        private static string Preview(string content)
        {
            const int maxChars = 280;
            string trimmed = content.Trim();
            return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "…";
        }
    }
}
