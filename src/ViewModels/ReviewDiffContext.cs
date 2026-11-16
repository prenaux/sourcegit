using System;

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

    public enum ReviewPane
    {
        Combined,
        Old,
        New,
    }

    public sealed class ReviewFileItem : ObservableObject
    {
        public ReviewFileItem(Models.ReviewFile file, Models.ReviewFile sourceFile)
        {
            Data = file;
            SourceFile = sourceFile;
            IsRecordedContext = sourceFile == null;
            Change = new Models.Change
            {
                Path = file.Path,
                OriginalPath = file.OldPath,
                Index = GetChangeState(file),
            };
            RefreshMark();
        }

        public Models.ReviewFile Data { get; }
        public Models.ReviewFile SourceFile { get; }
        public Models.Change Change { get; }
        public bool IsRecordedContext { get; set; } = false;
        public Models.ReviewFile DisplayFile => IsRecordedContext ? Data : SourceFile;
        public string Path => Data.Path;

        public void RefreshMark()
        {
            var toFix = 0;
            var comments = 0;
            foreach (var comment in Data.Comments)
            {
                if (string.IsNullOrWhiteSpace(comment.Body))
                    continue;
                comments++;
                if (comment.State == Models.ReviewCommentState.ToFix)
                    toFix++;
            }
            Change.ReviewToFixCount = toFix;
            Change.HasReviewComments = comments > 0;
        }

        private static Models.ChangeState GetChangeState(Models.ReviewFile file)
        {
            if (file.OldPath.Length > 0 && file.OldPath != file.Path)
                return Models.ChangeState.Renamed;
            var patch = file.Patch;
            if (file.IsUntracked || (patch.Contains("new file mode", StringComparison.Ordinal) && patch.Contains("--- /dev/null", StringComparison.Ordinal)))
                return Models.ChangeState.Added;
            if (patch.Contains("deleted file mode", StringComparison.Ordinal) || patch.Contains("+++ /dev/null", StringComparison.Ordinal))
                return Models.ChangeState.Deleted;
            return Models.ChangeState.Modified;
        }
    }

    public sealed class ReviewCommentItem(Models.ReviewComment comment) : ObservableObject
    {
        public Models.ReviewComment Data { get; } = comment;
        public string Body => Data.Body;
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
        public string State => App.Text("Reviews.State." + Data.State);
        public string Attachment => App.Text("Reviews.Attachment." + Data.Anchor.Attachment);
        public string Location
        {
            get
            {
                var anchor = Data.Anchor.Attachment == Models.ReviewAttachment.Attached ? Data.Anchor : Data.OriginAnchor;
                var location = ReviewDiffContext.FormatAnchorLabel(anchor);
                return Data.Anchor.Attachment == Models.ReviewAttachment.Attached ? location : App.Text("Reviews.OriginalLocation", location);
            }
        }

        public string Details
        {
            get
            {
                var attachment = Data.Anchor.Attachment == Models.ReviewAttachment.Attached ? string.Empty : Attachment;
                return attachment.Length == 0 ? $"{State} - {Location}" : $"{State} - {attachment} - {Location}";
            }
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(Body));
            OnPropertyChanged(nameof(FirstLine));
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(Attachment));
            OnPropertyChanged(nameof(Location));
            OnPropertyChanged(nameof(Details));
        }
    }

    public sealed class ReviewDiffContext : ObservableObject
    {
        private readonly Reviews _owner;
        private readonly Models.ReviewFile _file;
        private readonly Models.ReviewFile _sourceFile;
        private readonly bool _fileCommentsOnly;
        private Models.ReviewComment _comment;
        private Models.ReviewAnchor _draft;
        private string _body = string.Empty;
        private Models.ReviewCommentState _state = Models.ReviewCommentState.ToFix;
        private Models.ReviewComment _hoveredComment;
        private bool _hasSelection;

        public event EventHandler EditorOpened;
        public Models.ReviewAnchor ActiveAnchor => _comment?.Anchor ?? _draft;
        public bool HasSelection => _hasSelection;

        public Models.ReviewCommentState[] StateChoices { get; } = Enum.GetValues<Models.ReviewCommentState>();
        public bool IsEditing => _comment != null || _draft != null;
        public bool CanReattach => _comment != null && _comment.Anchor.Attachment != Models.ReviewAttachment.Attached;
        public bool CanOpenOriginal => _comment != null && _comment.OriginAnchor.Side != Models.ReviewSide.File &&
            (_comment.OriginSnapshot != _owner.CurrentSnapshot.Id || CanReattach);
        public string CommentActionLabel => App.Text(_owner.HasPendingReattach ? "Reviews.AttachComment" :
            _hoveredComment != null ? "Reviews.EditComment" : _hasSelection ? "Reviews.CommentSelection" : "Reviews.AddComment");
        public Models.ReviewComment SelectedComment => _comment;
        public string AttachmentLabel => _comment == null ? string.Empty : App.Text("Reviews.Attachment." + _comment.Anchor.Attachment);
        public bool ShowAnchorLabel => !_fileCommentsOnly;
        public string AnchorLabel => _comment != null && _comment.Anchor.Attachment != Models.ReviewAttachment.Attached ?
            App.Text("Reviews.OriginalLocation", FormatAnchorLabel(_comment.OriginAnchor)) : FormatAnchorLabel(ActiveAnchor);

        public string Body
        {
            get => _comment?.Body ?? _body;
            set
            {
                if (Body == value)
                    return;
                _body = value ?? string.Empty;
                if (_comment == null && _draft != null && !string.IsNullOrWhiteSpace(_body))
                {
                    _comment = new Models.ReviewComment
                    {
                        Body = _body,
                        State = _state,
                        Anchor = _draft.Copy(),
                        OriginAnchor = _draft.Copy(),
                        OriginSnapshot = _owner.CurrentSnapshot.Id,
                        OriginPath = _draft.Side == Models.ReviewSide.Old && _file.OldPath.Length > 0 ? _file.OldPath : _file.Path,
                    };
                    _owner.AddComment(_file, _comment);
                    _owner.SelectComment(_comment);
                }
                else if (_comment != null)
                {
                    _comment.Body = _body;
                    _owner.NotifyFileChanged(_file);
                }
                OnPropertyChanged();
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
                    _owner.NotifyFileChanged(_file);
                }
                OnPropertyChanged();
            }
        }

        public ReviewDiffContext(Reviews owner, Models.ReviewFile file, Models.ReviewFile sourceFile, bool fileCommentsOnly = false)
        {
            _owner = owner;
            _file = file;
            _sourceFile = sourceFile;
            _fileCommentsOnly = fileCommentsOnly;
        }

        public void BeginFileComment()
        {
            var existing = _file.Comments.Find(x => x.Anchor.Side == Models.ReviewSide.File);
            if (existing != null)
            {
                EditComment(existing);
                return;
            }
            BeginComment(new Models.ReviewAnchor
            {
                Side = Models.ReviewSide.File,
                Attachment = _file.IsOutsideDiff ? Models.ReviewAttachment.OutsideDiff : Models.ReviewAttachment.Attached,
            });
        }

        public void BeginComment(Models.ReviewAnchor anchor)
        {
            BeginComment(anchor, true);
        }

        public void EditComment(Models.ReviewComment comment)
        {
            EditComment(comment, true);
        }

        public void DropEmpty()
        {
            if (_comment == null)
            {
                if (_draft == null)
                    return;
                _draft = null;
                Refresh();
                return;
            }
            if (!string.IsNullOrWhiteSpace(_comment.Body))
                return;
            _owner.RemoveComment(_file, _comment);
            _comment = null;
            _draft = null;
            Refresh();
        }

        public void NotifySelectionMoved(TextDiffSelectedChunk chunk, bool hasSelection)
        {
            _owner.SyncCommentTarget(this, chunk, hasSelection);
        }

        private void BeginComment(Models.ReviewAnchor anchor, bool activate)
        {
            DropEmptyComment();
            _comment = null;
            _draft = anchor;
            _body = string.Empty;
            _state = Models.ReviewCommentState.ToFix;
            if (!_fileCommentsOnly)
                _owner.SelectComment(null);
            Refresh();
            if (activate)
            {
                EditorOpened?.Invoke(this, EventArgs.Empty);
                _owner.NotifyEditorOpened(this);
            }
        }

        private void EditComment(Models.ReviewComment comment, bool activate)
        {
            if (_fileCommentsOnly && comment != null && comment.Anchor.Side != Models.ReviewSide.File)
                return;
            DropEmptyComment();
            _comment = comment;
            _draft = null;
            _owner.SelectComment(comment);
            Refresh();
            if (activate && comment != null)
            {
                EditorOpened?.Invoke(this, EventArgs.Empty);
                _owner.NotifyEditorOpened(this);
            }
        }

        private void DropEmptyComment()
        {
            if (_comment == null || !string.IsNullOrWhiteSpace(_comment.Body))
                return;
            _owner.RemoveComment(_file, _comment);
            _comment = null;
        }

        public void CloseEditor()
        {
            if (_comment != null && string.IsNullOrWhiteSpace(_comment.Body))
                _owner.RemoveComment(_file, _comment);
            _comment = null;
            _draft = null;
            if (!_fileCommentsOnly)
            {
                _owner.ClearCommentSelection();
                _owner.CancelReattach();
            }
            Refresh();
            _owner.RefreshFileFilter();
        }

        public void BeginReattach()
        {
            if (_comment != null)
            {
                _owner.BeginReattach(_comment);
                CloseForReattach();
            }
        }

        private void CloseForReattach()
        {
            _comment = null;
            _draft = null;
            _owner.ClearCommentSelection();
            Refresh();
        }

        public async System.Threading.Tasks.Task OpenOriginal()
        {
            if (_comment != null)
                await _owner.ShowOriginalAsync(_comment);
        }

        public void SetCommentTarget(Models.ReviewComment comment, bool hasSelection)
        {
            if (ReferenceEquals(_hoveredComment, comment) && _hasSelection == hasSelection)
                return;
            _hoveredComment = comment;
            _hasSelection = hasSelection;
            OnPropertyChanged(nameof(CommentActionLabel));
        }

        public void OpenComment(Models.TextDiff data, TextDiffSelectedChunk chunk, bool hasSelection)
        {
            OpenComment(data, chunk, hasSelection, true);
        }

        public void TrackComment(Models.TextDiff data, TextDiffSelectedChunk chunk, bool hasSelection)
        {
            OpenComment(data, chunk, hasSelection, false);
        }

        private void OpenComment(Models.TextDiff data, TextDiffSelectedChunk chunk, bool hasSelection, bool activate)
        {
            if (_fileCommentsOnly)
                return;
            if (!hasSelection && !_owner.HasPendingReattach && chunk.StartIdx == chunk.EndIdx && chunk.StartIdx >= 0 && chunk.StartIdx < data.Lines.Count)
            {
                var pane = chunk.Combined ? ReviewPane.Combined : chunk.IsOldSide ? ReviewPane.Old : ReviewPane.New;
                var existing = FindComment(data.Lines[chunk.StartIdx], pane);
                if (existing != null)
                {
                    EditComment(existing, activate);
                    return;
                }
            }
            AcceptSelection(data, chunk, activate);
        }

        public void AcceptSelection(Models.TextDiff data, TextDiffSelectedChunk chunk)
        {
            AcceptSelection(data, chunk, true);
        }

        private void AcceptSelection(Models.TextDiff data, TextDiffSelectedChunk chunk, bool activate)
        {
            var oldStart = int.MaxValue;
            var newStart = int.MaxValue;
            var oldEnd = 0;
            var newEnd = 0;
            var hasAdded = false;
            var hasDeleted = false;
            for (var i = Math.Max(0, chunk.StartIdx); i <= chunk.EndIdx && i < data.Lines.Count; i++)
            {
                var line = data.Lines[i];
                hasAdded |= line.Type == Models.TextDiffLineType.Added;
                hasDeleted |= line.Type == Models.TextDiffLineType.Deleted;
                if (line.OldLineNumber > 0)
                {
                    oldStart = Math.Min(oldStart, line.OldLineNumber);
                    oldEnd = Math.Max(oldEnd, line.OldLineNumber);
                }
                if (line.NewLineNumber > 0)
                {
                    newStart = Math.Min(newStart, line.NewLineNumber);
                    newEnd = Math.Max(newEnd, line.NewLineNumber);
                }
            }
            var side = chunk.Combined ?
                hasAdded ? Models.ReviewSide.New : hasDeleted ? Models.ReviewSide.Old : newEnd > 0 ? Models.ReviewSide.New : Models.ReviewSide.Old :
                chunk.IsOldSide ? Models.ReviewSide.Old : Models.ReviewSide.New;
            var start = side == Models.ReviewSide.Old ? oldStart : newStart;
            var end = side == Models.ReviewSide.Old ? oldEnd : newEnd;
            if (end == 0)
                return;

            var hash = side == Models.ReviewSide.Old ? _sourceFile.OldContent : _sourceFile.NewContent;
            var selected = data.Lines.FindAll(x => x.Type is not (Models.TextDiffLineType.Indicator or Models.TextDiffLineType.None) &&
                (side == Models.ReviewSide.Old ? x.OldLineNumber : x.NewLineNumber) >= start &&
                (side == Models.ReviewSide.Old ? x.OldLineNumber : x.NewLineNumber) <= end);
            if (start < 1 || selected.Count == 0)
                return;

            var anchor = new Models.ReviewAnchor
            {
                Side = side,
                StartLine = start,
                EndLine = end,
                OldStartLine = oldEnd > 0 ? oldStart : 0,
                OldEndLine = oldEnd,
                NewStartLine = newEnd > 0 ? newStart : 0,
                NewEndLine = newEnd,
                Quote = string.Join("\n", selected.ConvertAll(x => x.Content)),
            };
            Models.ReviewPatch.CaptureAnchorContext(anchor, _sourceFile.Patch,
                selected.Count == end - start + 1 ? _owner.ReadContent(hash) : null);
            if (_owner.HasPendingReattach)
            {
                var comment = _owner.ReattachComment(_file, anchor);
                EditComment(comment, activate);
            }
            else
            {
                BeginComment(anchor, activate);
            }
        }

        public Models.ReviewComment FindComment(Models.TextDiffLine line, ReviewPane pane)
        {
            return _file.Comments.Find(x => ContainsLine(x.Anchor, line, pane));
        }

        public bool IsSelectedLine(Models.TextDiffLine line, ReviewPane pane)
        {
            var anchor = _comment?.Anchor ?? _draft;
            return anchor != null && ContainsLine(anchor, line, pane);
        }

        private static bool ContainsLine(Models.ReviewAnchor anchor, Models.TextDiffLine line, ReviewPane pane)
        {
            if (anchor.Side == Models.ReviewSide.File || anchor.Attachment != Models.ReviewAttachment.Attached)
                return false;
            if (line.Type is Models.TextDiffLineType.None or Models.TextDiffLineType.Indicator)
                return false;
            var oldStart = anchor.OldEndLine > 0 ? anchor.OldStartLine : anchor.Side == Models.ReviewSide.Old ? anchor.StartLine : 0;
            var oldEnd = anchor.OldEndLine > 0 ? anchor.OldEndLine : anchor.Side == Models.ReviewSide.Old ? anchor.EndLine : 0;
            var newStart = anchor.NewEndLine > 0 ? anchor.NewStartLine : anchor.Side == Models.ReviewSide.New ? anchor.StartLine : 0;
            var newEnd = anchor.NewEndLine > 0 ? anchor.NewEndLine : anchor.Side == Models.ReviewSide.New ? anchor.EndLine : 0;
            var oldMatch = line.OldLineNumber > 0 && line.OldLineNumber >= oldStart && line.OldLineNumber <= oldEnd;
            var newMatch = line.NewLineNumber > 0 && line.NewLineNumber >= newStart && line.NewLineNumber <= newEnd;
            return pane == ReviewPane.Old ? oldMatch : pane == ReviewPane.New ? newMatch : oldMatch || newMatch;
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(Body));
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(CanReattach));
            OnPropertyChanged(nameof(CanOpenOriginal));
            OnPropertyChanged(nameof(SelectedComment));
            OnPropertyChanged(nameof(AnchorLabel));
            OnPropertyChanged(nameof(ActiveAnchor));
            OnPropertyChanged(nameof(AttachmentLabel));
            OnPropertyChanged(nameof(CommentActionLabel));
        }

        public static string FormatAnchorLabel(Models.ReviewAnchor anchor)
        {
            if (anchor == null)
                return string.Empty;
            if (anchor.Side == Models.ReviewSide.File)
                return App.Text("Reviews.FileComment");
            var oldEnd = anchor.OldEndLine > 0 ? anchor.OldEndLine : anchor.Side == Models.ReviewSide.Old ? anchor.EndLine : 0;
            var newEnd = anchor.NewEndLine > 0 ? anchor.NewEndLine : anchor.Side == Models.ReviewSide.New ? anchor.EndLine : 0;
            var oldStart = anchor.OldEndLine > 0 ? anchor.OldStartLine : anchor.StartLine;
            var newStart = anchor.NewEndLine > 0 ? anchor.NewStartLine : anchor.StartLine;
            if (oldEnd > 0 && newEnd > 0)
                return $"Old: {oldStart}-{oldEnd}, New: {newStart}-{newEnd}";
            return oldEnd > 0 ? $"Old: {oldStart}-{oldEnd}" : $"New: {newStart}-{newEnd}";
        }
    }
}
