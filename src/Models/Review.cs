using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.Models
{
    public enum ReviewSourceKind
    {
        Commit,
        Comparison,
        Staged,
        Unstaged,
        AllLocal,
    }

    public enum ReviewCommentState
    {
        ToReview,
        Todo,
        Resolved,
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

    public enum ReviewFileProgress
    {
        Unreviewed,
        Reviewed,
        Changed,
    }

    public class ReviewAnchor
    {
        public ReviewSide Side { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public int OldStartLine { get; set; }
        public int OldEndLine { get; set; }
        public int NewStartLine { get; set; }
        public int NewEndLine { get; set; }
        public ReviewAttachment Attachment { get; set; }
        public string Quote { get; set; } = string.Empty;

        public ReviewAnchor Copy()
        {
            return (ReviewAnchor)MemberwiseClone();
        }
    }

    public class ReviewComment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Body { get; set; } = string.Empty;
        public ReviewCommentState State { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public string OriginSnapshot { get; set; } = string.Empty;
        public string OriginPath { get; set; } = string.Empty;
        public ReviewAnchor OriginAnchor { get; set; } = new();
        public ReviewAnchor Anchor { get; set; } = new();

        public ReviewComment Copy()
        {
            var result = (ReviewComment)MemberwiseClone();
            result.Anchor = Anchor.Copy();
            result.OriginAnchor = OriginAnchor.Copy();
            return result;
        }
    }

    public class ReviewFile
    {
        public string Path { get; set; } = string.Empty;
        public string OldPath { get; set; } = string.Empty;
        public string Patch { get; set; } = string.Empty;
        public string OldContent { get; set; } = string.Empty;
        public string NewContent { get; set; } = string.Empty;
        public bool IsBinary { get; set; }
        public bool IsOutsideDiff { get; set; }
        public ReviewFileProgress Progress { get; set; }
        public List<ReviewComment> Comments { get; set; } = [];
        public long ChangeVersion { get; private set; }
        public long SavedVersion { get; set; } = -1;

        public void MarkChanged()
        {
            ChangeVersion++;
        }
    }

    public class ReviewSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public ReviewSourceKind Source { get; set; }
        public string BaseRevision { get; set; } = string.Empty;
        public string TargetRevision { get; set; } = string.Empty;
        public string SourceFingerprint { get; set; } = string.Empty;
        public List<ReviewFile> Files { get; set; } = [];
        public Dictionary<string, string> Contents { get; } = [];
        public bool IsLoaded { get; set; }

        public void ComputeId()
        {
            Id = ComputeContentId();
        }

        public string ComputeContentId()
        {
            var builder = new StringBuilder();
            builder.Append(Source).Append('\0').Append(BaseRevision).Append('\0').Append(TargetRevision).Append('\0');
            foreach (var file in Files)
            {
                if (file.IsOutsideDiff)
                    continue;

                builder.Append(file.Path).Append('\0').Append(file.OldPath).Append('\0')
                    .Append(file.OldContent).Append('\0').Append(file.NewContent).Append('\0')
                    .Append(file.IsBinary).Append('\0').Append(file.Patch).Append('\0');
            }
            return Review.ComputeHash(builder.ToString());
        }
    }

    public class Review : ObservableObject
    {
        public const string DirectoryName = "_reviews";
        private string _title = string.Empty;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get => _title; set => SetProperty(ref _title, value); }
        public string GeneralText { get; set; } = string.Empty;
        public string LastSnapshotId { get; set; } = string.Empty;
        public List<ReviewSnapshot> Snapshots { get; set; } = [];
        public long ChangeVersion { get; private set; }
        public long SavedVersion { get; set; } = -1;

        public void MarkChanged()
        {
            ChangeVersion++;
        }

        public static string ComputeHash(string text)
        {
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }

        public static bool IsReviewPath(string path)
        {
            return path.Equals(DirectoryName, StringComparison.Ordinal) ||
                path.StartsWith(DirectoryName + "/", StringComparison.Ordinal);
        }
    }
}
