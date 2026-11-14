using System;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public enum ReviewPane
    {
        Combined,
        Old,
        New,
    }

    public sealed class ReviewFileItem(Models.ReviewFile file) : ObservableObject
    {
        public Models.ReviewFile Data { get; } = file;
        public string Path => Data.Path;
        public string Progress => Data.IsOutsideDiff ? App.Text("Reviews.Attachment.OutsideDiff") : App.Text("Reviews.Progress." + Data.Progress);
        public string CommentCount => App.Text("Reviews.CommentCounts", Data.Comments.Count,
            Data.Comments.FindAll(x => x.State == Models.ReviewCommentState.ToReview).Count,
            Data.Comments.FindAll(x => x.State == Models.ReviewCommentState.Todo).Count);

        public void Refresh()
        {
            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(CommentCount));
        }
    }

    public sealed class ReviewCommentItem(Models.ReviewComment comment) : ObservableObject
    {
        public Models.ReviewComment Data { get; } = comment;
        public string Body => Data.Body;
        public string State => App.Text("Reviews.State." + Data.State);
        public string Attachment => App.Text("Reviews.Attachment." + Data.Anchor.Attachment);
        public string Location
        {
            get
            {
                var anchor = Data.Anchor.Attachment == Models.ReviewAttachment.Attached ? Data.Anchor : Data.OriginAnchor;
                var location = anchor.Side == Models.ReviewSide.File ? App.Text("Reviews.FileComment") : $"{anchor.Side}: {anchor.StartLine}-{anchor.EndLine}";
                return Data.Anchor.Attachment == Models.ReviewAttachment.Attached ? location : App.Text("Reviews.OriginalLocation", location);
            }
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(Body));
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(Attachment));
            OnPropertyChanged(nameof(Location));
        }
    }

    public sealed class ReviewDiffContext : ObservableObject
    {
        private readonly Reviews _owner;
        private readonly Models.ReviewFile _file;
        private Models.ReviewComment _comment;
        private Models.ReviewAnchor _draft;
        private string _body = string.Empty;
        private Models.ReviewCommentState _state;

        public Models.ReviewCommentState[] StateChoices { get; } = Enum.GetValues<Models.ReviewCommentState>();
        public bool IsEditing => _comment != null || _draft != null;
        public bool CanReattach => _comment != null;
        public bool CanOpenOriginal => _comment != null;
        public string CommentActionLabel => App.Text(_owner.HasPendingReattach ? "Reviews.AttachComment" : "Reviews.AddComment");
        public Models.ReviewComment SelectedComment => _comment;
        public string AttachmentLabel => _comment == null ? string.Empty : App.Text("Reviews.Attachment." + _comment.Anchor.Attachment);
        public string AnchorLabel => _comment != null && _comment.Anchor.Attachment != Models.ReviewAttachment.Attached ?
            App.Text("Reviews.OriginalLocation", GetAnchorLabel(_comment.OriginAnchor)) : GetAnchorLabel(_comment?.Anchor ?? _draft);

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

        public ReviewDiffContext(Reviews owner, Models.ReviewFile file)
        {
            _owner = owner;
            _file = file;
        }

        public void BeginFileComment()
        {
            BeginComment(new Models.ReviewAnchor
            {
                Side = Models.ReviewSide.File,
                Attachment = _file.IsOutsideDiff ? Models.ReviewAttachment.OutsideDiff : Models.ReviewAttachment.Attached,
            });
        }

        public void BeginComment(Models.ReviewAnchor anchor)
        {
            _comment = null;
            _draft = anchor;
            _body = string.Empty;
            _state = Models.ReviewCommentState.ToReview;
            _owner.SelectComment(null);
            Refresh();
        }

        public void EditComment(Models.ReviewComment comment)
        {
            _comment = comment;
            _draft = null;
            _owner.SelectComment(comment);
            Refresh();
        }

        public void CloseEditor()
        {
            _comment = null;
            _draft = null;
            _owner.ClearCommentSelection();
            _owner.CancelReattach();
            Refresh();
        }

        public void BeginReattach()
        {
            if (_comment != null)
                _owner.BeginReattach(_comment);
        }

        public async System.Threading.Tasks.Task OpenOriginal()
        {
            if (_comment != null)
                await _owner.ShowOriginalAsync(_comment);
        }

        public void AcceptSelection(Models.TextDiff data, TextDiffSelectedChunk chunk)
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

            var hash = side == Models.ReviewSide.Old ? _file.OldContent : _file.NewContent;
            var lines = Models.ReviewPatch.SplitLines(_owner.ReadContent(hash));
            if (start < 1 || end > lines.Length)
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
                Quote = string.Join("\n", lines, start - 1, end - start + 1),
            };
            if (_owner.HasPendingReattach)
            {
                var comment = _owner.ReattachComment(_file, anchor);
                EditComment(comment);
            }
            else
            {
                BeginComment(anchor);
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
            var number = anchor.Side == Models.ReviewSide.Old ? line.OldLineNumber : line.NewLineNumber;
            if (line.Type == Models.TextDiffLineType.None ||
                (pane == ReviewPane.Old && anchor.Side != Models.ReviewSide.Old) ||
                (pane == ReviewPane.New && anchor.Side != Models.ReviewSide.New))
                return false;
            return number >= anchor.StartLine && number <= anchor.EndLine;
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
            OnPropertyChanged(nameof(AttachmentLabel));
            OnPropertyChanged(nameof(CommentActionLabel));
        }

        private static string GetAnchorLabel(Models.ReviewAnchor anchor)
        {
            if (anchor == null)
                return string.Empty;
            return anchor.Side == Models.ReviewSide.File ? App.Text("Reviews.FileComment") :
                $"{anchor.Side}: {anchor.StartLine}-{anchor.EndLine}";
        }
    }
}
