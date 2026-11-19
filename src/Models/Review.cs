using System;
using System.Collections.Generic;

namespace SourceGit.Models
{
    public enum ReviewSourceKind
    {
        Local,
        Commit,
        Comparison,
    }

    public enum ReviewCommentState
    {
        Comment,
        ToFix,
        Fixed,
        Dismissed,
    }

    public enum ReviewAttachment
    {
        Attached,
        NeedsAttention,
        OutsideDiff,
    }

    public enum ReviewSide
    {
        File,
        Old,
        New,
    }

    public class ReviewAnchor
    {
        public ReviewSide Side { get; set; }
        public int OldStartLine { get; set; }
        public int OldEndLine { get; set; }
        public int NewStartLine { get; set; }
        public int NewEndLine { get; set; }
        public ReviewAttachment Attachment { get; set; }
        public string Quote { get; set; } = string.Empty;
        public string ContextText { get; set; } = string.Empty;
        public int ContextStartLine { get; set; }
        public string ContextPatch { get; set; } = string.Empty;

        public int StartLine => Side == ReviewSide.Old ? OldStartLine : NewStartLine;
        public int EndLine => Side == ReviewSide.Old ? OldEndLine : NewEndLine;
        public bool HasOldRange => Side != ReviewSide.File && OldEndLine > 0;
        public bool HasNewRange => Side != ReviewSide.File && NewEndLine > 0;

        public ReviewAnchor Copy()
        {
            return (ReviewAnchor)MemberwiseClone();
        }
    }

    public class ReviewComment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Body { get; set; } = string.Empty;
        public ReviewCommentState State { get; set; } = ReviewCommentState.ToFix;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public ReviewAnchor Anchor { get; set; } = new();

        public bool HasBody => !string.IsNullOrWhiteSpace(Body);
    }

    public class ReviewFile
    {
        public string Path { get; set; } = string.Empty;
        public string OldPath { get; set; } = string.Empty;
        public List<ReviewComment> Comments { get; set; } = [];

        public bool HasReviewWork => Comments.Exists(x => x.HasBody);
        public ReviewComment FileComment => Comments.Find(x => x.Anchor.Side == ReviewSide.File);
    }

    public class Review
    {
        public const string DirectoryName = "_reviews";
        public const string ArchiveDirectoryName = "_toremove/_reviews";

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = string.Empty;
        public string GeneralText { get; set; } = string.Empty;
        public ReviewSourceKind Source { get; set; }
        public string BaseRevision { get; set; } = string.Empty;
        public string TargetRevision { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public List<ReviewFile> Files { get; set; } = [];

        public bool IsSaved { get; set; }
        public bool HasGeneralEdits { get; set; }
        public bool HasContent => HasGeneralEdits || !string.IsNullOrWhiteSpace(GeneralText) || Files.Exists(x => x.HasReviewWork);

        public ReviewFile FindFile(string path)
        {
            return Files.Find(x => x.Path.Equals(path, StringComparison.Ordinal));
        }

        public int CountComments(ReviewCommentState state)
        {
            var count = 0;
            foreach (var file in Files)
                foreach (var comment in file.Comments)
                    if (comment.HasBody && comment.State == state)
                        count++;
            return count;
        }
    }
}
