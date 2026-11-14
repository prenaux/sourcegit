using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public sealed class Reviews : ObservableObject, IDisposable
    {
        private readonly string _repository;
        private readonly Models.ReviewStore _store;
        private readonly DispatcherTimer _saveTimer;
        private readonly SemaphoreSlim _interactionLock = new(1);
        private readonly Dictionary<string, Models.Review> _pending = [];
        private readonly Stack<List<(Models.ReviewComment Comment, Models.ReviewCommentState State)>> _undo = new();
        private readonly List<ReviewFileItem> _allFiles = [];
        private Task _lastSave = Task.CompletedTask;
        private Models.Review _review;
        private Models.Review _selectedReview;
        private Models.ReviewSnapshot _snapshot;
        private ReviewFileItem _selectedFile;
        private ReviewCommentItem _selectedComment;
        private ReviewDiffContext _context;
        private TextDiffContext _diff;
        private Models.ReviewComment _reattach;
        private Models.ReviewSourceKind _source = Models.ReviewSourceKind.Staged;
        private string _baseRevision = "HEAD~1";
        private string _targetRevision = "HEAD";
        private string _fileFilter = string.Empty;
        private string _status = string.Empty;
        private string _saveStatus = string.Empty;
        private string _lastSaveError = string.Empty;
        private bool _isBusy;
        private bool _sourceChanged;
        private bool _isSelecting;
        private bool _initialized;
        private bool _isCheckingSource;
        private bool _disposed;
        private Models.ReviewCommentState _bulkState;

        public AvaloniaList<Models.Review> SavedReviews { get; } = [];
        public AvaloniaList<Models.ReviewSnapshot> Snapshots { get; } = [];
        public AvaloniaList<ReviewFileItem> Files { get; } = [];
        public AvaloniaList<ReviewCommentItem> Comments { get; } = [];
        public Models.ReviewSourceKind[] SourceChoices { get; } = Enum.GetValues<Models.ReviewSourceKind>();
        public Models.ReviewCommentState[] StateChoices { get; } = Enum.GetValues<Models.ReviewCommentState>();
        public Models.Review CurrentReview => _review;
        public Models.ReviewSnapshot CurrentSnapshot => _snapshot;
        public bool HasPendingReattach => _reattach != null;
        public bool HasReview => _review != null;
        public bool HasFile => _selectedFile != null;
        public bool CanMarkFileReviewed => _selectedFile != null && !_selectedFile.Data.IsOutsideDiff;
        public bool HasTextDiff => _diff != null;
        public bool IsPreview => _review != null && !SavedReviews.Contains(_review);
        public bool CanUndo => _undo.Count > 0;
        public bool IsRevisionSource => _source is Models.ReviewSourceKind.Commit or Models.ReviewSourceKind.Comparison;
        public bool IsComparison => _source == Models.ReviewSourceKind.Comparison;
        public string ComparisonLabel => _snapshot == null ? string.Empty : $"{_snapshot.BaseRevision} -> {_snapshot.TargetRevision}";

        public Models.Review SelectedReview
        {
            get => _selectedReview;
            set
            {
                if (SetProperty(ref _selectedReview, value) && !_isSelecting && value != null)
                    _ = RunBusyAsync(() => OpenReviewAsync(value));
            }
        }

        public Models.ReviewSnapshot SelectedSnapshot
        {
            get => _snapshot;
            set
            {
                if (value == null || _isSelecting || ReferenceEquals(value, _snapshot))
                    return;
                _ = RunBusyAsync(() => OpenSnapshotAsync(value));
            }
        }

        public ReviewFileItem SelectedFile
        {
            get => _selectedFile;
            set
            {
                if (_isSelecting && value == null)
                    return;
                if (SetProperty(ref _selectedFile, value) && !_isSelecting)
                    BuildFileView();
            }
        }

        public ReviewCommentItem SelectedComment
        {
            get => _selectedComment;
            set
            {
                if (SetProperty(ref _selectedComment, value) && !_isSelecting)
                    _context?.EditComment(value?.Data);
            }
        }

        public ReviewDiffContext Context
        {
            get => _context;
            private set => SetProperty(ref _context, value);
        }

        public TextDiffContext Diff
        {
            get => _diff;
            private set => SetProperty(ref _diff, value);
        }

        public Models.ReviewSourceKind Source
        {
            get => _source;
            set
            {
                if (!SetProperty(ref _source, value))
                    return;
                OnPropertyChanged(nameof(IsRevisionSource));
                OnPropertyChanged(nameof(IsComparison));
                if (!_isSelecting && !IsRevisionSource && _initialized)
                    _ = PreviewAsync();
            }
        }

        public string BaseRevision { get => _baseRevision; set => SetProperty(ref _baseRevision, value); }
        public string TargetRevision { get => _targetRevision; set => SetProperty(ref _targetRevision, value); }
        public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
        public bool SourceChanged { get => _sourceChanged; private set => SetProperty(ref _sourceChanged, value); }
        public string Status { get => _status; private set => SetProperty(ref _status, value); }
        public string SaveStatus { get => _saveStatus; private set => SetProperty(ref _saveStatus, value); }
        public Models.ReviewCommentState BulkState { get => _bulkState; set => SetProperty(ref _bulkState, value); }

        public string Title
        {
            get => _review?.Title ?? string.Empty;
            set
            {
                if (_review == null || _review.Title == value)
                    return;
                _review.Title = value ?? string.Empty;
                OnPropertyChanged();
                QueueSave();
            }
        }

        public string GeneralText
        {
            get => _review?.GeneralText ?? string.Empty;
            set
            {
                if (_review == null || _review.GeneralText == value)
                    return;
                _review.GeneralText = value ?? string.Empty;
                OnPropertyChanged();
                QueueSave();
            }
        }

        public string FileFilter
        {
            get => _fileFilter;
            set
            {
                if (SetProperty(ref _fileFilter, value))
                    FilterFiles();
            }
        }

        public bool IsFileReviewed
        {
            get => _selectedFile?.Data.Progress == Models.ReviewFileProgress.Reviewed;
            set
            {
                if (!CanMarkFileReviewed || IsFileReviewed == value)
                    return;
                _selectedFile.Data.Progress = value ? Models.ReviewFileProgress.Reviewed : Models.ReviewFileProgress.Unreviewed;
                NotifyFileChanged(_selectedFile.Data);
            }
        }

        public bool UseSideBySide
        {
            get => Preferences.Instance.UseSideBySideDiff;
            set
            {
                if (UseSideBySide == value)
                    return;
                Preferences.Instance.UseSideBySideDiff = value;
                OnPropertyChanged();
                RebuildDiff();
            }
        }

        public Reviews(Repository repository) : this(repository.FullPath)
        {
            if (repository.IsBare)
            {
                SourceChoices = [Models.ReviewSourceKind.Commit, Models.ReviewSourceKind.Comparison];
                _source = Models.ReviewSourceKind.Commit;
            }
        }

        public Reviews(string repository)
        {
            _repository = repository;
            _store = new Models.ReviewStore(repository);
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Tick += async (_, _) => await FlushAsync();
        }

        public async Task InitializeAsync()
        {
            if (_initialized)
                return;
            await RunBusyAsync(async () =>
            {
                await LoadSavedReviewsAsync();
                if (SavedReviews.Count > 0)
                    await OpenReviewAsync(SavedReviews[0]);
                if (_store.LoadErrors.Count > 0)
                    Status = string.Join("\n", _store.LoadErrors);
            });
            if (_review == null && !IsRevisionSource)
                await PreviewAsync();
        }

        private async Task LoadSavedReviewsAsync()
        {
            if (_initialized)
                return;
            SavedReviews.AddRange(await Task.Run(_store.LoadReviews));
            _initialized = true;
        }

        public async Task OpenSourceAsync(Models.ReviewSourceKind source, string baseRevision, string targetRevision)
        {
            await RunBusyAsync(async () =>
            {
                await LoadSavedReviewsAsync();
                _isSelecting = true;
                Source = source;
                BaseRevision = baseRevision;
                TargetRevision = targetRevision;
                _isSelecting = false;
            });
            await PreviewAsync();
        }

        public Task PreviewAsync()
        {
            return RunBusyAsync(async () =>
            {
                var snapshot = await Task.Run(() => new Commands.CaptureReview(_repository).CaptureAsync(Source, BaseRevision, TargetRevision));
                if (_disposed)
                    return;
                foreach (var saved in SavedReviews)
                {
                    var match = saved.Snapshots.Find(x => x.Id == snapshot.Id);
                    if (match == null)
                        continue;
                    await Task.Run(() => _store.LoadSnapshot(saved, match));
                    _review = saved;
                    SetSnapshot(match);
                    return;
                }
                OpenPreview(snapshot);
            });
        }

        public void OpenPreview(Models.ReviewSnapshot snapshot)
        {
            _review = new Models.Review
            {
                Title = App.Text("Reviews.DefaultTitle", App.Text("Reviews.Source." + snapshot.Source), DateTime.Now.ToString("yyyy-MM-dd HH:mm")),
                LastSnapshotId = snapshot.Id,
                Snapshots = [snapshot],
            };
            SetSnapshot(snapshot);
            Status = snapshot.Files.Count == 0 ? App.Text("Reviews.NoChanges") : App.Text("Reviews.PreviewHint");
        }

        public Task UpdateSnapshot()
        {
            return RunBusyAsync(async () =>
            {
                if (_snapshot == null || _review == null)
                    return;
                var next = await Task.Run(() => new Commands.CaptureReview(_repository).CaptureAsync(_snapshot.Source, _snapshot.BaseRevision, _snapshot.TargetRevision));
                if (_disposed)
                    return;
                var existing = _review.Snapshots.Find(x => x.Id == next.Id);
                if (existing != null)
                {
                    existing.SourceFingerprint = next.SourceFingerprint;
                    await OpenSnapshotAsync(existing);
                    return;
                }

                var previous = _snapshot;
                Models.ReviewMigration.MigrateComments(previous, next, hash => next.Contents.TryGetValue(hash, out var text) ? text : ReadContent(hash));
                _review.Snapshots.Add(next);
                SetSnapshot(next);
                var attached = 0;
                var attention = 0;
                var outside = 0;
                foreach (var file in next.Files)
                    foreach (var comment in file.Comments)
                    {
                        if (comment.Anchor.Attachment == Models.ReviewAttachment.Attached)
                            attached++;
                        else if (comment.Anchor.Attachment == Models.ReviewAttachment.NeedsAttention)
                            attention++;
                        else
                            outside++;
                    }
                Status = App.Text("Reviews.MigrationResult", attached, attention, outside);
                QueueSave();
            });
        }

        public async Task CheckSourceAsync()
        {
            if (_snapshot == null || IsBusy || _isCheckingSource || _disposed ||
                _snapshot.Source is Models.ReviewSourceKind.Commit or Models.ReviewSourceKind.Comparison)
                return;
            var snapshot = _snapshot;
            _isCheckingSource = true;
            try
            {
                var current = await new Commands.CaptureReview(_repository).ReadSourceFingerprintAsync(snapshot.Source);
                if (ReferenceEquals(snapshot, _snapshot))
                    SourceChanged = current != snapshot.SourceFingerprint;
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(snapshot, _snapshot))
                    Status = exception.Message;
            }
            finally
            {
                _isCheckingSource = false;
            }
        }

        private async Task OpenReviewAsync(Models.Review review)
        {
            var snapshot = review.Snapshots.Find(x => x.Id == review.LastSnapshotId) ??
                (review.Snapshots.Count > 0 ? review.Snapshots[^1] : null);
            if (snapshot == null)
                return;
            await Task.Run(() => _store.LoadSnapshot(review, snapshot));
            if (_disposed)
                return;
            _review = review;
            SetSnapshot(snapshot);
        }

        private async Task OpenSnapshotAsync(Models.ReviewSnapshot snapshot)
        {
            await Task.Run(() => _store.LoadSnapshot(_review, snapshot));
            if (!_disposed)
                SetSnapshot(snapshot);
        }

        private void SetSnapshot(Models.ReviewSnapshot snapshot)
        {
            _isSelecting = true;
            try
            {
                _snapshot = snapshot;
                _selectedReview = SavedReviews.Contains(_review) ? _review : null;
                _source = snapshot.Source;
                _baseRevision = snapshot.BaseRevision;
                _targetRevision = snapshot.TargetRevision;
                Snapshots.Clear();
                Snapshots.AddRange(_review.Snapshots);
                _allFiles.Clear();
                foreach (var file in snapshot.Files)
                    _allFiles.Add(new ReviewFileItem(file));
                _selectedFile = null;
                _selectedComment = null;
                _reattach = null;
                _undo.Clear();
            }
            finally
            {
                _isSelecting = false;
            }
            SourceChanged = false;
            SaveStatus = string.Empty;
            Status = IsPreview ? App.Text("Reviews.PreviewHint") : string.Empty;
            FilterFiles();
            OnPropertyChanged(nameof(SelectedReview));
            OnPropertyChanged(nameof(SelectedSnapshot));
            OnPropertyChanged(nameof(Source));
            OnPropertyChanged(nameof(BaseRevision));
            OnPropertyChanged(nameof(TargetRevision));
            OnPropertyChanged(nameof(IsRevisionSource));
            OnPropertyChanged(nameof(IsComparison));
            OnPropertyChanged(nameof(ComparisonLabel));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(GeneralText));
            OnPropertyChanged(nameof(HasReview));
            OnPropertyChanged(nameof(IsPreview));
            OnPropertyChanged(nameof(CanUndo));
        }

        private void FilterFiles()
        {
            var selected = _selectedFile;
            _isSelecting = true;
            Files.Clear();
            foreach (var file in _allFiles)
                if (string.IsNullOrEmpty(_fileFilter) || file.Path.Contains(_fileFilter, StringComparison.OrdinalIgnoreCase))
                    Files.Add(file);
            _isSelecting = false;
            _selectedFile = null;
            SelectedFile = selected != null && Files.Contains(selected) ? selected : Files.Count > 0 ? Files[0] : null;
            if (SelectedFile == null)
                BuildFileView();
        }

        private void BuildFileView()
        {
            Context = _selectedFile == null ? null : new ReviewDiffContext(this, _selectedFile.Data);
            _selectedComment = null;
            RebuildDiff();
            RefreshComments();
            OnPropertyChanged(nameof(HasFile));
            OnPropertyChanged(nameof(CanMarkFileReviewed));
            OnPropertyChanged(nameof(IsFileReviewed));
        }

        private void RebuildDiff()
        {
            var file = _selectedFile?.Data;
            var parsed = file == null || file.IsBinary ? null : Commands.Diff.ParsePatch(file.Patch).TextDiff;
            if (parsed == null)
            {
                Diff = null;
            }
            else
            {
                var option = new Models.DiffOption(_snapshot.BaseRevision, _snapshot.TargetRevision, new Models.Change { Path = file.Path, OriginalPath = file.OldPath });
                TextDiffContext context = UseSideBySide ? new TwoSideTextDiff(option, parsed) : new CombinedTextDiff(option, parsed);
                context.Review = _context;
                Diff = context;
            }
            OnPropertyChanged(nameof(HasTextDiff));
        }

        public string ReadContent(string hash)
        {
            foreach (var snapshot in _review.Snapshots)
                if (snapshot.Contents.TryGetValue(hash, out var text))
                    return text;
            return _store.ReadContent(hash);
        }

        public void AddComment(Models.ReviewFile file, Models.ReviewComment comment)
        {
            file.Comments.Add(comment);
            NotifyFileChanged(file);
        }

        public void NotifyFileChanged(Models.ReviewFile file)
        {
            file.MarkChanged();
            foreach (var item in _allFiles)
                if (ReferenceEquals(item.Data, file))
                    item.Refresh();
            RefreshComments();
            OnPropertyChanged(nameof(IsFileReviewed));
            QueueSave();
        }

        private void RefreshComments()
        {
            var selected = _selectedComment?.Data.Id;
            _isSelecting = true;
            if (_selectedFile == null || Comments.Count != _selectedFile.Data.Comments.Count ||
                Comments.Any(x => !_selectedFile.Data.Comments.Contains(x.Data)))
            {
                Comments.Clear();
                if (_selectedFile != null)
                    foreach (var comment in _selectedFile.Data.Comments)
                        Comments.Add(new ReviewCommentItem(comment));
            }
            foreach (var item in Comments)
                item.Refresh();
            _selectedComment = Comments.FirstOrDefault(x => x.Data.Id == selected);
            _isSelecting = false;
            OnPropertyChanged(nameof(SelectedComment));
        }

        public void AddFileComment() => _context?.BeginFileComment();

        public void SelectComment(Models.ReviewComment comment)
        {
            _isSelecting = true;
            _selectedComment = Comments.FirstOrDefault(x => ReferenceEquals(x.Data, comment));
            OnPropertyChanged(nameof(SelectedComment));
            _isSelecting = false;
        }

        public void ClearCommentSelection()
        {
            SelectedComment = null;
        }

        public void SetAllCommentStates()
        {
            ChangeCommentStates(_bulkState, _selectedFile?.Data.Comments ?? []);
        }

        public void ResolveRemainingComments()
        {
            ChangeCommentStates(Models.ReviewCommentState.Resolved,
                _selectedFile?.Data.Comments.FindAll(x => x.State is Models.ReviewCommentState.ToReview or Models.ReviewCommentState.Todo) ?? []);
        }

        private void ChangeCommentStates(Models.ReviewCommentState state, List<Models.ReviewComment> comments)
        {
            var changes = new List<(Models.ReviewComment Comment, Models.ReviewCommentState State)>();
            foreach (var comment in comments)
            {
                if (comment.State == state)
                    continue;
                changes.Add((comment, comment.State));
                comment.State = state;
            }
            if (changes.Count == 0)
                return;
            _undo.Push(changes);
            Status = App.Text("Reviews.BulkResult", changes.Count);
            NotifyFileChanged(_selectedFile.Data);
            _context?.Refresh();
            OnPropertyChanged(nameof(CanUndo));
        }

        public void UndoCommentStates()
        {
            if (_undo.Count == 0)
                return;
            foreach (var change in _undo.Pop())
            {
                change.Comment.State = change.State;
                foreach (var file in _snapshot.Files)
                    if (file.Comments.Contains(change.Comment))
                        NotifyFileChanged(file);
            }
            _context?.Refresh();
            OnPropertyChanged(nameof(CanUndo));
        }

        public void BeginReattach(Models.ReviewComment comment)
        {
            _reattach = comment;
            Status = App.Text("Reviews.ReattachHint");
            _context?.Refresh();
        }

        public void CancelReattach()
        {
            _reattach = null;
            _context?.Refresh();
        }

        public Models.ReviewComment ReattachComment(Models.ReviewFile target, Models.ReviewAnchor anchor)
        {
            var comment = _reattach;
            if (comment == null)
                throw new InvalidOperationException("No review comment is selected for reattachment.");
            foreach (var file in _snapshot.Files)
            {
                if (!file.Comments.Contains(comment) || ReferenceEquals(file, target))
                    continue;
                file.Comments.Remove(comment);
                NotifyFileChanged(file);
                break;
            }
            if (!target.Comments.Contains(comment))
                target.Comments.Add(comment);
            comment.Anchor = anchor;
            _reattach = null;
            NotifyFileChanged(target);
            return comment;
        }

        public Task ShowOriginalAsync(Models.ReviewComment comment)
        {
            return RunBusyAsync(async () =>
            {
                var snapshot = _review.Snapshots.Find(x => x.Id == comment.OriginSnapshot);
                if (snapshot == null)
                    throw new InvalidOperationException("The original review snapshot is missing.");
                await OpenSnapshotAsync(snapshot);
                FileFilter = string.Empty;
                SelectedFile = Files.FirstOrDefault(x => x.Data.Path == comment.OriginPath || x.Data.OldPath == comment.OriginPath);
                SelectedComment = Comments.FirstOrDefault(x => x.Data.Id == comment.Id);
            });
        }

        public async Task CopyForAI()
        {
            if (_review != null && _snapshot != null)
                await App.CopyTextAsync(Models.ReviewExport.CreateText(_review, _snapshot));
        }

        private void QueueSave()
        {
            if (_review == null)
                return;
            _review.MarkChanged();
            if (IsPreview && string.IsNullOrWhiteSpace(_review.GeneralText) &&
                !_review.Snapshots.Exists(x => x.Files.Exists(f => f.Comments.Count > 0 || f.Progress != Models.ReviewFileProgress.Unreviewed)))
                return;
            if (!SavedReviews.Contains(_review))
            {
                SavedReviews.Insert(0, _review);
                _selectedReview = _review;
                OnPropertyChanged(nameof(SelectedReview));
                OnPropertyChanged(nameof(IsPreview));
            }
            _review.LastSnapshotId = _snapshot.Id;
            _pending[_review.Id] = _review;
            SaveStatus = App.Text("Reviews.Saving");
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public Task FlushAsync()
        {
            _saveTimer.Stop();
            var tasks = new List<Task>();
            foreach (var review in _pending.Values)
                if (review.ChangeVersion != review.SavedVersion)
                    tasks.Add(_store.SaveAsync(review));
            if (tasks.Count == 0)
                return _lastSave;
            var previous = _lastSave;
            _lastSave = CompleteSaveAsync(previous, tasks);
            return _lastSave;
        }

        private async Task CompleteSaveAsync(Task previous, List<Task> tasks)
        {
            try
            {
                try
                {
                    await previous.ConfigureAwait(false);
                }
                catch
                {
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);
                _lastSaveError = string.Empty;
                if (!_disposed)
                    Dispatcher.UIThread.Post(() => SaveStatus = IsPreview ? string.Empty :
                        _review?.SavedVersion == _review?.ChangeVersion ? App.Text("Reviews.Saved") : App.Text("Reviews.Saving"));
            }
            catch (Exception exception)
            {
                _lastSaveError = exception.Message;
                if (!_disposed)
                    Dispatcher.UIThread.Post(() => SaveStatus = App.Text("Reviews.SaveFailed", exception.Message));
            }
        }

        private async Task RunBusyAsync(Func<Task> action)
        {
            if (_disposed)
                return;
            await _interactionLock.WaitAsync();
            if (_disposed)
            {
                _interactionLock.Release();
                return;
            }
            IsBusy = true;
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                Status = exception.Message;
                _selectedReview = SavedReviews.Contains(_review) ? _review : null;
                OnPropertyChanged(nameof(SelectedReview));
                OnPropertyChanged(nameof(SelectedSnapshot));
            }
            finally
            {
                IsBusy = false;
                _interactionLock.Release();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _saveTimer.Stop();
            FlushAsync().GetAwaiter().GetResult();
            if (_lastSaveError.Length > 0)
                App.RaiseException(_repository, App.Text("Reviews.SaveFailed", _lastSaveError));
        }
    }
}
