using YASN.Application;
using YASN.AvaloniaNotes;
using YASN.Core;
using YASN.Infrastructure.Sync;
using YASN.ViewModels;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the note manager view model's list, create, delete, and level logic against a
    /// temp-directory repository and a fake window manager (no real windows).
    /// </summary>
    public sealed class MainWindowViewModelTests : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(), "yasn-main-window-tests", Guid.NewGuid().ToString("N"));

        /// <summary>
        /// Removes the temporary repository directory.
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>
        /// Refresh lists every persisted note and reports emptiness.
        /// </summary>
        [Fact]
        public void RefreshPopulatesNotesAndIsEmpty()
        {
            NoteRepository repository = new NoteRepository(root);
            FakeNoteWindowManager windows = new FakeNoteWindowManager();
            MainWindowViewModel viewModel = new MainWindowViewModel(repository, windows);

            Assert.True(viewModel.IsEmpty);

            repository.CreateNote();
            repository.CreateNote();
            viewModel.Refresh();

            Assert.False(viewModel.IsEmpty);
            Assert.Equal(2, viewModel.Notes.Count);
        }

        /// <summary>
        /// Creating a note persists the chosen level and opens it.
        /// </summary>
        [Fact]
        public void CreateNotePersistsLevelAndOpens()
        {
            NoteRepository repository = new NoteRepository(root);
            FakeNoteWindowManager windows = new FakeNoteWindowManager();
            MainWindowViewModel viewModel = new MainWindowViewModel(repository, windows);

            viewModel.CreateNote(WindowLevel.TopMost);

            AvaloniaNoteDocument saved = repository.LoadAll().Single();
            Assert.Equal(WindowLevel.TopMost, saved.Level);
            Assert.Contains(saved.Id, windows.Opened);
            Assert.Single(viewModel.Notes);
        }

        /// <summary>
        /// Deleting a note closes its window and removes it from disk and the list.
        /// </summary>
        [Fact]
        public void DeleteRemovesNoteAndClosesWindow()
        {
            NoteRepository repository = new NoteRepository(root);
            FakeNoteWindowManager windows = new FakeNoteWindowManager();
            MainWindowViewModel viewModel = new MainWindowViewModel(repository, windows);
            viewModel.CreateNote(WindowLevel.Normal);
            NoteListItemViewModel row = viewModel.Notes.Single();

            viewModel.Delete(row);

            Assert.Empty(repository.LoadAll());
            Assert.Empty(viewModel.Notes);
            Assert.Contains(row.Id, windows.Closed);
        }

        /// <summary>
        /// Changing level persists and applies it to the open window.
        /// </summary>
        [Fact]
        public void ChangeLevelPersistsAndApplies()
        {
            NoteRepository repository = new NoteRepository(root);
            FakeNoteWindowManager windows = new FakeNoteWindowManager();
            MainWindowViewModel viewModel = new MainWindowViewModel(repository, windows);
            viewModel.CreateNote(WindowLevel.Normal);
            NoteListItemViewModel row = viewModel.Notes.Single();

            viewModel.ChangeLevel(row, WindowLevel.TopMost);

            Assert.Equal(WindowLevel.TopMost, repository.LoadAll().Single().Level);
            Assert.Equal(WindowLevel.TopMost, windows.LastAppliedLevel);
        }

        /// <summary>
        /// Renaming persists the stored title and refreshes the list.
        /// </summary>
        [Fact]
        public void RenamePersistsStoredTitle()
        {
            NoteRepository repository = new NoteRepository(root);
            FakeNoteWindowManager windows = new FakeNoteWindowManager();
            MainWindowViewModel viewModel = new MainWindowViewModel(repository, windows);
            viewModel.CreateNote(WindowLevel.Normal);
            NoteListItemViewModel row = viewModel.Notes.Single();

            bool ok = viewModel.Rename(row, "  Groceries  ", out string? errorKey);

            Assert.True(ok);
            Assert.Null(errorKey);
            Assert.Equal("Groceries", repository.LoadAll().Single().StoredTitle);
            Assert.Equal("Groceries", viewModel.Notes.Single().Title);
        }

        /// <summary>
        /// Renaming to an existing title (case-insensitive) is rejected.
        /// </summary>
        [Fact]
        public void RenameRejectsDuplicateTitle()
        {
            NoteRepository repository = new NoteRepository(root);
            FakeNoteWindowManager windows = new FakeNoteWindowManager();
            MainWindowViewModel viewModel = new MainWindowViewModel(repository, windows);
            viewModel.CreateNote(WindowLevel.Normal);
            viewModel.CreateNote(WindowLevel.Normal);
            NoteListItemViewModel first = viewModel.Notes[0];
            NoteListItemViewModel second = viewModel.Notes[1];
            viewModel.Rename(first, "Unique", out _);

            bool ok = viewModel.Rename(second, "unique", out string? errorKey);

            Assert.False(ok);
            Assert.Equal(NoteTitleValidator.DuplicateErrorKey, errorKey);
        }

        /// <summary>
        /// A conflicted note is pinned to the top of the list and flagged.
        /// </summary>
        [Fact]
        public async Task ConflictedNoteIsPinnedAndFlagged()
        {
            using SyncFixture fixture = new SyncFixture(root);
            fixture.SaveNote("k1", "a", "1");
            AvaloniaNoteDocument conflicted = fixture.SaveNote("k2", "b", "2");
            await fixture.Engine.SyncNowAsync();

            conflicted.Content = "local change";
            fixture.Repository.Save(conflicted);
            fixture.SeedRemote("k2", "remote change");
            await fixture.Engine.SyncNowAsync();

            MainWindowViewModel viewModel = new MainWindowViewModel(fixture.Repository, new FakeNoteWindowManager(), fixture.Engine);

            Assert.True(viewModel.Notes[0].IsConflicted);
            Assert.Equal("k2", viewModel.Notes[0].Note.SyncKey);
        }

        /// <summary>
        /// Resolving is rejected until duplicate copies are deleted, then succeeds.
        /// </summary>
        [Fact]
        public async Task ResolveConflictRequiresSingleCopy()
        {
            using SyncFixture fixture = new SyncFixture(root);
            AvaloniaNoteDocument note = fixture.SaveNote("k1", "v1", "1");
            await fixture.Engine.SyncNowAsync();
            note.Content = "local v2";
            fixture.Repository.Save(note);
            fixture.SeedRemote("k1", "remote v2");
            await fixture.Engine.SyncNowAsync();

            MainWindowViewModel viewModel = new MainWindowViewModel(fixture.Repository, new FakeNoteWindowManager(), fixture.Engine);
            NoteListItemViewModel conflicted = viewModel.Notes.First(n => n.IsConflicted);

            Assert.False(viewModel.ResolveConflict(conflicted, out string? errorKey));
            Assert.Equal("Sync.Resolve.Duplicates", errorKey);

            string copyId = fixture.Repository.LoadAll().Where(n => n.SyncKey == "k1").Max(n => n.Id)!;
            fixture.Repository.Delete(copyId);

            Assert.True(viewModel.ResolveConflict(conflicted, out _));
            Assert.Empty(fixture.Engine.ConflictedSyncKeys);
        }

        private sealed class SyncFixture : IDisposable
        {
            private readonly FakeSyncClient client = new();

            public SyncFixture(string root)
            {
                Repository = new NoteRepository(root);
                State = new SyncStateStore(Path.Combine(root, "sync-vm.db"));
                Engine = new ThreeWaySyncEngine(Repository, State);
                Engine.Reconfigure(() => client, string.Empty, TimeSpan.Zero);
            }

            public NoteRepository Repository { get; }

            public SyncStateStore State { get; }

            public ThreeWaySyncEngine Engine { get; }

            public AvaloniaNoteDocument SaveNote(string key, string content, string id)
            {
                AvaloniaNoteDocument note = new AvaloniaNoteDocument { Id = id, SyncKey = key, Content = content };
                Repository.Save(note);
                return note;
            }

            public void SeedRemote(string key, string content)
            {
                SyncNoteDocument wire = new SyncNoteDocument { SyncKey = key, Content = content };
                client.SeedRemote($"notes/{key}.json", NoteWireSerializer.Serialize(wire));
            }

            public void Dispose()
            {
                Engine.Dispose();
                State.Dispose();
            }
        }

        private sealed class FakeNoteWindowManager : INoteWindowManager
        {
            public List<string> Opened { get; } = new();

            public List<string> Closed { get; } = new();

            public WindowLevel? LastAppliedLevel { get; private set; }

            private readonly HashSet<string> open = new(StringComparer.Ordinal);

            public event EventHandler? NotesChanged;

            public bool IsOpen(string noteId) => open.Contains(noteId);

            public void Open(AvaloniaNoteDocument note)
            {
                Opened.Add(note.Id);
                open.Add(note.Id);
                NotesChanged?.Invoke(this, EventArgs.Empty);
            }

            public void Close(string noteId)
            {
                Closed.Add(noteId);
                open.Remove(noteId);
                NotesChanged?.Invoke(this, EventArgs.Empty);
            }

            public void ApplyLevel(string noteId, WindowLevel level)
            {
                LastAppliedLevel = level;
            }

            public void ShowQuickLayout(AvaloniaNoteDocument note)
            {
                Open(note);
            }

            public void ActivateForReminder(AvaloniaNoteDocument note, int? sourceOffset)
            {
                Open(note);
            }

            public void RefreshTaskbarVisibilityForAll()
            {
            }

            public void SetOpenMainWindowAction(Action? action)
            {
            }

            public bool TryApplyExternalContent(string noteId, string content) => false;
        }
    }
}
