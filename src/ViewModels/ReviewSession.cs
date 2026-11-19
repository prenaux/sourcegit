using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public enum ReviewFileFilter
    {
        All,
        Reviewed,
        Unreviewed,
        Comment,
        ToFix,
        Fixed,
        Dismissed,
    }

    // The values are the tab indexes of `Views.ReviewPanel`.
    public enum ReviewTab
    {
        Commit,
        Comment,
        FileComment,
        General,
    }

    public sealed class ReviewCommentItem(Models.ReviewComment comment) : ObservableObject
    {
        public Models.ReviewComment Data { get; } = comment;

        public string FirstLine
        {
            get
            {
                foreach (var line in Data.Body.Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line))
                        return line.Trim();
                return string.Empty;
            }
        }

        public string Details
        {
            get
            {
                var state = App.Text("Reviews.State." + Data.State);
                var location = ReviewSession.FormatAnchorLabel(Data.Anchor);
                if (Data.Anchor.Attachment == Models.ReviewAttachment.Attached)
                    return $"{state} - {location}";
                return $"{state} - {App.Text("Reviews.Attachment." + Data.Anchor.Attachment)} - {location}";
            }
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(FirstLine));
            OnPropertyChanged(nameof(Details));
        }
    }

    // The content of one comment editor: an existing comment, or the anchor of a comment that the first typed text creates.
    public sealed class ReviewCommentDraft(ReviewSession session, ReviewTab tab) : ObservableObject
    {
        private Models.ReviewFile _file;
        private Models.ReviewComment _comment;
        private Models.ReviewAnchor _anchor;
        private string _body = string.Empty;
        private Models.ReviewCommentState _state = Models.ReviewCommentState.ToFix;

        public Models.ReviewCommentState[] StateChoices { get; } = Enum.GetValues<Models.ReviewCommentState>();
        public Models.ReviewComment Comment => _comment;
        public Models.ReviewAnchor Anchor => _comment?.Anchor ?? _anchor;
        public bool HasTarget => _file != null && Anchor != null;
        public bool ShowAnchorLabel => tab == ReviewTab.Comment;
        public string AnchorLabel => ReviewSession.FormatAnchorLabel(Anchor);
        public bool CanReattach => _comment != null && _comment.Anchor.Side != Models.ReviewSide.File &&
            _comment.Anchor.Attachment != Models.ReviewAttachment.Attached;
        public string AttachmentLabel => CanReattach ? App.Text("Reviews.Attachment." + _comment.Anchor.Attachment) : string.Empty;

        public string Body
        {
            get => _comment?.Body ?? _body;
            set
            {
                var body = value ?? string.Empty;
                if (Body == body)
                    return;

                _body = body;
                if (_comment != null)
                {
                    _comment.Body = body;
                    session.NotifyCommentChanged();
                }
                else if (HasTarget && !string.IsNullOrWhiteSpace(body))
                {
                    _comment = new Models.ReviewComment { Body = body, State = _state, Anchor = _anchor };
                    _anchor = null;
                    session.AddComment(_file, _comment);
                }
                Refresh();
            }
        }

        public Models.ReviewCommentState State
        {
            get => _comment?.State ?? _state;
            set
            {
                if (State == value)
                    return;

                _state = value;
                if (_comment != null)
                {
                    _comment.State = value;
                    session.NotifyCommentChanged();
                }
                Refresh();
            }
        }

        public void SetTarget(Models.ReviewFile file, Models.ReviewComment comment, Models.ReviewAnchor anchor)
        {
            if (comment != null && ReferenceEquals(comment, _comment))
                return;

            DropEmptyComment();
            _file = file;
            _comment = comment;
            _anchor = comment == null ? anchor : null;
            _body = string.Empty;
            _state = Models.ReviewCommentState.ToFix;
            Refresh();
        }

        public void ClearTarget()
        {
            SetTarget(null, null, null);
        }

        // A comment without text is not a finding. Its anchor stays the target, so new text creates the comment again.
        public void DropEmptyComment()
        {
            if (_comment == null || _comment.HasBody)
                return;

            var comment = _comment;
            _comment = null;
            _anchor = comment.Anchor;
            _body = string.Empty;
            session.RemoveComment(_file, comment);
            Refresh();
        }

        public void BeginReattach()
        {
            if (_comment != null)
                session.BeginReattach(_comment);
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(Body));
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(Comment));
            OnPropertyChanged(nameof(Anchor));
            OnPropertyChanged(nameof(HasTarget));
            OnPropertyChanged(nameof(AnchorLabel));
            OnPropertyChanged(nameof(AttachmentLabel));
            OnPropertyChanged(nameof(CanReattach));
        }
    }

    // Binds one review to the file lists, the diff and the review panel of one page.
    public sealed class ReviewSession : ObservableObject
    {
        private readonly Reviews _reviews;
        private readonly HashSet<Models.ReviewComment> _located = [];
        private Models.Review _review;
        private Models.ReviewFile _file;
        private Models.Change _change;
        private List<Models.Change> _changes = [];
        private DiffContext _diffSource;
        private TextDiffContext _diff;
        private Models.ReviewComment _reattach;
        private ReviewCommentItem _selectedComment;
        private ReviewTab _activeTab;
        private ReviewFileFilter _filter;
        private string _generalReviewMessage = string.Empty;
        private bool _hasSelection;
        private bool _isSyncingSelection;

        public event EventHandler FocusEditorRequested;
        public event EventHandler FocusDiffRequested;
        public event EventHandler FilterChanged;
        public event EventHandler<Models.ReviewComment> RevealRequested;

        public ReviewCommentDraft LineDraft { get; }
        public ReviewCommentDraft FileDraft { get; }
        public AvaloniaList<ReviewCommentItem> Comments { get; } = [];
        public ReviewFileFilter[] FilterChoices { get; } = Enum.GetValues<ReviewFileFilter>();
        public Models.ReviewCommentState[] StateChoices { get; } = Enum.GetValues<Models.ReviewCommentState>();
        public Models.Review Review => _review;
        public bool HasReview => _review != null;
        public bool IsLocal => _review?.Source == Models.ReviewSourceKind.Local;
        public bool HasFile => _file != null;
        public Models.Change SelectedChange => _change;
        public bool HasPendingReattach => _reattach != null;
        public string CommentsCount => $"({Comments.Count})";
        public string CommentActionLabel => App.Text(_reattach != null ? "Reviews.AttachComment" :
            _hasSelection ? "Reviews.CommentSelection" : "Reviews.AddComment");

        public ReviewTab ActiveTab
        {
            get => _activeTab;
            set
            {
                if (!SetProperty(ref _activeTab, value))
                    return;

                OnPropertyChanged(nameof(ActiveTabIndex));
                OnPropertyChanged(nameof(IsCommentTabActive));
            }
        }

        public int ActiveTabIndex
        {
            get => (int)_activeTab;
            set
            {
                // A tab control reports -1 while it has no items.
                if (value >= 0)
                    ActiveTab = (ReviewTab)value;
            }
        }

        public bool IsCommentTabActive => _activeTab is ReviewTab.Comment or ReviewTab.FileComment;

        public ReviewFileFilter Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                    FilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ReviewCommentItem SelectedComment
        {
            get => _selectedComment;
            set
            {
                if (SetProperty(ref _selectedComment, value) && !_isSyncingSelection && value != null)
                    ShowComment(value.Data);
            }
        }

        // The first line is the review title. A blank line separates it from the general notes.
        public string GeneralReviewMessage
        {
            get => _generalReviewMessage;
            set
            {
                if (_review == null || _generalReviewMessage == value)
                    return;

                _generalReviewMessage = value ?? string.Empty;
                var message = _generalReviewMessage.Replace("\r\n", "\n", StringComparison.Ordinal);
                var separator = message.IndexOf('\n');
                _review.Title = separator < 0 ? message : message[..separator];
                _review.GeneralText = separator < 0 ? string.Empty : message[(separator + 1)..].TrimStart('\n');
                _review.HasGeneralEdits = true;
                OnPropertyChanged();
                _reviews.QueueSave(_review);
            }
        }

        public ReviewSession(Reviews reviews, ReviewTab initialTab)
        {
            _reviews = reviews;
            _activeTab = initialTab;
            _reviews.ReviewRemoved += OnReviewRemoved;
            LineDraft = new ReviewCommentDraft(this, ReviewTab.Comment);
            FileDraft = new ReviewCommentDraft(this, ReviewTab.FileComment);
        }

        public static string FormatAnchorLabel(Models.ReviewAnchor anchor)
        {
            if (anchor == null)
                return string.Empty;
            if (anchor.Side == Models.ReviewSide.File)
                return App.Text("Reviews.FileComment");
            if (anchor.HasOldRange && anchor.HasNewRange)
                return App.Text("Reviews.Range.Both", anchor.OldStartLine, anchor.OldEndLine, anchor.NewStartLine, anchor.NewEndLine);
            if (anchor.HasOldRange)
                return App.Text("Reviews.Range.Old", anchor.OldStartLine, anchor.OldEndLine);
            return App.Text("Reviews.Range.New", anchor.NewStartLine, anchor.NewEndLine);
        }

        public void OpenLocal()
        {
            var review = _reviews.FindOrCreateLocalReview();
            if (!ReferenceEquals(review, _review))
                ReplaceReview(review);
        }

        public void StartNewLocalReview()
        {
            ReplaceReview(_reviews.CreateLocalReview());
        }

        public void Open(Models.ReviewSourceKind source, string baseRevision, string targetRevision, string title)
        {
            var review = _reviews.FindOrCreateReview(source, baseRevision, targetRevision, title);
            if (ReferenceEquals(review, _review))
                return;

            SetReview(review);
            SelectFile(null);
        }

        public void Close()
        {
            if (_review == null)
                return;

            SetReview(null);
            SelectFile(null);
        }

        public void SetChanges(List<Models.Change> changes)
        {
            _changes = changes ?? [];
            RefreshMarks();
        }

        public void SelectFile(Models.Change change)
        {
            var isSameFile = change != null && _change != null && _file != null &&
                change.Path.Equals(_change.Path, StringComparison.Ordinal);
            _change = change;

            // The host selects the same file again each time it refreshes its lists. The editors must keep their content then.
            if (isSameFile)
                return;

            DetachDiff();
            _diffSource = null;
            _file = change == null || _review == null ? null : FindOrCreateFile(change);
            LineDraft.ClearTarget();
            RetargetFileDraft();
            RefreshComments();
            OnPropertyChanged(nameof(HasFile));
        }

        // The new diff context of the selected file reports here first. It can still show the content of the previous one.
        public void SetDiffSource(DiffContext source, TextDiffContext carried)
        {
            _diffSource = source;
            if (!ReferenceEquals(carried, _diff))
                DetachDiff();
        }

        public void AttachDiff(DiffContext source, TextDiffContext diff)
        {
            if (!ReferenceEquals(source, _diffSource) || ReferenceEquals(diff, _diff))
                return;

            DetachDiff();
            _diff = diff;
            if (diff == null || _file == null)
                return;

            if (Models.ReviewAnchors.AttachComments(_file, diff.Data, GetAttachPolicy(diff), _located) && _review.Files.Contains(_file))
                _reviews.QueueSave(_review);
            RefreshComments();
            OnPropertyChanged(nameof(Comments));
        }

        public bool IsCurrentDiff(TextDiffContext diff)
        {
            return diff != null && _file != null && ReferenceEquals(diff, _diff);
        }

        public Models.ReviewComment FindComment(TextDiffContext diff, Models.TextDiffLine line, Models.ReviewPane pane)
        {
            if (!IsCurrentDiff(diff))
                return null;

            return _file.Comments.Find(x => _located.Contains(x) && Models.ReviewAnchors.ContainsLine(x.Anchor, line, pane));
        }

        public List<Models.ReviewComment> GetLocatedComments(TextDiffContext diff)
        {
            return IsCurrentDiff(diff) ? _file.Comments.FindAll(_located.Contains) : [];
        }

        // The lines of the comment in the editor get an outline. The target of a new comment gets one only while its tab is open.
        public bool IsActiveLine(TextDiffContext diff, Models.TextDiffLine line, Models.ReviewPane pane)
        {
            if (!IsCurrentDiff(diff) || LineDraft.Anchor == null)
                return false;

            var comment = LineDraft.Comment;
            if (comment == null ? _activeTab != ReviewTab.Comment : !_located.Contains(comment))
                return false;
            return Models.ReviewAnchors.ContainsLine(LineDraft.Anchor, line, pane);
        }

        public void OpenComment(TextDiffContext diff, TextDiffSelectedChunk chunk, bool hasSelection)
        {
            if (!IsCurrentDiff(diff) || chunk == null)
                return;

            if (_reattach != null)
                ReattachComment(diff, chunk);
            else
                SetLineTarget(diff, chunk, hasSelection);

            if (!LineDraft.HasTarget)
                return;

            ActiveTab = ReviewTab.Comment;
            FocusEditorRequested?.Invoke(this, EventArgs.Empty);
        }

        // Follows the caret and the selection of the diff. The Comment tab then edits the code that the user looks at.
        public void TrackComment(TextDiffContext diff, TextDiffSelectedChunk chunk, bool hasSelection)
        {
            if (!IsCurrentDiff(diff) || chunk == null)
                return;

            if (_hasSelection != hasSelection)
            {
                _hasSelection = hasSelection;
                OnPropertyChanged(nameof(CommentActionLabel));
            }

            if (_reattach == null)
                SetLineTarget(diff, chunk, hasSelection);
        }

        public void EditComment(Models.ReviewComment comment)
        {
            if (_file == null || !_file.Comments.Contains(comment))
                return;

            ShowComment(comment);
            FocusEditorRequested?.Invoke(this, EventArgs.Empty);
        }

        public void OpenFileComment()
        {
            if (_file == null)
                return;

            ActiveTab = ReviewTab.FileComment;
            FocusEditorRequested?.Invoke(this, EventArgs.Empty);
        }

        public void FocusDiff()
        {
            CancelReattach();
            LineDraft.DropEmptyComment();
            FileDraft.DropEmptyComment();
            FocusDiffRequested?.Invoke(this, EventArgs.Empty);
        }

        public void BeginReattach(Models.ReviewComment comment)
        {
            _reattach = comment;
            OnPropertyChanged(nameof(HasPendingReattach));
            OnPropertyChanged(nameof(CommentActionLabel));
        }

        public void CancelReattach()
        {
            if (_reattach == null)
                return;

            _reattach = null;
            OnPropertyChanged(nameof(HasPendingReattach));
            OnPropertyChanged(nameof(CommentActionLabel));
        }

        public void AddComment(Models.ReviewFile file, Models.ReviewComment comment)
        {
            if (!_review.Files.Contains(file))
                _review.Files.Add(file);
            file.Comments.Add(comment);
            if (ReferenceEquals(file, _file) && _diff != null)
            {
                Models.ReviewAnchors.CaptureCode(comment.Anchor, _diff.Data);
                _located.Add(comment);
            }
            NotifyCommentChanged();
            SyncSelectedComment(LineDraft.Comment);
        }

        public void RemoveComment(Models.ReviewFile file, Models.ReviewComment comment)
        {
            if (file == null || !file.Comments.Remove(comment))
                return;

            _located.Remove(comment);
            NotifyCommentChanged();
        }

        public void NotifyCommentChanged()
        {
            RefreshComments();
            RefreshMarks();
            _reviews.QueueSave(_review);
        }

        public int CountToFix(List<Models.Change> changes)
        {
            var count = 0;
            foreach (var change in changes)
            {
                var file = FindFile(change);
                if (file != null)
                    count += file.Comments.Count(x => x.HasBody && x.State == Models.ReviewCommentState.ToFix);
            }
            return count;
        }

        public void MarkFilesAsOk(List<Models.Change> changes)
        {
            if (_review == null || changes.Count == 0 || CountToFix(changes) > 0)
                return;

            foreach (var change in changes)
            {
                var file = FindOrCreateFile(change);
                var comment = file.FileComment;
                if (comment == null)
                {
                    comment = new Models.ReviewComment { Anchor = new Models.ReviewAnchor { Side = Models.ReviewSide.File } };
                    file.Comments.Add(comment);
                    if (!_review.Files.Contains(file))
                        _review.Files.Add(file);
                }
                comment.Body = App.Text("Reviews.OkComment");
                comment.State = Models.ReviewCommentState.Comment;
            }
            NotifyBulkChange();
        }

        public void SetCommentsState(List<Models.Change> changes, Models.ReviewCommentState state)
        {
            if (_review == null)
                return;

            foreach (var change in changes)
            {
                var file = FindFile(change);
                if (file == null)
                    continue;
                foreach (var comment in file.Comments)
                    if (comment.HasBody)
                        comment.State = state;
            }
            NotifyBulkChange();
        }

        public bool MatchesFilter(Models.Change change)
        {
            if (_filter == ReviewFileFilter.All || _review == null)
                return true;

            var file = FindFile(change);
            if (_filter == ReviewFileFilter.Reviewed)
                return file is { HasReviewWork: true };
            if (_filter == ReviewFileFilter.Unreviewed)
                return file is not { HasReviewWork: true };

            var state = _filter switch
            {
                ReviewFileFilter.Comment => Models.ReviewCommentState.Comment,
                ReviewFileFilter.ToFix => Models.ReviewCommentState.ToFix,
                ReviewFileFilter.Fixed => Models.ReviewCommentState.Fixed,
                _ => Models.ReviewCommentState.Dismissed,
            };
            return file != null && file.Comments.Exists(x => x.HasBody && x.State == state);
        }

        public List<Models.Change> FilterChanges(List<Models.Change> changes)
        {
            if (_filter == ReviewFileFilter.All || _review == null)
                return changes;

            return changes.FindAll(MatchesFilter);
        }

        public async Task CopyReviewAsync()
        {
            if (_review != null)
                await App.CopyTextAsync(Models.ReviewExport.CreateText(_review));
        }

        private void SetReview(Models.Review review)
        {
            LineDraft.DropEmptyComment();
            FileDraft.DropEmptyComment();
            _review = review;
            _reattach = null;
            _change = null;
            _file = null;
            _generalReviewMessage = review == null ? string.Empty : review.Title + "\n\n" + review.GeneralText;
            RefreshMarks();
            OnPropertyChanged(nameof(Review));
            OnPropertyChanged(nameof(HasReview));
            OnPropertyChanged(nameof(IsLocal));
            OnPropertyChanged(nameof(GeneralReviewMessage));
            OnPropertyChanged(nameof(HasPendingReattach));
            OnPropertyChanged(nameof(CommentActionLabel));
        }

        // Changes the review under the files and the diff that the page shows now.
        private void ReplaceReview(Models.Review review)
        {
            var change = _change;
            var source = _diffSource;
            var diff = _diff;

            SetReview(review);
            SelectFile(change);
            _diffSource = source;
            AttachDiff(source, diff);
        }

        private void OnReviewRemoved(object sender, Models.Review review)
        {
            if (!ReferenceEquals(review, _review))
                return;

            if (review.Source == Models.ReviewSourceKind.Local)
                StartNewLocalReview();
            else
                ReplaceReview(_reviews.FindOrCreateReview(review.Source, review.BaseRevision, review.TargetRevision, review.Title));
        }

        private void DetachDiff()
        {
            _diff = null;
            _located.Clear();
            _hasSelection = false;
            if (LineDraft.Comment == null)
                LineDraft.ClearTarget();
            OnPropertyChanged(nameof(CommentActionLabel));
        }

        private Models.ReviewAttachPolicy GetAttachPolicy(TextDiffContext diff)
        {
            // A commit does not change, so a comment that this diff does not show is still valid.
            // A file with staged and unstaged changes has two diffs, and each diff shows a part of the comments.
            var change = diff.Option.WorkingCopyChange;
            if (change == null || (change.Index != Models.ChangeState.None && change.WorkTree != Models.ChangeState.None))
                return Models.ReviewAttachPolicy.Partial;
            return Models.ReviewAttachPolicy.Authoritative;
        }

        private Models.ReviewFile FindFile(Models.Change change)
        {
            if (_review == null)
                return null;

            return _review.FindFile(change.Path) ??
                (string.IsNullOrEmpty(change.OriginalPath) ? null : _review.FindFile(change.OriginalPath));
        }

        private Models.ReviewFile FindOrCreateFile(Models.Change change)
        {
            if (_file != null && _file.Path.Equals(change.Path, StringComparison.Ordinal))
                return _file;

            var file = FindFile(change);
            if (file == null)
                return new Models.ReviewFile { Path = change.Path, OldPath = change.OriginalPath ?? string.Empty };

            // Git reported a rename of a reviewed file. The comments follow the file to its new path.
            if (!file.Path.Equals(change.Path, StringComparison.Ordinal))
            {
                file.OldPath = file.Path;
                file.Path = change.Path;
                _reviews.QueueSave(_review);
            }
            return file;
        }

        private void SetLineTarget(TextDiffContext diff, TextDiffSelectedChunk chunk, bool hasSelection)
        {
            var pane = chunk.Combined ? Models.ReviewPane.Combined : chunk.IsOldSide ? Models.ReviewPane.Old : Models.ReviewPane.New;
            var lines = diff.Data.Lines;
            if (!hasSelection && chunk.StartIdx == chunk.EndIdx && chunk.StartIdx >= 0 && chunk.StartIdx < lines.Count)
            {
                var line = lines[chunk.StartIdx];
                var current = LineDraft.Comment;
                if (current != null && _located.Contains(current) && Models.ReviewAnchors.ContainsLine(current.Anchor, line, pane))
                    return;

                var existing = FindComment(diff, line, pane);
                if (existing != null)
                {
                    SetLineTarget(existing, null);
                    return;
                }
            }

            var anchor = Models.ReviewAnchors.CreateAnchor(diff.Data, chunk.StartIdx, chunk.EndIdx, pane);
            if (anchor == null)
                return;

            var sameRange = hasSelection ? null : _file.Comments.Find(x => _located.Contains(x) && HasSameRange(x.Anchor, anchor));
            if (sameRange != null)
                SetLineTarget(sameRange, null);
            else if (LineDraft.Comment != null || !HasSameRange(LineDraft.Anchor, anchor))
                SetLineTarget(null, anchor);
        }

        private void SetLineTarget(Models.ReviewComment comment, Models.ReviewAnchor anchor)
        {
            LineDraft.SetTarget(_file, comment, anchor);
            SyncSelectedComment(comment);
            OnPropertyChanged(nameof(LineDraft));
        }

        private static bool HasSameRange(Models.ReviewAnchor a, Models.ReviewAnchor b)
        {
            return a != null && b != null && a.Side == b.Side &&
                a.OldStartLine == b.OldStartLine && a.OldEndLine == b.OldEndLine &&
                a.NewStartLine == b.NewStartLine && a.NewEndLine == b.NewEndLine;
        }

        private void ReattachComment(TextDiffContext diff, TextDiffSelectedChunk chunk)
        {
            var pane = chunk.Combined ? Models.ReviewPane.Combined : chunk.IsOldSide ? Models.ReviewPane.Old : Models.ReviewPane.New;
            var anchor = Models.ReviewAnchors.CreateAnchor(diff.Data, chunk.StartIdx, chunk.EndIdx, pane);
            if (anchor == null)
                return;

            var comment = _reattach;
            CancelReattach();
            var previous = _review.Files.Find(x => x.Comments.Contains(comment));
            if (previous != null && !ReferenceEquals(previous, _file))
                previous.Comments.Remove(comment);
            if (!_review.Files.Contains(_file))
                _review.Files.Add(_file);
            if (!_file.Comments.Contains(comment))
                _file.Comments.Add(comment);
            Models.ReviewAnchors.CaptureCode(anchor, diff.Data);
            comment.Anchor = anchor;
            _located.Add(comment);
            NotifyCommentChanged();
            SetLineTarget(comment, null);
        }

        // Shows the comment in its tab and in the diff. The keyboard focus stays where it is.
        public void ShowComment(Models.ReviewComment comment)
        {
            if (comment.Anchor.Side == Models.ReviewSide.File)
            {
                ActiveTab = ReviewTab.FileComment;
                return;
            }

            SetLineTarget(comment, null);
            ActiveTab = ReviewTab.Comment;
            if (_located.Contains(comment))
                RevealRequested?.Invoke(this, comment);
        }

        private void SyncSelectedComment(Models.ReviewComment comment)
        {
            _isSyncingSelection = true;
            SelectedComment = comment == null ? null : Comments.FirstOrDefault(x => ReferenceEquals(x.Data, comment));
            _isSyncingSelection = false;
        }

        private void RetargetFileDraft()
        {
            if (_file == null)
            {
                FileDraft.ClearTarget();
                return;
            }

            FileDraft.SetTarget(_file, _file.FileComment, new Models.ReviewAnchor { Side = Models.ReviewSide.File });
        }

        private void NotifyBulkChange()
        {
            RetargetFileDraft();
            LineDraft.Refresh();
            NotifyCommentChanged();
            if (_filter != ReviewFileFilter.All)
                FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RefreshComments()
        {
            var selected = _selectedComment?.Data;
            var comments = _file == null ? [] : _file.Comments.FindAll(x => x.HasBody || ReferenceEquals(x, LineDraft.Comment) || ReferenceEquals(x, FileDraft.Comment));
            comments = comments
                .OrderBy(x => Models.ReviewAnchors.GetFirstLine(x.Anchor))
                .ThenBy(x => x.Anchor.EndLine - x.Anchor.StartLine)
                .ToList();

            _isSyncingSelection = true;
            if (comments.Count != Comments.Count || !comments.SequenceEqual(Comments.Select(x => x.Data)))
            {
                Comments.Clear();
                Comments.AddRange(comments.Select(x => new ReviewCommentItem(x)));
            }
            foreach (var item in Comments)
                item.Refresh();
            SelectedComment = selected == null ? null : Comments.FirstOrDefault(x => ReferenceEquals(x.Data, selected));
            _isSyncingSelection = false;
            OnPropertyChanged(nameof(CommentsCount));
        }

        private void RefreshMarks()
        {
            foreach (var change in _changes)
            {
                var toFix = 0;
                var comments = 0;
                foreach (var comment in FindFile(change)?.Comments ?? [])
                {
                    if (!comment.HasBody)
                        continue;
                    comments++;
                    if (comment.State == Models.ReviewCommentState.ToFix)
                        toFix++;
                }
                change.ReviewToFixCount = toFix;
                change.HasReviewComments = comments > 0;
            }
        }
    }
}
