using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public sealed class CaptureReview
    {
        private readonly string _repository;
        private const int CaptureAttempts = 2;
        private const int FullContextLines = 999999999;
        private const string ExcludeReviews = "-- . \":(top,exclude)_reviews/**\" \":(top,exclude)_reviews\"";
        private const string DiffFlags = "--no-color --no-ext-diff --no-textconv --no-relative --diff-algorithm=myers --no-indent-heuristic --find-renames --src-prefix=a/ --dst-prefix=b/";

        private enum ExitPolicy
        {
            Success,
            Difference,
            Probe,
        }

        private sealed class Entry
        {
            public string Path { get; set; }
            public string OldPath { get; set; }
            public string Identity { get; set; }
            public bool IsUntracked { get; set; }
        }

        public CaptureReview(string repository)
        {
            _repository = repository;
        }

        public async Task<Models.ReviewSnapshot> CaptureAsync(Models.ReviewSourceKind source, string baseRevision = "", string targetRevision = "")
        {
            for (var attempt = 0; attempt < CaptureAttempts; attempt++)
            {
                var snapshot = await ResolveSnapshotAsync(source, baseRevision, targetRevision);
                var entries = await ReadEntriesAsync(snapshot);
                var before = await ComputeFingerprintAsync(snapshot, entries);
                var selection = GetComparisonArguments(snapshot);
                var raw = await ReadGitAsync($"diff {DiffFlags} --patch --full-index --unified={FullContextLines} {selection} {ExcludeReviews}");
                var patches = SplitFilePatches(raw);
                var patchIndex = 0;
                foreach (var entry in entries)
                {
                    string patch;
                    if (entry.IsUntracked)
                    {
                        patch = await ReadGitAsync($"diff {DiffFlags} --no-index --patch --full-index --unified={FullContextLines} -- /dev/null {entry.Path.Quoted()}", ExitPolicy.Difference);
                    }
                    else
                    {
                        if (patchIndex >= patches.Count)
                            throw new InvalidDataException("The changed-file list and captured patch differ. Retry the capture.");
                        patch = patches[patchIndex++];
                    }

                    var parsed = Diff.ParsePatch(patch);
                    var file = new Models.ReviewFile
                    {
                        Path = entry.Path,
                        OldPath = entry.OldPath,
                        Patch = parsed.TextDiff == null ? patch : Models.ReviewPatch.CreateContextPatch(patch, parsed.TextDiff),
                        IsBinary = parsed.IsBinary || parsed.IsLFS,
                    };
                    if (parsed.TextDiff != null)
                    {
                        var oldText = Models.ReviewPatch.ExtractText(parsed.TextDiff, Models.ReviewSide.Old);
                        var newText = Models.ReviewPatch.ExtractText(parsed.TextDiff, Models.ReviewSide.New);
                        file.OldContent = Models.Review.ComputeHash(oldText);
                        file.NewContent = Models.Review.ComputeHash(newText);
                        snapshot.Contents[file.OldContent] = oldText;
                        snapshot.Contents[file.NewContent] = newText;
                    }
                    snapshot.Files.Add(file);
                }
                if (patchIndex != patches.Count)
                    throw new InvalidDataException("The captured patch contains unexpected files. Retry the capture.");

                var latest = await ResolveSnapshotAsync(source, baseRevision, targetRevision);
                var after = await ComputeFingerprintAsync(latest, await ReadEntriesAsync(latest));
                if (before != after)
                    continue;

                snapshot.SourceFingerprint = before;
                snapshot.IsLoaded = true;
                snapshot.Files.Sort((a, b) => StringComparer.Ordinal.Compare(a.Path, b.Path));
                snapshot.ComputeId();
                return snapshot;
            }
            throw new IOException("The source changed while capturing the review. Retry after the edits or staging operation finish.");
        }

        public async Task<string> ReadSourceFingerprintAsync(Models.ReviewSourceKind source)
        {
            var snapshot = await ResolveSnapshotAsync(source, string.Empty, string.Empty);
            return await ComputeFingerprintAsync(snapshot, await ReadEntriesAsync(snapshot));
        }

        private async Task<Models.ReviewSnapshot> ResolveSnapshotAsync(Models.ReviewSourceKind source, string baseRevision, string targetRevision)
        {
            var snapshot = new Models.ReviewSnapshot { Source = source };
            if (source == Models.ReviewSourceKind.Commit)
            {
                snapshot.TargetRevision = await ResolveRevisionAsync(targetRevision);
                var parents = (await ReadGitAsync($"rev-list --parents -n 1 {snapshot.TargetRevision}")).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                snapshot.BaseRevision = parents.Length > 1 ? parents[1] : Models.EmptyTreeHash.Guess(snapshot.TargetRevision);
                return snapshot;
            }
            if (source == Models.ReviewSourceKind.Comparison)
            {
                snapshot.BaseRevision = await ResolveRevisionAsync(baseRevision);
                snapshot.TargetRevision = await ResolveRevisionAsync(targetRevision);
                return snapshot;
            }

            var head = (await ReadGitAsync("rev-parse --verify HEAD", ExitPolicy.Probe)).Trim();
            if (head.Length == 0)
            {
                var format = (await ReadGitAsync("rev-parse --show-object-format")).Trim();
                head = Models.EmptyTreeHash.Guess(new string('0', format == "sha256" ? 64 : 40));
            }
            snapshot.BaseRevision = source == Models.ReviewSourceKind.Unstaged ? "index" : head;
            snapshot.TargetRevision = source == Models.ReviewSourceKind.Staged ? "index" : "working tree";
            return snapshot;
        }

        private async Task<string> ResolveRevisionAsync(string revision)
        {
            if (string.IsNullOrWhiteSpace(revision))
                throw new ArgumentException("A review revision is required.");
            var value = (await ReadGitAsync($"rev-parse --verify --end-of-options {(revision + "^{commit}").Quoted()}")).Trim();
            if (value.Length != 40 && value.Length != 64)
                throw new InvalidDataException("The review revision did not resolve to an object ID.");
            foreach (var c in value)
                if (!Uri.IsHexDigit(c))
                    throw new InvalidDataException("The review revision is not an object ID.");
            return value;
        }

        private static string GetComparisonArguments(Models.ReviewSnapshot snapshot)
        {
            return snapshot.Source switch
            {
                Models.ReviewSourceKind.Staged => $"--cached {snapshot.BaseRevision}",
                Models.ReviewSourceKind.Unstaged => string.Empty,
                Models.ReviewSourceKind.AllLocal => snapshot.BaseRevision,
                _ => $"{snapshot.BaseRevision} {snapshot.TargetRevision}",
            };
        }

        private async Task<List<Entry>> ReadEntriesAsync(Models.ReviewSnapshot snapshot)
        {
            if (snapshot.Source is Models.ReviewSourceKind.Staged or Models.ReviewSourceKind.Unstaged or Models.ReviewSourceKind.AllLocal)
            {
                var unmerged = await ReadGitAsync("ls-files --unmerged -z " + ExcludeReviews);
                if (unmerged.Length > 0)
                    throw new InvalidOperationException("Resolve index conflicts before capturing local changes for review.");
            }

            var text = await ReadGitAsync($"diff {DiffFlags} --raw --no-abbrev -z {GetComparisonArguments(snapshot)} {ExcludeReviews}");
            var fields = text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            var entries = new List<Entry>();
            for (var i = 0; i < fields.Length;)
            {
                var identity = fields[i++];
                var separator = identity.LastIndexOf(' ');
                if (!identity.StartsWith(':') || separator < 0 || i >= fields.Length)
                    throw new InvalidDataException("Invalid Git raw diff output.");
                var status = identity[(separator + 1)..];
                var path = fields[i++];
                var oldPath = path;
                if (status.StartsWith('R') || status.StartsWith('C'))
                {
                    if (i >= fields.Length)
                        throw new InvalidDataException("Invalid Git rename output.");
                    path = fields[i++];
                }
                if (Models.Review.IsReviewPath(path) || Models.Review.IsReviewPath(oldPath))
                    continue;
                entries.Add(new Entry { Path = path, OldPath = oldPath, Identity = identity });
            }

            if (snapshot.Source is Models.ReviewSourceKind.Unstaged or Models.ReviewSourceKind.AllLocal)
            {
                var untracked = await ReadGitAsync("ls-files --others --exclude-standard -z " + ExcludeReviews);
                foreach (var path in untracked.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Models.Review.IsReviewPath(path))
                        continue;
                    entries.Add(new Entry { Path = path, OldPath = string.Empty, Identity = "untracked", IsUntracked = true });
                }
            }
            return entries;
        }

        private async Task<string> ComputeFingerprintAsync(Models.ReviewSnapshot snapshot, List<Entry> entries)
        {
            var builder = new StringBuilder();
            builder.Append(snapshot.Source).Append('\0').Append(snapshot.BaseRevision).Append('\0').Append(snapshot.TargetRevision).Append('\0');
            foreach (var entry in entries)
            {
                var identity = entry.Identity;
                if (snapshot.Source is Models.ReviewSourceKind.Unstaged or Models.ReviewSourceKind.AllLocal && !entry.IsUntracked)
                {
                    var fields = identity.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length == 5)
                        identity = string.Join(' ', fields[0], fields[1], fields[2], fields[4]);
                }
                builder.Append(identity).Append('\0').Append(entry.OldPath).Append('\0').Append(entry.Path).Append('\0');
                if (snapshot.Source is not (Models.ReviewSourceKind.Unstaged or Models.ReviewSourceKind.AllLocal))
                    continue;

                var path = GetWorkingPath(entry.Path);
                var info = new FileInfo(path);
                if (info.LinkTarget is { } link)
                {
                    builder.Append(Models.Review.ComputeHash(link));
                }
                else if (File.Exists(path))
                {
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    builder.Append(Convert.ToHexStringLower(await SHA256.HashDataAsync(stream)));
                }
                else if (Directory.Exists(path))
                {
                    builder.Append(await ReadGitAsync($"-C {path.Quoted()} rev-parse HEAD"));
                }
                else
                {
                    builder.Append("missing");
                }
                builder.Append('\0');
            }
            return Models.Review.ComputeHash(builder.ToString());
        }

        private string GetWorkingPath(string relative)
        {
            var root = Path.GetFullPath(_repository);
            var path = Path.GetFullPath(Path.Combine(root, relative));
            var check = Path.GetRelativePath(root, path);
            if (Path.IsPathRooted(check) || check == ".." || check.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("A review source path is outside the working tree.");
            var parent = new DirectoryInfo(Path.GetDirectoryName(path)!);
            while (parent != null && parent.FullName != root)
            {
                if (parent.LinkTarget != null)
                    throw new InvalidDataException("A review source path traverses a symbolic link.");
                parent = parent.Parent;
            }
            return path;
        }

        public static List<string> SplitFilePatches(string patch)
        {
            var result = new List<string>();
            var start = 0;
            for (var i = 1; i < patch.Length; i++)
            {
                if (patch[i - 1] != '\n' || !patch.AsSpan(i).StartsWith("diff --git ", StringComparison.Ordinal))
                    continue;
                result.Add(patch[start..i]);
                start = i;
            }
            if (start < patch.Length)
                result.Add(patch[start..]);
            return result;
        }

        private async Task<string> ReadGitAsync(string arguments, ExitPolicy policy = ExitPolicy.Success)
        {
            var start = new ProcessStartInfo(Native.OS.GitExecutable)
            {
                Arguments = "--no-pager --no-optional-locks -c core.quotepath=off " + arguments,
                WorkingDirectory = _repository,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false, true),
                StandardErrorEncoding = Encoding.UTF8,
            };
            start.Environment["LANG"] = "C";
            start.Environment["LC_ALL"] = "C";
            using var process = new Process { StartInfo = start };
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(output, error, process.WaitForExitAsync()).ConfigureAwait(false);
            if (process.ExitCode == 0 || (policy == ExitPolicy.Difference && process.ExitCode == 1))
                return await output;
            if (policy == ExitPolicy.Probe)
                return string.Empty;
            throw new InvalidOperationException((await error).Trim());
        }
    }
}
