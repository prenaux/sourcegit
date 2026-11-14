using System;

namespace SourceGit.Models
{
    public static class ReviewMigration
    {
        private const int AnchorContextLineCount = 2;

        public static void MigrateComments(ReviewSnapshot previous, ReviewSnapshot next, Func<string, string> readContent)
        {
            foreach (var oldFile in previous.Files)
            {
                var matches = next.Files.FindAll(x => !x.IsOutsideDiff &&
                    (x.Path == oldFile.Path || x.OldPath == oldFile.Path));
                var file = matches.Count == 1 ? matches[0] : null;
                if (file == null)
                {
                    if (oldFile.Comments.Count == 0)
                        continue;

                    file = new ReviewFile
                    {
                        Path = oldFile.Path,
                        OldPath = oldFile.OldPath,
                        OldContent = oldFile.OldContent,
                        NewContent = oldFile.NewContent,
                        IsOutsideDiff = true,
                    };
                    next.Files.Add(file);
                }
                else if (oldFile.Progress != ReviewFileProgress.Unreviewed)
                {
                    file.Progress = oldFile.Path == file.Path && oldFile.OldContent == file.OldContent &&
                        oldFile.NewContent == file.NewContent && oldFile.IsBinary == file.IsBinary &&
                        (file.OldContent.Length > 0 || file.NewContent.Length > 0 || oldFile.SourceIdentity == file.SourceIdentity) ?
                        oldFile.Progress : ReviewFileProgress.Changed;
                }

                foreach (var original in oldFile.Comments)
                {
                    if (file.Comments.Exists(x => x.Id == original.Id))
                        continue;

                    var comment = original.Copy();
                    var anchor = comment.Anchor;
                    if (file.IsOutsideDiff)
                    {
                        anchor.Attachment = ReviewAttachment.OutsideDiff;
                    }
                    else if (anchor.Side == ReviewSide.File)
                    {
                        anchor.Attachment = ReviewAttachment.Attached;
                    }
                    else
                    {
                        MigrateAnchor(oldFile, file, anchor, readContent);
                    }
                    file.Comments.Add(comment);
                    file.MarkChanged();
                }
            }
        }

        private static void MigrateAnchor(ReviewFile previous, ReviewFile next, ReviewAnchor anchor, Func<string, string> readContent)
        {
            var oldHash = anchor.Side == ReviewSide.Old ? previous.OldContent : previous.NewContent;
            var newHash = anchor.Side == ReviewSide.Old ? next.OldContent : next.NewContent;
            if (string.IsNullOrEmpty(oldHash) || string.IsNullOrEmpty(newHash))
            {
                anchor.Attachment = ReviewAttachment.NeedsAttention;
                return;
            }

            if (oldHash != newHash)
            {
                var originalText = readContent(oldHash);
                var nextText = readContent(newHash);
                var oldLines = ReviewPatch.SplitLines(originalText ?? anchor.ContextText);
                var newLines = ReviewPatch.SplitLines(nextText);
                var offset = originalText == null ? anchor.ContextStartLine - 1 : 0;
                if (nextText == null || (originalText == null && !anchor.CanAutoMap) ||
                    !TryMapRange(oldLines, newLines, anchor.StartLine - offset, anchor.EndLine - offset, out var start))
                {
                    anchor.Attachment = ReviewAttachment.NeedsAttention;
                    return;
                }
                anchor.EndLine = start + anchor.EndLine - anchor.StartLine;
                anchor.StartLine = start;
            }

            anchor.OldStartLine = anchor.Side == ReviewSide.Old ? anchor.StartLine : 0;
            anchor.OldEndLine = anchor.Side == ReviewSide.Old ? anchor.EndLine : 0;
            anchor.NewStartLine = anchor.Side == ReviewSide.New ? anchor.StartLine : 0;
            anchor.NewEndLine = anchor.Side == ReviewSide.New ? anchor.EndLine : 0;
            anchor.Attachment = ReviewPatch.ContainsLine(next.Patch, anchor.Side, anchor.StartLine) &&
                ReviewPatch.ContainsLine(next.Patch, anchor.Side, anchor.EndLine) ?
                ReviewAttachment.Attached : ReviewAttachment.OutsideDiff;
            ReviewPatch.CaptureAnchorContext(anchor, next.Patch, readContent(newHash));
        }

        public static bool TryMapRange(string[] oldLines, string[] newLines, int startLine, int endLine, out int mappedStart)
        {
            mappedStart = 0;
            if (startLine < 1 || endLine < startLine || endLine > oldLines.Length)
                return false;

            var windowStart = Math.Max(0, startLine - 1 - AnchorContextLineCount);
            var windowEnd = Math.Min(oldLines.Length, endLine + AnchorContextLineCount);
            var length = windowEnd - windowStart;
            if (FindUniqueWindow(oldLines, oldLines, windowStart, length) != windowStart)
                return false;

            var found = FindUniqueWindow(newLines, oldLines, windowStart, length);
            if (found >= 0)
            {
                mappedStart = found + startLine - windowStart;
                return true;
            }

            var startIndex = startLine - 1;
            var count = endLine - startLine + 1;
            if (FindUniqueWindow(oldLines, oldLines, startIndex, count) != startIndex)
                return false;
            found = FindUniqueWindow(newLines, oldLines, startIndex, count);
            if (found < 0)
                return false;

            var prefix = 0;
            while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix])
                prefix++;
            var suffix = 0;
            while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix &&
                oldLines[oldLines.Length - suffix - 1] == newLines[newLines.Length - suffix - 1])
                suffix++;

            if ((endLine <= prefix && found == startIndex) ||
                (startIndex >= oldLines.Length - suffix && found == newLines.Length - oldLines.Length + startIndex))
            {
                mappedStart = found + 1;
                return true;
            }
            return false;
        }

        private static int FindUniqueWindow(string[] target, string[] source, int start, int length)
        {
            var found = -1;
            for (var i = 0; i <= target.Length - length; i++)
            {
                var matches = true;
                for (var j = 0; j < length; j++)
                {
                    if (target[i + j] == source[start + j])
                        continue;

                    matches = false;
                    break;
                }
                if (!matches)
                    continue;
                if (found >= 0)
                    return -1;

                found = i;
            }
            return found;
        }
    }
}
