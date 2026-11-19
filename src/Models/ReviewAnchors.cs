using System;
using System.Collections.Generic;
using System.Text;

namespace SourceGit.Models
{
    public enum ReviewPane
    {
        Combined,
        Old,
        New,
    }

    public enum ReviewAttachPolicy
    {
        // The diff shows the only current version of the file. A comment that is not found loses its attachment.
        Authoritative,
        // Another diff can still show the commented code. A comment that is not found keeps its recorded state.
        Partial,
    }

    public static class ReviewAnchors
    {
        public const int ContextLineCount = 4;

        private readonly record struct SideRow(int Number, string Content, int Index);

        public static ReviewAnchor CreateAnchor(TextDiff diff, int first, int last, ReviewPane pane)
        {
            var oldStart = int.MaxValue;
            var newStart = int.MaxValue;
            var oldEnd = 0;
            var newEnd = 0;
            var hasAdded = false;
            var hasDeleted = false;
            for (var i = Math.Max(0, first); i <= last && i < diff.Lines.Count; i++)
            {
                var line = diff.Lines[i];
                if (line.Type is TextDiffLineType.Indicator or TextDiffLineType.None ||
                    (pane == ReviewPane.Old && line.Type == TextDiffLineType.Added) ||
                    (pane == ReviewPane.New && line.Type == TextDiffLineType.Deleted))
                    continue;

                hasAdded |= line.Type == TextDiffLineType.Added;
                hasDeleted |= line.Type == TextDiffLineType.Deleted;
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

            var side = pane switch
            {
                ReviewPane.Old => ReviewSide.Old,
                ReviewPane.New => ReviewSide.New,
                _ => hasAdded || !hasDeleted ? ReviewSide.New : ReviewSide.Old,
            };
            if ((side == ReviewSide.Old ? oldEnd : newEnd) == 0)
                return null;

            var anchor = new ReviewAnchor { Side = side };
            if (side == ReviewSide.Old || (pane == ReviewPane.Combined && hasDeleted))
            {
                anchor.OldStartLine = oldStart;
                anchor.OldEndLine = oldEnd;
            }
            if (side == ReviewSide.New)
            {
                anchor.NewStartLine = newStart;
                anchor.NewEndLine = newEnd;
            }
            return anchor;
        }

        // Records the commented lines and the lines around them. A later diff finds the comment again by this code.
        // This reads the whole diff, so the caller does it when a comment is created, not for each caret move.
        public static void CaptureCode(ReviewAnchor anchor, TextDiff diff)
        {
            if (anchor.Side == ReviewSide.File)
                return;

            var quote = new List<string>();
            foreach (var row in CollectSideRows(diff, anchor.Side))
                if (row.Number >= anchor.StartLine && row.Number <= anchor.EndLine)
                    quote.Add(row.Content);
            anchor.Quote = string.Join("\n", quote);
            CaptureContext(anchor, diff);
        }

        public static bool ContainsLine(ReviewAnchor anchor, TextDiffLine line, ReviewPane pane)
        {
            if (anchor.Side == ReviewSide.File || line.Type is TextDiffLineType.None or TextDiffLineType.Indicator)
                return false;

            var oldMatch = anchor.HasOldRange && line.OldLineNumber >= anchor.OldStartLine && line.OldLineNumber <= anchor.OldEndLine;
            var newMatch = anchor.HasNewRange && line.NewLineNumber >= anchor.NewStartLine && line.NewLineNumber <= anchor.NewEndLine;
            return pane switch
            {
                ReviewPane.Old => oldMatch,
                ReviewPane.New => newMatch,
                _ => oldMatch || newMatch,
            };
        }

        public static int FindFirstLineIndex(ReviewAnchor anchor, List<TextDiffLine> lines, ReviewPane pane)
        {
            for (var i = 0; i < lines.Count; i++)
                if (ContainsLine(anchor, lines[i], pane))
                    return i;
            return -1;
        }

        public static int GetFirstLine(ReviewAnchor anchor)
        {
            if (anchor.Side == ReviewSide.File)
                return 0;
            if (anchor.HasOldRange && anchor.HasNewRange)
                return Math.Min(anchor.OldStartLine, anchor.NewStartLine);
            return anchor.StartLine;
        }

        // Finds each line comment of the file in the diff. Adds the found comments to `located`.
        // Returns true when a recorded anchor changed.
        public static bool AttachComments(ReviewFile file, TextDiff diff, ReviewAttachPolicy policy, ICollection<ReviewComment> located)
        {
            var changed = false;
            foreach (var comment in file.Comments)
            {
                var anchor = comment.Anchor;
                if (anchor.Side == ReviewSide.File)
                {
                    located.Add(comment);
                    changed |= SetAttachment(anchor, ReviewAttachment.Attached);
                    continue;
                }

                var rows = CollectSideRows(diff, anchor.Side);
                var start = FindAttachedLine(anchor, rows);
                if (start > 0)
                {
                    located.Add(comment);
                    changed |= MoveAnchor(anchor, start);
                    changed |= SetAttachment(anchor, ReviewAttachment.Attached);
                    changed |= CaptureContext(anchor, diff);
                    continue;
                }

                if (policy == ReviewAttachPolicy.Partial)
                    continue;

                var isDisplayed = rows.Exists(x => x.Number >= anchor.StartLine && x.Number <= anchor.EndLine);
                changed |= SetAttachment(anchor, isDisplayed ? ReviewAttachment.NeedsAttention : ReviewAttachment.OutsideDiff);
            }
            return changed;
        }

        public static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return [];

            var lines = text.Split('\n');
            if (text.EndsWith('\n'))
                Array.Resize(ref lines, lines.Length - 1);
            return lines;
        }

        private static List<SideRow> CollectSideRows(TextDiff diff, ReviewSide side)
        {
            var rows = new List<SideRow>();
            for (var i = 0; i < diff.Lines.Count; i++)
            {
                var line = diff.Lines[i];
                var number = side == ReviewSide.Old ? line.OldLineNumber : line.NewLineNumber;
                if (number > 0 && line.Type is not (TextDiffLineType.Indicator or TextDiffLineType.None))
                    rows.Add(new SideRow(number, line.Content, i));
            }
            return rows;
        }

        private static int FindAttachedLine(ReviewAnchor anchor, List<SideRow> rows)
        {
            var quote = anchor.Quote.Split('\n');
            var context = anchor.ContextStartLine > 0 ? anchor.ContextText.Split('\n') : [];
            var beforeCount = anchor.StartLine - anchor.ContextStartLine;
            if (beforeCount < 0 || beforeCount + quote.Length > context.Length)
            {
                context = [];
                beforeCount = 0;
            }

            var original = -1;
            var unchanged = new List<int>();
            var candidates = new List<int>();
            for (var i = 0; i + quote.Length <= rows.Count; i++)
            {
                if (!MatchesRows(rows, i, quote))
                    continue;

                var hasSameContext = HasSameContext(rows, i, quote.Length, context, beforeCount);
                var number = rows[i].Number;
                if (number == anchor.StartLine)
                {
                    if (hasSameContext)
                        return number;
                    original = number;
                }
                candidates.Add(number);
                if (hasSameContext)
                    unchanged.Add(number);
            }

            if (unchanged.Count == 1)
                return unchanged[0];
            if (unchanged.Count > 1)
                return 0;
            if (candidates.Count == 1)
                return candidates[0];
            return Math.Max(original, 0);
        }

        private static bool MatchesRows(List<SideRow> rows, int index, string[] quote)
        {
            for (var i = 0; i < quote.Length; i++)
            {
                var row = rows[index + i];
                if (row.Number != rows[index].Number + i || !row.Content.Equals(quote[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        // A context line that the diff does not show cannot contradict the match.
        private static bool HasSameContext(List<SideRow> rows, int index, int quoteCount, string[] context, int beforeCount)
        {
            for (var distance = 1; distance <= beforeCount; distance++)
            {
                var at = index - distance;
                if (at < 0 || rows[at].Number != rows[index].Number - distance)
                    break;
                if (!rows[at].Content.Equals(context[beforeCount - distance], StringComparison.Ordinal))
                    return false;
            }

            var last = index + quoteCount - 1;
            for (var distance = 1; beforeCount + quoteCount + distance <= context.Length; distance++)
            {
                var at = last + distance;
                if (at >= rows.Count || rows[at].Number != rows[last].Number + distance)
                    break;
                if (!rows[at].Content.Equals(context[beforeCount + quoteCount + distance - 1], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static bool MoveAnchor(ReviewAnchor anchor, int startLine)
        {
            var delta = startLine - anchor.StartLine;
            if (delta == 0)
                return false;

            var endLine = anchor.EndLine + delta;
            anchor.OldStartLine = anchor.Side == ReviewSide.Old ? startLine : 0;
            anchor.OldEndLine = anchor.Side == ReviewSide.Old ? endLine : 0;
            anchor.NewStartLine = anchor.Side == ReviewSide.New ? startLine : 0;
            anchor.NewEndLine = anchor.Side == ReviewSide.New ? endLine : 0;
            return true;
        }

        private static bool SetAttachment(ReviewAnchor anchor, ReviewAttachment attachment)
        {
            if (anchor.Attachment == attachment)
                return false;

            anchor.Attachment = attachment;
            return true;
        }

        private static bool CaptureContext(ReviewAnchor anchor, TextDiff diff)
        {
            var rows = CollectSideRows(diff, anchor.Side);
            var first = rows.FindIndex(x => x.Number == anchor.StartLine);
            var last = rows.FindLastIndex(x => x.Number == anchor.EndLine);
            if (first < 0 || last < first)
                return false;

            var start = first;
            while (start > 0 && first - start < ContextLineCount && rows[start - 1].Number == rows[start].Number - 1)
                start--;
            var end = last;
            while (end < rows.Count - 1 && end - last < ContextLineCount && rows[end + 1].Number == rows[end].Number + 1)
                end++;

            var context = new List<string>();
            for (var i = start; i <= end; i++)
                context.Add(rows[i].Content);
            var contextText = string.Join("\n", context);

            var firstIndex = rows[first].Index;
            var lastIndex = rows[last].Index;
            for (var i = 0; i < diff.Lines.Count; i++)
            {
                if (!ContainsLine(anchor, diff.Lines[i], ReviewPane.Combined))
                    continue;
                firstIndex = Math.Min(firstIndex, i);
                lastIndex = Math.Max(lastIndex, i);
            }
            var contextPatch = CreateContextPatch(diff, firstIndex, lastIndex);

            if (anchor.ContextStartLine == rows[start].Number && anchor.ContextText == contextText && anchor.ContextPatch == contextPatch)
                return false;

            anchor.ContextStartLine = rows[start].Number;
            anchor.ContextText = contextText;
            anchor.ContextPatch = contextPatch;
            return true;
        }

        // The result is one hunk for a reader. It is not a patch that git can apply.
        private static string CreateContextPatch(TextDiff diff, int first, int last)
        {
            var start = first;
            while (start > 0 && first - start < ContextLineCount && diff.Lines[start - 1].Type != TextDiffLineType.Indicator)
                start--;
            var end = last;
            while (end < diff.Lines.Count - 1 && end - last < ContextLineCount && diff.Lines[end + 1].Type != TextDiffLineType.Indicator)
                end++;

            var oldBefore = 0;
            var newBefore = 0;
            for (var i = 0; i < start; i++)
            {
                if (diff.Lines[i].OldLineNumber > 0)
                    oldBefore = diff.Lines[i].OldLineNumber;
                if (diff.Lines[i].NewLineNumber > 0)
                    newBefore = diff.Lines[i].NewLineNumber;
            }

            var oldStart = 0;
            var newStart = 0;
            var oldCount = 0;
            var newCount = 0;
            var body = new StringBuilder();
            for (var i = start; i <= end; i++)
            {
                var line = diff.Lines[i];
                if (line.Type is TextDiffLineType.Indicator or TextDiffLineType.None)
                    continue;

                if (line.Type != TextDiffLineType.Added)
                {
                    oldStart = oldCount == 0 ? line.OldLineNumber : oldStart;
                    oldCount++;
                }
                if (line.Type != TextDiffLineType.Deleted)
                {
                    newStart = newCount == 0 ? line.NewLineNumber : newStart;
                    newCount++;
                }

                var prefix = line.Type == TextDiffLineType.Added ? '+' : line.Type == TextDiffLineType.Deleted ? '-' : ' ';
                body.Append(prefix).Append(line.Content).Append('\n');
                if (line.NoNewLineEndOfFile)
                    body.Append("\\ No newline at end of file\n");
            }

            var header = new StringBuilder();
            header.Append("@@ -").Append(oldCount == 0 ? oldBefore : oldStart).Append(',').Append(oldCount)
                .Append(" +").Append(newCount == 0 ? newBefore : newStart).Append(',').Append(newCount).Append(" @@\n");
            return header.Append(body).ToString();
        }
    }
}
