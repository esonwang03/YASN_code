using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using YASN.Application;
using YASN.AvaloniaNotes;
using YASN.Core;
using YASN.Infrastructure.Sync;
using YASN.Notifications;
using YASN.PlatformServices;
using YASN.SyncNotifications;

namespace YASN.ViewModels
{
    /// <summary>
    /// Backs the note manager window: lists notes and drives create, open/close, delete, and level
    /// commands against the shared note window manager.
    /// </summary>
    public sealed class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly NoteRepository repository;
        private readonly INoteWindowManager windows;
        private readonly ThreeWaySyncEngine? sync;
        private readonly PlatformServiceBundle? platformServices;
        private readonly SyncNotificationReporter? syncReporter;

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
        /// </summary>
        /// <param name="repository">The note repository.</param>
        /// <param name="windows">The shared note window manager.</param>
        /// <param name="sync">The sync engine, or null when sync is unavailable.</param>
        /// <param name="notifications">Optional service used to toast the result of a manual sync.</param>
        public MainWindowViewModel(NoteRepository repository, INoteWindowManager windows, ThreeWaySyncEngine? sync = null, INotificationService? notifications = null)
        {
            this.repository = repository;
            this.windows = windows;
            this.sync = sync;
            this.syncReporter = notifications is null ? null : new SyncNotificationReporter(notifications);
            if (sync is not null)
            {
                sync.SyncCompleted += HandleSyncCompleted;
            }

            Refresh();
        }

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Gets the notes shown in the list.
        /// </summary>
        public ObservableCollection<NoteListItemViewModel> Notes { get; } = new();

        /// <summary>
        /// Gets whether there are no notes.
        /// </summary>
        public bool IsEmpty => Notes.Count == 0;

        /// <summary>
        /// Reloads the note list from disk and reconciles open state from the window manager.
        /// Conflicted notes are pinned to the top.
        /// </summary>
        public void Refresh()
        {
            HashSet<string> conflicted = sync is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(sync.ConflictedSyncKeys, StringComparer.Ordinal);

            Notes.Clear();
            IEnumerable<AvaloniaNoteDocument> ordered = repository.LoadAll()
                .OrderByDescending(note => conflicted.Contains(note.SyncKey))
                .ThenBy(note => note.Id);

            foreach (AvaloniaNoteDocument note in ordered)
            {
                Notes.Add(new NoteListItemViewModel(note, windows.IsOpen(note.Id), windows.SupportedLevels, ChangeLevel)
                {
                    IsConflicted = conflicted.Contains(note.SyncKey)
                });
            }

            OnPropertyChanged(nameof(IsEmpty));
        }

        /// <summary>
        /// Triggers a manual sync pass, if sync is configured, then toasts the result when complete.
        /// </summary>
        public async void SyncNow()
        {
            if (sync is null)
            {
                return;
            }

            AppLogger.Info("Manual sync triggered by user.");
            SyncResult result = await sync.SyncNowAsync().ConfigureAwait(true);

            if (syncReporter is not null)
            {
                await syncReporter.ReportCompletedAsync(result).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Resolves a conflict for the row's note by declaring it the single source of truth: other
        /// rows sharing its sync key are deleted, the conflict is cleared, and an immediate sync pass
        /// is triggered to force-upload the winner over the remote.
        /// </summary>
        /// <param name="item">The note row that should win.</param>
        /// <param name="errorKey">A localization key describing why resolution failed, if any.</param>
        /// <returns><see langword="true"/> when the conflict was cleared.</returns>
        public bool ResolveConflict(NoteListItemViewModel item, out string? errorKey)
        {
            errorKey = null;
            if (sync is null)
            {
                return false;
            }

            if (!sync.TryResolveConflict(item.Note.SyncKey, item.Note.Id, out errorKey))
            {
                return false;
            }

            Refresh();

            // Force the resolved version onto the remote now rather than waiting for the periodic timer.
            // If a pass is already running it returns "busy"; the queued force-upload marker is durable
            // and the next pass still honors it.
            SyncNow();
            return true;
        }

        private void HandleSyncCompleted(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(Refresh);
        }

        /// <summary>
        /// Creates a note at the given level, persists it, and opens it.
        /// </summary>
        /// <param name="level">The stacking level for the new note.</param>
        public void CreateNote(WindowLevel level)
        {
            AvaloniaNoteDocument note = repository.CreateNote();
            if (level != WindowLevel.Normal)
            {
                note.Level = level;
                repository.Save(note);
            }

            windows.Open(note);
            Refresh();
        }

        /// <summary>
        /// Opens the note if closed, or closes it if open.
        /// </summary>
        /// <param name="item">The note row.</param>
        public void ToggleOpen(NoteListItemViewModel item)
        {
            if (windows.IsOpen(item.Id))
            {
                windows.Close(item.Id);
            }
            else
            {
                windows.Open(item.Note);
            }

            Refresh();
        }

        /// <summary>
        /// Changes a note's level, live if open and persisted regardless.
        /// </summary>
        /// <param name="item">The note row.</param>
        /// <param name="level">The level to apply.</param>
        public void ChangeLevel(NoteListItemViewModel item, WindowLevel level)
        {
            item.Note.Level = level;
            repository.Save(item.Note);
            windows.ApplyLevel(item.Id, level);
            Refresh();
        }

        /// <summary>
        /// Opens the quick-layout overlay for a note's window.
        /// </summary>
        /// <param name="item">The note row.</param>
        public void QuickLayout(NoteListItemViewModel item)
        {
            windows.ShowQuickLayout(item.Note);
            Refresh();
        }

        /// <summary>
        /// Deletes a note: closes its window if open, then removes it from disk.
        /// </summary>
        /// <param name="item">The note row.</param>
        public void Delete(NoteListItemViewModel item)
        {
            windows.Close(item.Id);
            repository.Delete(item.Id);
            Refresh();
        }

        /// <summary>
        /// Attempts to rename a note, validating against other notes' titles.
        /// </summary>
        /// <param name="item">The note row.</param>
        /// <param name="proposed">The proposed new title.</param>
        /// <param name="errorKey">The localization key describing a validation failure, if any.</param>
        /// <returns><see langword="true"/> when the rename succeeded.</returns>
        public bool Rename(NoteListItemViewModel item, string? proposed, out string? errorKey)
        {
            IEnumerable<string> otherTitles = repository.LoadAll()
                .Where(other => other.Id != item.Id)
                .Select(other => other.Title);

            if (!NoteTitleValidator.TryValidate(proposed, otherTitles, out string normalized, out errorKey))
            {
                return false;
            }

            item.Note.StoredTitle = normalized;
            repository.Save(item.Note);
            Refresh();
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
