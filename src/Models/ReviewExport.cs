using System;
using System.Text;

namespace SourceGit.Models
{
    public static class ReviewExport
    {
        private enum CommentScope
        {
            Active,
            All,
        }

        public static string CreateText(Review review, ReviewSnapshot snapshot) => CreateText(review, snapshot, CommentScope.Active);
        public static string CreateDocument(Review review, ReviewSnapshot snapshot) => CreateText(review, snapshot, CommentScope.All);

        private static string CreateText(Review review, ReviewSnapshot snapshot, CommentScope scope)
        {
            var result = new StringBuilder();
            result.Append("# ").AppendLine(review.Title);
            if (!string.IsNullOrWhiteSpace(review.GeneralText))
                result.AppendLine().AppendLine(review.GeneralText);
            result.AppendLine().Append("Source: ").Append(snapshot.Source).Append("; ")
                .Append(snapshot.BaseRevision).Append(" -> ").AppendLine(snapshot.TargetRevision);
            foreach (var file in snapshot.Files)
            {
                var comments = file.Comments.FindAll(x => !string.IsNullOrWhiteSpace(x.Body) &&
                    (scope == CommentScope.All || x.State is ReviewCommentState.Comment or ReviewCommentState.ToFix));
                if (comments.Count == 0)
                    continue;
                result.AppendLine().Append("## ").AppendLine(file.Path);
                foreach (var comment in comments)
                {
                    result.AppendLine().Append("### ").AppendLine(comment.State.ToString().ToLowerInvariant());
                    result.AppendLine().AppendLine(comment.Body);
                    var attached = comment.Anchor.Attachment == ReviewAttachment.Attached;
                    var anchor = attached ? comment.Anchor : comment.OriginAnchor;
                    var path = attached ? anchor.Side == ReviewSide.Old && file.OldPath.Length > 0 ? file.OldPath : file.Path : comment.OriginPath;
                    result.AppendLine().Append("Location: ").Append(path);
                    if (anchor.Side != ReviewSide.File)
                        result.Append(" [").Append(anchor.Side).Append(" lines ").Append(anchor.StartLine).Append('-').Append(anchor.EndLine).Append(']');
                    result.AppendLine();
                    if (!attached)
                    {
                        result.Append("Attachment: ").AppendLine(comment.Anchor.Attachment.ToString());
                        result.AppendLine("This comment is not attached to the current diff. The location and code below are its original context.");
                        result.Append("Original snapshot: ").AppendLine(comment.OriginSnapshot);
                    }
                    var patch = anchor.ContextPatch.Length > 0 ? anchor.ContextPatch : ReviewPatch.SelectHunks(file.Patch, [anchor]);
                    if (patch.Length > 0)
                        AppendBlock(result, "diff", patch);
                    else if (anchor.Quote.Length > 0)
                        AppendBlock(result, "text", anchor.Quote);
                }
            }
            return result.ToString();
        }

        private static void AppendBlock(StringBuilder result, string language, string text)
        {
            var fence = "```";
            while (text.Contains(fence, StringComparison.Ordinal))
                fence += "`";
            result.AppendLine().Append(fence).AppendLine(language).Append(text);
            if (!text.EndsWith('\n'))
                result.AppendLine();
            result.AppendLine(fence);
        }
    }
}
