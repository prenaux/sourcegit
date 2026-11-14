using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SourceGit.Models
{
    public static partial class ReviewPatch
    {
        public const int ContextLineCount = 4;

        public static string ExtractText(TextDiff diff, ReviewSide side)
        {
            var builder = new StringBuilder();
            foreach (var line in diff.Lines)
            {
                if (line.Type == TextDiffLineType.Indicator || line.Type == TextDiffLineType.None ||
                    (side == ReviewSide.Old && line.Type == TextDiffLineType.Added) ||
                    (side == ReviewSide.New && line.Type == TextDiffLineType.Deleted))
                    continue;

                builder.Append(line.Content);
                if (!line.NoNewLineEndOfFile)
                    builder.Append('\n');
            }
            return builder.ToString();
        }

        public static string CreateContextPatch(string fullPatch, TextDiff diff)
        {
            var headerEnd = fullPatch.IndexOf("\n@@ ", StringComparison.Ordinal);
            if (headerEnd < 0)
                return fullPatch;

            var lines = diff.Lines.FindAll(x => x.Type is TextDiffLineType.Normal or TextDiffLineType.Added or TextDiffLineType.Deleted);
            var ranges = new List<(int Start, int End)>();
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Type == TextDiffLineType.Normal)
                    continue;

                var start = Math.Max(0, i - ContextLineCount);
                var end = Math.Min(lines.Count, i + ContextLineCount + 1);
                if (ranges.Count > 0 && start <= ranges[^1].End)
                    ranges[^1] = (ranges[^1].Start, Math.Max(end, ranges[^1].End));
                else
                    ranges.Add((start, end));
            }

            var result = new StringBuilder(fullPatch[..(headerEnd + 1)]);
            var oldLine = 1;
            var newLine = 1;
            var cursor = 0;
            foreach (var range in ranges)
            {
                while (cursor < range.Start)
                {
                    if (lines[cursor].Type != TextDiffLineType.Added)
                        oldLine++;
                    if (lines[cursor].Type != TextDiffLineType.Deleted)
                        newLine++;
                    cursor++;
                }

                var oldCount = 0;
                var newCount = 0;
                for (var i = range.Start; i < range.End; i++)
                {
                    if (lines[i].Type != TextDiffLineType.Added)
                        oldCount++;
                    if (lines[i].Type != TextDiffLineType.Deleted)
                        newCount++;
                }

                result.Append("@@ -").Append(oldCount == 0 ? oldLine - 1 : oldLine).Append(',').Append(oldCount)
                    .Append(" +").Append(newCount == 0 ? newLine - 1 : newLine).Append(',').Append(newCount).Append(" @@\n");
                for (; cursor < range.End; cursor++)
                {
                    var line = lines[cursor];
                    var prefix = line.Type == TextDiffLineType.Added ? '+' : line.Type == TextDiffLineType.Deleted ? '-' : ' ';
                    result.Append(prefix).Append(line.Content).Append('\n');
                    if (line.NoNewLineEndOfFile)
                        result.Append("\\ No newline at end of file\n");
                    if (line.Type != TextDiffLineType.Added)
                        oldLine++;
                    if (line.Type != TextDiffLineType.Deleted)
                        newLine++;
                }
            }
            return result.ToString();
        }

        public static bool ContainsLine(string patch, ReviewSide side, int line)
        {
            foreach (var text in patch.Split('\n'))
            {
                var match = MatchHunk().Match(text);
                if (!match.Success)
                    continue;

                var offset = side == ReviewSide.Old ? 1 : 3;
                var start = int.Parse(match.Groups[offset].Value);
                var count = match.Groups[offset + 1].Success ? int.Parse(match.Groups[offset + 1].Value) : 1;
                if (line >= start && line < start + count)
                    return true;
            }
            return false;
        }

        public static void CaptureAnchorContext(ReviewAnchor anchor, string patch, string source)
        {
            anchor.ContextPatch = SelectHunks(patch, [anchor]);
            var lines = SplitLines(source);
            if (anchor.Side == ReviewSide.File || anchor.StartLine < 1 || anchor.EndLine > lines.Length)
                return;
            var start = Math.Max(0, anchor.StartLine - 1 - ContextLineCount);
            var end = Math.Min(lines.Length, anchor.EndLine + ContextLineCount);
            anchor.Quote = string.Join("\n", lines, anchor.StartLine - 1, anchor.EndLine - anchor.StartLine + 1);
            anchor.ContextStartLine = start + 1;
            anchor.ContextText = string.Join("\n", lines, start, end - start);
            anchor.CanAutoMap = ReviewMigration.TryMapRange(lines, lines, anchor.StartLine, anchor.EndLine, out _);
        }

        public static string SelectHunks(string patch, IEnumerable<ReviewAnchor> anchors)
        {
            var first = patch.IndexOf("\n@@ ", StringComparison.Ordinal);
            if (first < 0)
                return string.Empty;
            var result = new StringBuilder();
            var start = first + 1;
            while (start < patch.Length)
            {
                var next = patch.IndexOf("\n@@ ", start, StringComparison.Ordinal);
                var end = next < 0 ? patch.Length : next + 1;
                var match = MatchHunk().Match(patch[start..]);
                if (!match.Success)
                    break;
                var oldLine = int.Parse(match.Groups[1].Value);
                var newLine = int.Parse(match.Groups[3].Value);
                if (match.Groups[2].Success && match.Groups[2].Value == "0")
                    oldLine++;
                if (match.Groups[4].Success && match.Groups[4].Value == "0")
                    newLine++;
                var rows = new List<(string Text, char Prefix)>();
                foreach (var text in patch[start..end].Split('\n'))
                {
                    if (text.Length > 0 && text[0] is ' ' or '+' or '-')
                        rows.Add((text, text[0]));
                    else if (text.StartsWith("\\ No newline", StringComparison.Ordinal) && rows.Count > 0)
                        rows[^1] = (rows[^1].Text + "\n" + text, rows[^1].Prefix);
                }
                var ranges = new List<(int Start, int End)>();
                var oldCursor = oldLine;
                var newCursor = newLine;
                for (var i = 0; i < rows.Count; i++)
                {
                    foreach (var anchor in anchors)
                    {
                        if (anchor.Side == ReviewSide.File || anchor.Attachment != ReviewAttachment.Attached)
                            continue;
                        var number = anchor.Side == ReviewSide.Old ? rows[i].Prefix == '+' ? 0 : oldCursor : rows[i].Prefix == '-' ? 0 : newCursor;
                        if (number < anchor.StartLine || number > anchor.EndLine || number == 0)
                            continue;
                        var from = Math.Max(0, i - ContextLineCount);
                        var to = Math.Min(rows.Count, i + ContextLineCount + 1);
                        if (ranges.Count > 0 && from <= ranges[^1].End)
                            ranges[^1] = (ranges[^1].Start, Math.Max(ranges[^1].End, to));
                        else
                            ranges.Add((from, to));
                        break;
                    }
                    if (rows[i].Prefix != '+')
                        oldCursor++;
                    if (rows[i].Prefix != '-')
                        newCursor++;
                }
                var cursor = 0;
                foreach (var range in ranges)
                {
                    while (cursor < range.Start)
                    {
                        if (rows[cursor].Prefix != '+')
                            oldLine++;
                        if (rows[cursor].Prefix != '-')
                            newLine++;
                        cursor++;
                    }
                    var oldCount = 0;
                    var newCount = 0;
                    for (var i = range.Start; i < range.End; i++)
                    {
                        if (rows[i].Prefix != '+')
                            oldCount++;
                        if (rows[i].Prefix != '-')
                            newCount++;
                    }
                    if (result.Length == 0)
                        result.Append(patch.AsSpan(0, first + 1));
                    result.Append("@@ -").Append(oldCount == 0 ? oldLine - 1 : oldLine).Append(',').Append(oldCount)
                        .Append(" +").Append(newCount == 0 ? newLine - 1 : newLine).Append(',').Append(newCount).Append(" @@\n");
                    for (; cursor < range.End; cursor++)
                    {
                        result.Append(rows[cursor].Text).Append('\n');
                        if (rows[cursor].Prefix != '+')
                            oldLine++;
                        if (rows[cursor].Prefix != '-')
                            newLine++;
                    }
                }
                start = end;
            }
            return result.ToString();
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

        [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")]
        private static partial Regex MatchHunk();
    }
}
