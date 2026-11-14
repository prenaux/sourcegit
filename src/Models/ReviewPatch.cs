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

        public static string[] SplitLines(string text)
        {
            if (text.Length == 0)
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
