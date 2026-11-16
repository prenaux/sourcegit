using System;
using System.Collections.Generic;
using System.IO;
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
        private readonly Repository _owner;
        private readonly Models.RepositoryUIStates _uiStates;
        private readonly Models.ReviewStore _store;
        private readonly DispatcherTimer _saveTimer;
        private readonly SemaphoreSlim _interactionLock = new(1);
        private readonly Dictionary<string, Models.Review> _pending = [];
        private readonly List<ReviewFileItem> _allFiles = [];
        private readonly List<Models.Change> _selectedChanges = [];
        private readonly Models.TreeExpansionState _expansionState = new();
        private Task _lastSave = Task.CompletedTask;
        private Models.Review _review;
        private Models.Review _selectedReview;
        private Models.ReviewSnapshot _snapshot;
        private Models.ReviewSnapshot _sourceSnapshot;
        private ReviewFileItem _selectedFile;
        private ReviewCommentItem _selectedComment;
        private ReviewDiffContext _context;
        private ReviewDiffContext _fileContext;
        private TextDiffContext _diff;
        private Models.ReviewComment _reattach;
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
        private bool _commentsExpanded;
        private int _activeTab;
        private Models.ChangeViewMode _viewMode = Models.ChangeViewMode.Tree;
        private CancellationTokenSource _fileLoadCancellation;
        private bool _isLoadingFile;
        private string _generalReviewMessage = string.Empty;
        private string _fileViewNotice = string.Empty;
        private ReviewFileFilter _reviewFilter;
        public Task ReviewLoadTask { get; private set; } = Task.CompletedTask;
        public Task FileLoadTask { get; private set; } = Task.CompletedTask;
        public event EventHandler FocusEditorRequested;
        public event EventHandler CommentTargetRequested;

        public AvaloniaList<Models.Review> SavedReviews { get; } = [];
        public AvaloniaList<Models.ReviewSnapshot> Snapshots { get; } = [];
        public AvaloniaList<ReviewFileItem> Files { get; } = [];
        public AvaloniaList<ReviewCommentItem> Comments { get; } = [];
        public List<Models.Change> Changes { get; private set; } = [];
        public ReviewFileFilter[] ReviewFilterChoices { get; } = Enum.GetValues<ReviewFileFilter>();
        public Models.ReviewCommentState[] StateChoices { get; } = Enum.GetValues<Models.ReviewCommentState>();
        public Models.Review CurrentReview => _review;
        public Models.ReviewSnapshot CurrentSnapshot => _snapshot;
        public bool HasPendingReattach => _reattach != null;
        public bool HasReview => _review != null;
        public bool IsLoadingFile { get => _isLoadingFile; private set => SetProperty(ref _isLoadingFile, value); }
        public bool HasTextDiff => _diff != null;
        public int AddedLines => _diff?.Data?.AddedLines ?? 0;
        public int DeletedLines => _diff?.Data?.DeletedLines ?? 0;
        public bool IsPreview => _review != null && !SavedReviews.Contains(_review);
        public string CommentsHeader => App.Text("Reviews.CommentsCount", Comments.Count);
        public string ComparisonLabel => _snapshot == null ? string.Empty : _snapshot.Source is Models.ReviewSourceKind.Commit or Models.ReviewSourceKind.Comparison ?
            $"{_snapshot.BaseRevision} -> {_snapshot.TargetRevision}" : App.Text("Reviews.Source." + _snapshot.Source);
        public string SelectedFileFullPath => _selectedFile == null ? string.Empty : GetFullPath(_selectedFile.Path);
        public string FileViewNotice { get => _fileViewNotice; private set => SetProperty(ref _fileViewNotice, value); }
        public Models.TreeExpansionState ExpansionState => _expansionState;
        public ReviewDiffContext FileContext { get => _fileContext; private set => SetProperty(ref _fileContext, value); }
        public int ActiveTabIndex
        {
            get => _activeTab;
            set
            {
                if (!SetProperty(ref _activeTab, value))
                    return;
                if (value == 1 && _fileContext?.IsEditing != true)
                    _fileContext?.BeginFileComment();
                else if (value == 0 && _context?.IsEditing != true)
                    CommentTargetRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        public bool IsCommentsExpanded
        {
            get => _uiStates?.ReviewCommentsExpanded ?? _commentsExpanded;
            set
            {
                if (IsCommentsExpanded == value)
                    return;
                _commentsExpanded = value;
                if (_uiStates != null)
                    _uiStates.ReviewCommentsExpanded = value;
                OnPropertyChanged();
            }
        }

        public Models.ChangeViewMode ViewMode
        {
            get => _uiStates?.ReviewFileViewMode ?? _viewMode;
            set
            {
                if (ViewMode == value)
                    return;
                _viewMode = value;
                if (_uiStates != null)
                    _uiStates.ReviewFileViewMode = value;
                OnPropertyChanged();
            }
        }

        public List<ReviewFileItem> SelectedFileItems => _selectedChanges.Count > 0 ?
            Files.Where(x => _selectedChanges.Contains(x.Change)).ToList() :
            _selectedFile != null ? [_selectedFile] : [];

        public int SelectedToFixCount
        {
            get
            {
                var count = 0;
                foreach (var item in SelectedFileItems)
                    foreach (var comment in item.Data.Comments)
                        if (comment.State == Models.ReviewCommentState.ToFix && !string.IsNullOrWhiteSpace(comment.Body))
                            count++;
                return count;
            }
        }

        public bool CanEditFileComment => SelectedFileItems.Count == 1;
        public bool CanMarkFilesAsOk => SelectedFileItems.Count > 0 && SelectedToFixCount == 0;
        public bool CanExportPatch => SelectedFileItems.Count > 0 && SelectedFileItems.TrueForAll(x =>
            x.SourceFile != null && x.SourceFile.IsLoaded && !x.IsRecordedContext && x.SourceFile.Patch.Length > 0);

        public bool IsLocalSource => _snapshot?.Source is Models.ReviewSourceKind.Staged or Models.ReviewSourceKind.Unstaged or Models.ReviewSourceKind.AllLocal;
        public string RepositoryPath => _repository;
        public Repository Owner => _owner;

        public bool CanActOnWorkingFile(ReviewFileItem item)
        {
            if (!IsLocalSource || item?.SourceFile == null || !item.SourceFile.IsLoaded)
                return false;
            return !item.Data.HasReviewWork || HasMatchingRecordedCode(item.Data, item.SourceFile);
        }

        public Models.Review SelectedReview
        {
            get => _selectedReview;
            set
            {
                if (SetProperty(ref _selectedReview, value) && !_isSelecting && value != null)
                    ReviewLoadTask = RunBusyAsync(() => OpenReviewAsync(value));
            }
        }

        public Models.ReviewSnapshot SelectedSnapshot
        {
            get => _snapshot;
            set
            {
                if (value == null || _isSelecting || ReferenceEquals(value, _snapshot))
                    return;
                ReviewLoadTask = RunBusyAsync(() => OpenSnapshotAsync(value));
            }
        }

        public ReviewFileItem SelectedFile
        {
            get => _selectedFile;
            set
            {
                if (_isSelecting && value == null)
                    return;
                var previous = _selectedFile;
                if (SetProperty(ref _selectedFile, value) && !_isSelecting)
                {
                    _fileLoadCancellation?.Cancel();
                    ReleasePreview(previous);
                    BuildFileView();
                    SyncSelectedChanges();
                }
            }
        }

        public List<Models.Change> SelectedChanges
        {
            get => _selectedChanges;
            set
            {
                var next = value ?? [];
                if (_selectedChanges.SequenceEqual(next))
                    return;
                _selectedChanges.Clear();
                _selectedChanges.AddRange(next);
                OnPropertyChanged();
                OnSelectionDependentChanged();
                if (_isSelecting || next.Count != 1)
                    return;
                var item = Files.FirstOrDefault(x => ReferenceEquals(x.Change, next[0]));
                if (item != null && !ReferenceEquals(item, _selectedFile))
                    SelectedFile = item;
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

        public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
        public bool SourceChanged { get => _sourceChanged; private set => SetProperty(ref _sourceChanged, value); }
        public string Status { get => _status; private set => SetProperty(ref _status, value); }
        public string SaveStatus { get => _saveStatus; private set => SetProperty(ref _saveStatus, value); }

        public string Title
        {
            get => _review?.Title ?? string.Empty;
            set
            {
                if (_review == null || _review.Title == value)
                    return;
                _review.Title = value ?? string.Empty;
                _review.HasGeneralEdits = true;
                _generalReviewMessage = _review.Title + "\n\n" + _review.GeneralText;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneralReviewMessage));
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
                _review.HasGeneralEdits = true;
                _generalReviewMessage = _review.Title + "\n\n" + _review.GeneralText;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneralReviewMessage));
                QueueSave();
            }
        }

        public string GeneralReviewMessage
        {
            get => _generalReviewMessage;
            set
            {
                if (_review == null || GeneralReviewMessage == value)
                    return;
                _generalReviewMessage = value ?? string.Empty;
                var message = _generalReviewMessage.Replace("\r\n", "\n", StringComparison.Ordinal);
                var separator = message.IndexOf('\n');
                _review.Title = separator < 0 ? message : message[..separator];
                _review.GeneralText = separator < 0 ? string.Empty : message[(separator + 1)..].TrimStart('\n');
                _review.HasGeneralEdits = !string.IsNullOrWhiteSpace(message);
                OnPropertyChanged();
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(GeneralText));
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

        public ReviewFileFilter ReviewFilter
        {
            get => _reviewFilter;
            set
            {
                if (SetProperty(ref _reviewFilter, value))
                    FilterFiles();
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
            _owner = repository;
            _uiStates = repository.UIStates;
            _expansionState = _uiStates.ReviewFileTreeExpansion;
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
        }

        private async Task LoadSavedReviewsAsync()
        {
            if (_initialized)
                return;
            SavedReviews.AddRange(await Task.Run(_store.LoadReviews));
            _initialized = true;
        }

        public Task OpenSourceAsync(Models.ReviewSourceKind source, string baseRevision, string targetRevision)
        {
            return RunBusyAsync(async () =>
            {
                await LoadSavedReviewsAsync();
                var snapshot = await Task.Run(() => new Commands.CaptureReview(_repository).CaptureAsync(source, baseRevision, targetRevision));
                if (_disposed)
                    return;
                foreach (var saved in SavedReviews)
                {
                    var match = saved.Snapshots.Find(x => CanReuseSnapshot(x, snapshot));
                    if (match == null)
                        continue;
                    await LoadSnapshotViewAsync(saved, match, snapshot);
                    return;
                }
                OpenPreview(snapshot);
            });
        }

        private static bool CanReuseSnapshot(Models.ReviewSnapshot saved, Models.ReviewSnapshot preview)
        {
            if (saved.Id == preview.Id)
                return true;
            if (saved.Source != preview.Source || saved.BaseRevision != preview.BaseRevision || saved.TargetRevision != preview.TargetRevision)
                return false;
            if (saved.Source is Models.ReviewSourceKind.Commit or Models.ReviewSourceKind.Comparison)
                return true;
            var reviewed = saved.Files.FindAll(x => x.HasReviewWork && !x.IsOutsideDiff);
            return reviewed.Count > 0 && reviewed.TrueForAll(old => old.SourceIdentity.Length > 0 &&
                preview.Files.Exists(file => file.Path == old.Path && file.SourceIdentity == old.SourceIdentity));
        }

        public void OpenPreview(Models.ReviewSnapshot snapshot)
        {
            _sourceSnapshot = snapshot;
            _review = new Models.Review
            {
                Title = snapshot.Subject.Length > 0 ? snapshot.Subject : App.Text("Reviews.DefaultTitle", App.Text("Reviews.Source." + snapshot.Source), DateTime.Now.ToString("yyyy-MM-dd HH:mm")),
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
                var capture = new Commands.CaptureReview(_repository);
                foreach (var file in next.Files)
                    if (previous.Files.Exists(x => x.HasReviewWork && (x.Path == file.Path || x.Path == file.OldPath)))
                        await capture.LoadFileAsync(next, file);
                Models.ReviewMigration.MigrateComments(previous, next, hash => next.Contents.TryGetValue(hash, out var text) ? text : ReadContent(hash));
                _review.Snapshots.Add(next);
                _sourceSnapshot = next;
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
                var files = _allFiles.Where(x => !x.IsRecordedContext && (x.Data.HasReviewWork || ReferenceEquals(x, _selectedFile)))
                    .Select(x => x.SourceFile).ToList();
                var changed = _allFiles.Exists(x => x.IsRecordedContext && x.SourceFile != null && x.Data.HasReviewWork) ||
                    await new Commands.CaptureReview(_repository).HaveFilesChangedAsync(_sourceSnapshot ?? snapshot, files);
                if (ReferenceEquals(snapshot, _snapshot))
                    SourceChanged = changed;
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
            await LoadSnapshotViewAsync(review, snapshot);
        }

        private Task OpenSnapshotAsync(Models.ReviewSnapshot snapshot) => LoadSnapshotViewAsync(_review, snapshot);

        private async Task LoadSnapshotViewAsync(Models.Review review, Models.ReviewSnapshot snapshot, Models.ReviewSnapshot source = null)
        {
            _fileLoadCancellation?.Cancel();
            await Task.Run(() => _store.LoadSnapshot(review, snapshot));
            var sourceError = string.Empty;
            if (source == null)
            {
                try
                {
                    source = await Task.Run(() => new Commands.CaptureReview(_repository).CaptureAsync(snapshot.Source, snapshot.BaseRevision, snapshot.TargetRevision));
                }
                catch (Exception exception)
                {
                    sourceError = App.Text("Reviews.SourceUnavailable", exception.Message);
                }
            }
            if (_disposed)
                return;
            _review = review;
            _sourceSnapshot = source;
            if (source != null)
                snapshot.Subject = source.Subject;
            SetSnapshot(snapshot);
            if (sourceError.Length > 0)
                Status = sourceError;
        }

        private void SetSnapshot(Models.ReviewSnapshot snapshot)
        {
            _isSelecting = true;
            try
            {
                _snapshot = snapshot;
                _selectedReview = SavedReviews.Contains(_review) ? _review : null;
                Snapshots.Clear();
                Snapshots.AddRange(_review.Snapshots);
                _allFiles.Clear();
                var recorded = snapshot.Files.ToDictionary(x => x.Path, StringComparer.Ordinal);
                var paths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var source in _sourceSnapshot?.Files ?? [])
                {
                    var file = recorded.TryGetValue(source.Path, out var existing) ? existing : source;
                    _allFiles.Add(new ReviewFileItem(file, source));
                    paths.Add(source.Path);
                }
                foreach (var file in snapshot.Files)
                    if (file.HasReviewWork && paths.Add(file.Path))
                        _allFiles.Add(new ReviewFileItem(file, null));
                _allFiles.Sort((a, b) => StringComparer.Ordinal.Compare(a.Path, b.Path));
                _selectedFile = null;
                _selectedComment = null;
                _reattach = null;
            }
            finally
            {
                _isSelecting = false;
            }
            _generalReviewMessage = _review.Title + "\n\n" + _review.GeneralText;
            SourceChanged = false;
            SaveStatus = string.Empty;
            Status = IsPreview ? App.Text("Reviews.PreviewHint") : string.Empty;
            FilterFiles();
            OnPropertyChanged(nameof(SelectedReview));
            OnPropertyChanged(nameof(SelectedSnapshot));
            OnPropertyChanged(nameof(ComparisonLabel));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(GeneralText));
            OnPropertyChanged(nameof(GeneralReviewMessage));
            OnPropertyChanged(nameof(HasReview));
            OnPropertyChanged(nameof(IsPreview));
        }

        private void FilterFiles()
        {
            var selected = _selectedFile;
            _isSelecting = true;
            Files.Clear();
            Files.AddRange(_allFiles.Where(MatchesFileFilter));
            Changes = Files.Select(x => x.Change).ToList();
            OnPropertyChanged(nameof(Changes));
            _isSelecting = false;
            _selectedFile = null;
            SelectedFile = selected != null && Files.Contains(selected) ? selected : Files.Count > 0 ? Files[0] : null;
            SyncSelectedChanges();
            if (SelectedFile == null)
                BuildFileView();
        }

        private void SyncSelectedChanges()
        {
            _isSelecting = true;
            _selectedChanges.Clear();
            if (_selectedFile != null)
                _selectedChanges.Add(_selectedFile.Change);
            OnPropertyChanged(nameof(SelectedChanges));
            _isSelecting = false;
            OnSelectionDependentChanged();
        }

        private void OnSelectionDependentChanged()
        {
            OnPropertyChanged(nameof(SelectedFileItems));
            OnPropertyChanged(nameof(SelectedToFixCount));
            OnPropertyChanged(nameof(CanEditFileComment));
            OnPropertyChanged(nameof(CanMarkFilesAsOk));
            OnPropertyChanged(nameof(CanExportPatch));
        }

        private void RefreshFileMarks()
        {
            foreach (var item in Files)
                item.RefreshMark();
        }

        private bool MatchesFileFilter(ReviewFileItem file)
        {
            return (string.IsNullOrEmpty(_fileFilter) || file.Path.Contains(_fileFilter, StringComparison.OrdinalIgnoreCase)) &&
                (_reviewFilter == ReviewFileFilter.All || (_reviewFilter == ReviewFileFilter.Reviewed) == file.Data.HasReviewWork);
        }

        public void RefreshFileFilter()
        {
            if (_isSelecting || _disposed || _reviewFilter == ReviewFileFilter.All)
                return;
            if (!Files.SequenceEqual(_allFiles.Where(MatchesFileFilter)))
                FilterFiles();
        }

        private void ReleasePreview(ReviewFileItem item)
        {
            var file = item?.SourceFile;
            if (file == null || item.Data.HasReviewWork || file.SourceIdentity.Length == 0)
                return;
            foreach (var hash in new[] { file.OldContent, file.NewContent })
                if (!_allFiles.Exists(x => x.Data.HasReviewWork && (x.Data.OldContent == hash || x.Data.NewContent == hash)))
                    _sourceSnapshot?.Contents.TryRemove(hash, out _);
            file.Patch = string.Empty;
            file.IsLoaded = false;
        }

        private void BuildFileView()
        {
            _fileLoadCancellation?.Cancel();
            _selectedComment = null;
            SelectDisplayContext(_selectedFile);
            RebuildDiff();
            RefreshComments();
            OnPropertyChanged(nameof(SelectedFileFullPath));
            IsLoadingFile = _selectedFile?.SourceFile?.IsLoaded == false;
            if (IsLoadingFile)
            {
                _fileLoadCancellation = new CancellationTokenSource();
                FileLoadTask = LoadSelectedFileAsync(_sourceSnapshot, _selectedFile, _fileLoadCancellation.Token);
            }
        }

        private static bool HasMatchingRecordedCode(Models.ReviewFile recorded, Models.ReviewFile source)
        {
            if (recorded.IsBinary != source.IsBinary)
                return false;
            if (recorded.OldContent.Length > 0 || recorded.NewContent.Length > 0)
                return recorded.OldContent == source.OldContent && recorded.NewContent == source.NewContent;
            return recorded.SourceIdentity == source.SourceIdentity;
        }

        private void SelectDisplayContext(ReviewFileItem item)
        {
            Context = null;
            FileViewNotice = string.Empty;
            if (item == null)
                return;
            item.IsRecordedContext = item.SourceFile == null ||
                (item.SourceFile.IsLoaded && item.Data.HasReviewWork && !HasMatchingRecordedCode(item.Data, item.SourceFile));
            var display = item.DisplayFile;
            if (item.IsRecordedContext)
                FileViewNotice = App.Text("Reviews.RecordedContextOnly");
            if (display?.IsLoaded == true)
            {
                Context = new ReviewDiffContext(this, item.Data, display);
                FileContext = new ReviewDiffContext(this, item.Data, display, true);
            }
            else
            {
                FileContext = null;
            }
        }

        private async Task LoadSelectedFileAsync(Models.ReviewSnapshot snapshot, ReviewFileItem item, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Run(() => new Commands.CaptureReview(_repository).LoadFileAsync(snapshot, item.SourceFile, cancellationToken), cancellationToken);
                if (_disposed || cancellationToken.IsCancellationRequested || !ReferenceEquals(item, _selectedFile))
                    return;
                SelectDisplayContext(item);
                RebuildDiff();
                if (_selectedComment != null)
                    Context?.EditComment(_selectedComment.Data);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                if (!_disposed && !cancellationToken.IsCancellationRequested && ReferenceEquals(item, _selectedFile))
                {
                    if (item.Data.HasReviewWork)
                    {
                        item.IsRecordedContext = true;
                        Context = new ReviewDiffContext(this, item.Data, item.Data);
                        RebuildDiff();
                        FileViewNotice = App.Text("Reviews.SourceUnavailable", exception.Message);
                    }
                    else
                        Status = exception.Message;
                }
            }
            finally
            {
                if (ReferenceEquals(item, _selectedFile))
                    IsLoadingFile = false;
            }
        }

        private void RebuildDiff()
        {
            var file = _selectedFile?.DisplayFile;
            var parsed = file == null || !file.IsLoaded || file.IsBinary ? null : Commands.Diff.ParsePatch(file.Patch).TextDiff;
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
            OnPropertyChanged(nameof(AddedLines));
            OnPropertyChanged(nameof(DeletedLines));
        }

        public string ReadContent(string hash)
        {
            if (_sourceSnapshot != null && _sourceSnapshot.Contents.TryGetValue(hash, out var sourceText))
                return sourceText;
            foreach (var snapshot in _review.Snapshots)
                if (snapshot.Contents.TryGetValue(hash, out var text))
                    return text;
            return _store.ReadContent(hash);
        }

        public string GetFullPath(string path) => Path.GetFullPath(Path.Combine(_repository, path));

        public void AddComment(Models.ReviewFile file, Models.ReviewComment comment)
        {
            if (ReferenceEquals(_selectedFile?.Data, file) && !_selectedFile.IsRecordedContext)
            {
                var source = _selectedFile.SourceFile;
                file.Patch = source.Patch;
                file.OldContent = source.OldContent;
                file.NewContent = source.NewContent;
                file.SourceIdentity = source.SourceIdentity;
            }
            file.Comments.Add(comment);
            NotifyFileChanged(file);
        }

        public void RemoveComment(Models.ReviewFile file, Models.ReviewComment comment)
        {
            if (file.Comments.Remove(comment))
                NotifyFileChanged(file);
        }

        public void NotifyFileChanged(Models.ReviewFile file)
        {
            if (!_snapshot.Files.Contains(file))
                _snapshot.Files.Add(file);
            file.MarkChanged();
            RefreshComments();
            RefreshFileMarks();
            QueueSave();
            if (_context?.IsEditing != true && _fileContext?.IsEditing != true)
                RefreshFileFilter();
            OnSelectionDependentChanged();
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
            OnPropertyChanged(nameof(CommentsHeader));
        }

        public void NotifyEditorOpened(ReviewDiffContext context)
        {
            ActiveTabIndex = ReferenceEquals(context, _fileContext) ? 1 : 0;
            FocusEditorRequested?.Invoke(this, EventArgs.Empty);
        }

        public void OpenFileComment()
        {
            if (!CanEditFileComment)
                return;
            FileContext?.BeginFileComment();
        }

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

        public void SetSelectedCommentsState(Models.ReviewCommentState state)
        {
            var files = SelectedFileItems;
            var count = 0;
            foreach (var item in files)
                foreach (var comment in item.Data.Comments)
                {
                    if (comment.State == state || string.IsNullOrWhiteSpace(comment.Body))
                        continue;
                    comment.State = state;
                    count++;
                }
            if (count == 0)
                return;
            if (files.Count > 0)
                NotifyFileChanged(files[0].Data);
            Status = App.Text("Reviews.BulkResult", count);
            RefreshFileFilter();
        }

        public void MarkFilesAsOk()
        {
            if (!CanMarkFilesAsOk)
                return;
            var files = SelectedFileItems;
            foreach (var item in files)
            {
                var comment = item.Data.Comments.Find(x => x.Anchor.Side == Models.ReviewSide.File);
                if (comment == null)
                {
                    var anchor = new Models.ReviewAnchor
                    {
                        Side = Models.ReviewSide.File,
                        Attachment = item.Data.IsOutsideDiff ? Models.ReviewAttachment.OutsideDiff : Models.ReviewAttachment.Attached,
                    };
                    AddComment(item.Data, new Models.ReviewComment
                    {
                        Body = "OK",
                        State = Models.ReviewCommentState.Dismissed,
                        Anchor = anchor,
                        OriginAnchor = anchor.Copy(),
                        OriginSnapshot = _snapshot.Id,
                        OriginPath = item.Path,
                    });
                }
                else
                {
                    comment.Body = "OK";
                    comment.State = Models.ReviewCommentState.Dismissed;
                    NotifyFileChanged(item.Data);
                }
            }
            Status = App.Text("Reviews.MarkedOk", files.Count);
            RefreshFileFilter();
        }

        public string GetSelectedPatchText()
        {
            if (!CanExportPatch)
                return string.Empty;
            var builder = new System.Text.StringBuilder();
            foreach (var item in SelectedFileItems)
                builder.Append(item.SourceFile.Patch.TrimEnd('\n')).Append('\n');
            return builder.ToString();
        }

        public Task CopyPatchAsync()
        {
            var patch = GetSelectedPatchText();
            return patch.Length == 0 ? Task.CompletedTask : App.CopyTextAsync(patch);
        }

        public void BeginReattach(Models.ReviewComment comment)
        {
            _reattach = comment;
            Status = App.Text("Reviews.ReattachHint");
            _context?.Refresh();
        }

        public void CancelReattach()
        {
            if (_reattach != null)
                Status = string.Empty;
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

        public async Task CopyReview()
        {
            if (_review != null && _snapshot != null)
                await App.CopyTextAsync(Models.ReviewExport.CreateText(_review, _snapshot));
        }

        private void QueueSave()
        {
            if (_review == null)
                return;
            _review.MarkChanged();
            if (IsPreview && !_review.HasGeneralEdits && string.IsNullOrWhiteSpace(_review.GeneralText) &&
                !_review.Snapshots.Exists(x => x.Files.Exists(f => f.HasReviewWork)))
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
            _fileLoadCancellation?.Cancel();
            _saveTimer.Stop();
            FlushAsync().GetAwaiter().GetResult();
            if (_lastSaveError.Length > 0)
                App.RaiseException(_repository, App.Text("Reviews.SaveFailed", _lastSaveError));
        }
    }
}
