using System.Text;

namespace SourceGit.Models
{
    public static class ReviewExport
    {
        public static string CreateText(Review review, ReviewSnapshot snapshot)
        {
            var result = new StringBuilder();
            result.Append("Review: ").AppendLine(review.Title);
            result.Append("Snapshot: ").AppendLine(snapshot.Id);
            result.Append("Source: ").AppendLine(snapshot.Source.ToString());
            result.Append("Comparison: ").Append(snapshot.BaseRevision).Append(" -> ").AppendLine(snapshot.TargetRevision);
            result.AppendLine().AppendLine("General review:").AppendLine(review.GeneralText);
            foreach (var file in snapshot.Files)
            {
                result.AppendLine().Append("File: ").AppendLine(file.Path);
                if (file.Patch.Length > 0)
                    AppendBlock(result, "diff", file.Patch);

                foreach (var comment in file.Comments)
                {
                    if (comment.State is ReviewCommentState.Resolved or ReviewCommentState.Dismissed)
                        continue;

                    result.AppendLine().Append("Comment ").Append(comment.Id).Append(" [")
                        .Append(comment.State.ToString().ToLowerInvariant()).AppendLine("]:");
                    if (comment.Anchor.Attachment == ReviewAttachment.Attached)
                    {
                        var path = comment.Anchor.Side == ReviewSide.Old && file.OldPath.Length > 0 ? file.OldPath : file.Path;
                        AppendLocation(result, path, comment.Anchor);
                    }
                    else
                    {
                        result.Append("Attachment: ").AppendLine(comment.Anchor.Attachment.ToString());
                        result.AppendLine("This comment is not attached to the current diff. Verify its applicability before changing code.");
                        result.Append("Original snapshot: ").AppendLine(comment.OriginSnapshot);
                        AppendLocation(result, comment.OriginPath, comment.OriginAnchor);
                        AppendBlock(result, "text", comment.OriginAnchor.Quote);
                    }
                    AppendBlock(result, "text", comment.Body);
                }
            }
            return result.ToString();
        }

        private static void AppendLocation(StringBuilder result, string path, ReviewAnchor anchor)
        {
            result.Append("Location: ").Append(path);
            if (anchor.Side != ReviewSide.File)
                result.Append(" [").Append(anchor.Side).Append(" lines ").Append(anchor.StartLine).Append('-').Append(anchor.EndLine).Append(']');
            result.AppendLine();
        }

        private static void AppendBlock(StringBuilder result, string language, string text)
        {
            var fence = "```";
            while (text.Contains(fence, System.StringComparison.Ordinal))
                fence += "`";
            result.Append(fence).AppendLine(language).Append(text);
            if (!text.EndsWith('\n'))
                result.AppendLine();
            result.AppendLine(fence);
        }
    }
}
