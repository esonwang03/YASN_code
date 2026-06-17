namespace YASN.Migration
{
    /// <summary>
    /// The outcome of a storage migration attempt.
    /// </summary>
    public enum MigrationStatus
    {
        /// <summary>No old or legacy index was found; nothing to do.</summary>
        NothingToMigrate,

        /// <summary>The index was already in the current schema; left untouched.</summary>
        AlreadyCurrent,

        /// <summary>The index was converted from an older schema to the current one.</summary>
        Migrated,

        /// <summary>Migration failed; see <see cref="MigrationReport.Messages"/>.</summary>
        Failed
    }

    /// <summary>
    /// Describes what a migration did, for logging and CLI output.
    /// </summary>
    public sealed class MigrationReport
    {
        /// <summary>Gets or sets the overall status.</summary>
        public MigrationStatus Status { get; set; }

        /// <summary>Gets or sets the number of notes written into the new index.</summary>
        public int NotesMigrated { get; set; }

        /// <summary>Gets or sets the number of markdown files created from inline content.</summary>
        public int MarkdownFilesWritten { get; set; }

        /// <summary>Gets or sets the path of the backup of the original index, if one was taken.</summary>
        public string? BackupPath { get; set; }

        /// <summary>Gets the human-readable log lines describing the run.</summary>
        public List<string> Messages { get; } = new();

        /// <summary>Gets whether the run completed without error.</summary>
        public bool Ok => Status != MigrationStatus.Failed;
    }
}
