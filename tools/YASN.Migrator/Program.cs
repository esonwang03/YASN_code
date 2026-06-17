using YASN.Migration;

namespace YASN.Migrator
{
    /// <summary>
    /// Command-line front end for <see cref="WpfNoteStorageMigrator"/>. Converts a legacy WPF note
    /// store into the schema the Avalonia build reads. Safe to run repeatedly: a current store is a
    /// no-op.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (HasFlag(args, "--help", "-h"))
            {
                PrintUsage();
                return 0;
            }

            string? dataDir = GetOption(args, "--data-dir") ?? FirstPositional(args);
            bool dryRun = HasFlag(args, "--dry-run");
            bool quiet = HasFlag(args, "--quiet", "-q");

            if (string.IsNullOrWhiteSpace(dataDir))
            {
                Console.Error.WriteLine("error: no data directory given. Pass --data-dir <path> (the folder containing notes.index.json).");
                PrintUsage();
                return 2;
            }

            string resolved = Path.GetFullPath(dataDir);
            if (!quiet)
            {
                Console.WriteLine($"YASN storage migrator — target: {resolved}{(dryRun ? " (dry-run)" : string.Empty)}");
            }

            TextWriter? log = quiet ? null : Console.Out;
            MigrationReport report = WpfNoteStorageMigrator.Migrate(resolved, dryRun, log);

            if (!quiet)
            {
                Console.WriteLine($"Result: {report.Status}; notes={report.NotesMigrated}, markdownWritten={report.MarkdownFilesWritten}.");
            }

            return report.Ok ? 0 : 1;
        }

        private static void PrintUsage()
        {
            Console.WriteLine(
                """
                Usage: yasn-migrate --data-dir <path> [--dry-run] [--quiet]

                Converts a legacy WPF YASN note store (PascalCase notes.index.json / notes.json)
                into the current Avalonia schema. The original index is backed up to
                notes.index.wpf-backup.json before any change. Running against an
                already-current store does nothing.

                Options:
                  --data-dir <path>   Folder containing notes.index.json and the notes/ folder.
                                      (May also be given as the first positional argument.)
                  --dry-run           Report what would change without writing.
                  --quiet, -q         Suppress progress output.
                  --help, -h          Show this help.

                Exit codes: 0 success or nothing to do, 1 migration error, 2 bad arguments.
                """);
        }

        private static bool HasFlag(string[] args, params string[] names) =>
            args.Any(a => names.Contains(a, StringComparer.OrdinalIgnoreCase));

        private static string? GetOption(string[] args, string name)
        {
            int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static string? FirstPositional(string[] args) =>
            args.FirstOrDefault(a => !a.StartsWith('-'));
    }
}
