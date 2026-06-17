using YASN.AvaloniaNotes;
using YASN.Infrastructure;
using YASN.Infrastructure.Settings;

namespace YASN.Application
{
    /// <summary>
    /// Creates the tutorial welcome note from the bundled <c>tutorial.md</c>. Used once on first run
    /// and on demand from the settings window. The first-run pass is guarded by a machine-local flag
    /// so a deleted tutorial note never reappears on the next launch.
    /// </summary>
    public sealed class TutorialNoteSeeder
    {
        /// <summary>Local-settings key recording that the first-run tutorial note was created.</summary>
        public const string SeededSettingKey = "tutorial.seeded";

        private readonly NoteRepository repository;
        private readonly NoteWindowManager noteWindows;
        private readonly SettingsStore settings;

        /// <summary>
        /// Initializes a new instance of the <see cref="TutorialNoteSeeder"/> class.
        /// </summary>
        /// <param name="repository">The note repository the tutorial note is saved to.</param>
        /// <param name="noteWindows">The window manager used to open the created note.</param>
        /// <param name="settings">The settings store holding the first-run flag.</param>
        public TutorialNoteSeeder(NoteRepository repository, NoteWindowManager noteWindows, SettingsStore settings)
        {
            this.repository = repository;
            this.noteWindows = noteWindows;
            this.settings = settings;
        }

        /// <summary>
        /// Creates the tutorial note once, the first time the app starts. Marks the local flag so it is
        /// not recreated on later launches even if the user deletes it. The created note is left for
        /// <see cref="NoteWindowManager.RestoreOpenNotes"/> to open, so callers must seed before restoring.
        /// </summary>
        public void SeedOnFirstRun()
        {
            bool alreadySeeded = bool.TryParse(
                settings.GetValue(SeededSettingKey, shouldSync: false, "false"), out bool seeded) && seeded;
            if (alreadySeeded)
            {
                return;
            }

            settings.SetValue(SeededSettingKey, shouldSync: false, "true");
            CreateNote();
        }
        // @next
        /// <summary>
        /// Creates a fresh tutorial note and opens its window. Invoked by the settings action so the
        /// user can summon the tutorial again at any time; each call adds a new note.
        /// </summary>
        public void CreateAndOpen()
        {
            AvaloniaNoteDocument note = CreateNote();
            noteWindows.Open(note);
        }

        private AvaloniaNoteDocument CreateNote()
        {
            AvaloniaNoteDocument note = repository.CreateNote();
            note.Content = ReadTutorialContent();
            repository.Save(note);
            return note;
        }

        private static string ReadTutorialContent()
        {
            string path = AppPaths.BundledTutorialPath;
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }

            AppLogger.Warn($"Bundled tutorial note not found at {path}. Using a placeholder.");
            return "# Welcome to YASN\n\nThe bundled tutorial could not be found.";
        }
    }
}
