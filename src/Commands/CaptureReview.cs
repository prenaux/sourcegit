using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public sealed class CaptureReview
    {
        private readonly string _repository;
        private const int CaptureAttempts = 2;
        private const int FullContextLines = 999999999;
        private const string ReviewExclusions = "\":(top,exclude)_reviews/**\" \":(top,exclude)_reviews\" \":(top,exclude)_toremove/_reviews/**\"";
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
            var snapshot = await ResolveSnapshotAsync(source, baseRevision, targetRevision);
            var entries = await ReadEntriesAsync(snapshot);
            entries.Sort((a, b) => StringComparer.Ordinal.Compare(a.Path, b.Path));
            foreach (var entry in entries)
                snapshot.Files.Add(new Models.ReviewFile
                {
                    Path = entry.Path,
                    OldPath = entry.OldPath,
                    IsUntracked = entry.IsUntracked,
                    SourceIdentity = GetEntryIdentity(snapshot, entry),
                    IsLoaded = false,
                });
            snapshot.SourceFingerprint = Models.Review.ComputeHash(string.Join('\0', snapshot.Files.Select(x => x.SourceIdentity)));
            snapshot.IsLoaded = true;
            snapshot.ComputeId();
            return snapshot;
        }

        public async Task LoadFileAsync(Models.ReviewSnapshot snapshot, Models.ReviewFile file, CancellationToken cancellationToken = default)
        {
            if (file.IsLoaded)
                return;
            var paths = new[] { file.Path, file.OldPath }.Where(x => x.Length > 0).Distinct().ToArray();
            for (var attempt = 0; attempt < CaptureAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var before = await ReadEntriesAsync(snapshot, paths, cancellationToken);
                var entry = before.Find(x => x.Path == file.Path);
                if (entry == null)
                    throw new IOException("This preview file changed or left the diff. Refresh the source list.");
                var identity = GetEntryIdentity(snapshot, entry);
                var patch = entry.IsUntracked ?
                    await ReadGitAsync($"diff {DiffFlags} --no-index --patch --full-index --unified={FullContextLines} -- /dev/null {entry.Path.Quoted()}", ExitPolicy.Difference, cancellationToken) :
                    await ReadGitAsync($"diff {DiffFlags} --patch --full-index --unified={FullContextLines} {GetComparisonArguments(snapshot)} {GetPathspec(paths)}", ExitPolicy.Success, cancellationToken);
                var after = await ReadEntriesAsync(snapshot, paths, cancellationToken);
                var latest = after.Find(x => x.Path == file.Path);
                if (latest == null || identity != GetEntryIdentity(snapshot, latest))
                    continue;
                cancellationToken.ThrowIfCancellationRequested();
                var parsed = Diff.ParsePatch(patch);
                cancellationToken.ThrowIfCancellationRequested();
                file.Patch = parsed.TextDiff == null ? patch : Models.ReviewPatch.CreateContextPatch(patch, parsed.TextDiff);
                file.IsBinary = parsed.IsBinary || parsed.IsLFS;
                if (parsed.TextDiff != null)
                {
                    var oldText = Models.ReviewPatch.ExtractText(parsed.TextDiff, Models.ReviewSide.Old);
                    var newText = Models.ReviewPatch.ExtractText(parsed.TextDiff, Models.ReviewSide.New);
                    file.OldContent = Models.Review.ComputeHash(oldText);
                    file.NewContent = Models.Review.ComputeHash(newText);
                    snapshot.Contents[file.OldContent] = oldText;
                    snapshot.Contents[file.NewContent] = newText;
                }
                file.SourceIdentity = identity;
                file.IsLoaded = true;
                return;
            }
            throw new IOException("The file changed during capture. Retry after its edits finish.");
        }

        public async Task<bool> HaveFilesChangedAsync(Models.ReviewSnapshot snapshot, List<Models.ReviewFile> files)
        {
            if (files.Count == 0)
                return false;
            var entries = await ReadEntriesAsync(snapshot, files.Select(x => x.Path).ToArray());
            foreach (var file in files)
            {
                var entry = entries.Find(x => x.Path == file.Path);
                if (entry == null || file.SourceIdentity != GetEntryIdentity(snapshot, entry))
                    return true;
            }
            return false;
        }

        private async Task<Models.ReviewSnapshot> ResolveSnapshotAsync(Models.ReviewSourceKind source, string baseRevision, string targetRevision)
        {
            var snapshot = new Models.ReviewSnapshot { Source = source };
            if (source == Models.ReviewSourceKind.Commit)
            {
                snapshot.TargetRevision = await ResolveRevisionAsync(targetRevision);
                var parents = (await ReadGitAsync($"rev-list --parents -n 1 {snapshot.TargetRevision}")).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                snapshot.BaseRevision = parents.Length > 1 ? parents[1] : Models.EmptyTreeHash.Guess(snapshot.TargetRevision);
                snapshot.Subject = (await ReadGitAsync($"show -s --format=%s {snapshot.TargetRevision}")).Trim();
                return snapshot;
            }
            if (source == Models.ReviewSourceKind.Comparison)
            {
                snapshot.BaseRevision = await ResolveRevisionAsync(baseRevision);
                snapshot.TargetRevision = await ResolveRevisionAsync(targetRevision);
                var first = (await ReadGitAsync($"show -s --format=%s {snapshot.BaseRevision}")).Trim();
                var last = (await ReadGitAsync($"show -s --format=%s {snapshot.TargetRevision}")).Trim();
                snapshot.Subject = first + " -> " + last;
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

        private static string GetPathspec(string[] paths = null)
        {
            var selection = paths == null ? "." : string.Join(' ', paths.Select(x => x.Quoted()));
            return "-- " + selection + " " + ReviewExclusions;
        }

        private async Task<List<Entry>> ReadEntriesAsync(Models.ReviewSnapshot snapshot, string[] paths = null, CancellationToken cancellationToken = default)
        {
            if (snapshot.Source is Models.ReviewSourceKind.Staged or Models.ReviewSourceKind.Unstaged or Models.ReviewSourceKind.AllLocal)
            {
                var unmerged = await ReadGitAsync("ls-files --unmerged -z " + GetPathspec(paths), ExitPolicy.Success, cancellationToken);
                if (unmerged.Length > 0)
                    throw new InvalidOperationException("Resolve index conflicts before capturing local changes for review.");
            }

            var text = await ReadGitAsync($"diff {DiffFlags} --raw --no-abbrev -z {GetComparisonArguments(snapshot)} {GetPathspec(paths)}", ExitPolicy.Success, cancellationToken);
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
                var untracked = await ReadGitAsync("ls-files --others --exclude-standard -z " + GetPathspec(paths), ExitPolicy.Success, cancellationToken);
                foreach (var path in untracked.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Models.Review.IsReviewPath(path))
                        continue;
                    entries.Add(new Entry { Path = path, OldPath = string.Empty, Identity = "untracked", IsUntracked = true });
                }
            }
            return entries;
        }

        private string GetEntryIdentity(Models.ReviewSnapshot snapshot, Entry entry)
        {
            var identity = entry.Identity;
            var local = snapshot.Source is Models.ReviewSourceKind.Unstaged or Models.ReviewSourceKind.AllLocal;
            if (local && !entry.IsUntracked)
            {
                var fields = identity.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length == 5)
                    identity = string.Join(' ', fields[0], fields[1], fields[2], fields[4]);
            }
            var builder = new StringBuilder(identity).Append('\0').Append(entry.OldPath).Append('\0').Append(entry.Path);
            if (local)
            {
                var info = new FileInfo(GetWorkingPath(entry.Path));
                builder.Append('\0').Append(info.LinkTarget);
                if (info.Exists)
                    builder.Append('\0').Append(info.Length).Append('\0').Append(info.LastWriteTimeUtc.Ticks);
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

        private async Task<string> ReadGitAsync(string arguments, ExitPolicy policy = ExitPolicy.Success, CancellationToken cancellationToken = default)
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
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await Task.WhenAll(output, error, process.WaitForExitAsync(cancellationToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(true);
                throw;
            }
            if (process.ExitCode == 0 || (policy == ExitPolicy.Difference && process.ExitCode == 1))
                return await output;
            if (policy == ExitPolicy.Probe)
                return string.Empty;
            throw new InvalidOperationException((await error).Trim());
        }
    }
}
