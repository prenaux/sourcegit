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

        public static string CreateText(Review review) => CreateText(review, CommentScope.Active);
        public static string CreateDocument(Review review) => CreateText(review, CommentScope.All);

        private static string CreateText(Review review, CommentScope scope)
        {
            var result = new StringBuilder();
            result.Append("# ").AppendLine(review.Title);
            if (!string.IsNullOrWhiteSpace(review.GeneralText))
                result.AppendLine().AppendLine(review.GeneralText);

            result.AppendLine().Append("Source: ").Append(review.Source);
            if (review.Source != ReviewSourceKind.Local)
                result.Append("; ").Append(review.BaseRevision).Append(" -> ").Append(review.TargetRevision);
            result.AppendLine();

            foreach (var file in review.Files)
            {
                var comments = file.Comments.FindAll(x => x.HasBody &&
                    (scope == CommentScope.All || x.State is ReviewCommentState.Comment or ReviewCommentState.ToFix));
                if (comments.Count == 0)
                    continue;

                result.AppendLine().Append("## ").AppendLine(file.Path);
                foreach (var comment in comments)
                {
                    var anchor = comment.Anchor;
                    result.AppendLine().Append("### ").AppendLine(comment.State.ToString().ToLowerInvariant());
                    result.AppendLine().AppendLine(comment.Body);
                    result.AppendLine().Append("Location: ").Append(anchor.Side == ReviewSide.Old && file.OldPath.Length > 0 ? file.OldPath : file.Path);
                    if (anchor.Side != ReviewSide.File)
                        result.Append(" [").Append(anchor.Side).Append(" lines ").Append(anchor.StartLine).Append('-').Append(anchor.EndLine).Append(']');
                    result.AppendLine();

                    if (anchor.Attachment != ReviewAttachment.Attached)
                    {
                        result.Append("Attachment: ").AppendLine(anchor.Attachment.ToString());
                        result.AppendLine("This comment is not attached to the current diff. The location and code below are its last known context.");
                    }

                    if (anchor.ContextPatch.Length > 0)
                        AppendBlock(result, "diff", anchor.ContextPatch);
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
