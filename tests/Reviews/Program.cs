using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Headless;

using SourceGit.Commands;
using SourceGit.Models;
using SourceGit.ViewModels;

namespace SourceGit.ReviewTests
{
    internal static class Program
    {
        private static string s_root;

        [STAThread]
        private static int Main()
        {
            s_root = Path.GetFullPath(Path.Combine("_tmp", "review_tests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(s_root);
            Directory.SetCurrentDirectory(s_root);
            AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
            SourceGit.Native.OS.GitExecutable = "git";

            var tests = new (string Name, Action Run)[]
            {
                (nameof(ParseCapturedTextAndGenerateRealPatch), ParseCapturedTextAndGenerateRealPatch),
                (nameof(PreserveLineEndingsAndMissingFinalNewline), PreserveLineEndingsAndMissingFinalNewline),
                (nameof(KeepSnapshotIdentityIndependentOfReviewEdits), KeepSnapshotIdentityIndependentOfReviewEdits),
                (nameof(MigrateUnchangedRangesAfterInsertedLines), MigrateUnchangedRangesAfterInsertedLines),
                (nameof(MigrateInsertionImmediatelyBeforeRange), MigrateInsertionImmediatelyBeforeRange),
                (nameof(FlagChangedRangesWithoutResolvingThem), FlagChangedRangesWithoutResolvingThem),
                (nameof(RefuseAmbiguousRangeMigration), RefuseAmbiguousRangeMigration),
                (nameof(KeepCommentsOutsideTheCurrentDiff), KeepCommentsOutsideTheCurrentDiff),
                (nameof(PreserveStatesAndFileProgressAcrossUpdates), PreserveStatesAndFileProgressAcrossUpdates),
                (nameof(MigrateRenamedFiles), MigrateRenamedFiles),
                (nameof(RoundTripXmlAndCapturedContent), RoundTripXmlAndCapturedContent),
                (nameof(RejectExternalEntitiesAndStorageLinks), RejectExternalEntitiesAndStorageLinks),
                (nameof(ExportOnlyActiveCommentsWithHonestAnchors), ExportOnlyActiveCommentsWithHonestAnchors),
                (nameof(SaveOnlyAfterReviewInteraction), SaveOnlyAfterReviewInteraction),
                (nameof(KeepFileProgressSeparateFromCommentStates), KeepFileProgressSeparateFromCommentStates),
                (nameof(UndoWholeFileStateChanges), UndoWholeFileStateChanges),
                (nameof(ReattachWithoutLosingOriginalContext), ReattachWithoutLosingOriginalContext),
                (nameof(DisableStagingInBothReviewLayouts), DisableStagingInBothReviewLayouts),
                (nameof(AnchorDeletedRangesOnTheOldSide), AnchorDeletedRangesOnTheOldSide),
                (nameof(CaptureStagedUnstagedAndLocalSources), CaptureStagedUnstagedAndLocalSources),
                (nameof(CaptureRenamesDeletionsBinaryAndEmptyFiles), CaptureRenamesDeletionsBinaryAndEmptyFiles),
                (nameof(CaptureCommitAndComparisonSources), CaptureCommitAndComparisonSources),
                (nameof(KeepReviewsOutOfCaptureAndFingerprint), KeepReviewsOutOfCaptureAndFingerprint),
                (nameof(KeepSnapshotsReadableWithoutGitObjects), KeepSnapshotsReadableWithoutGitObjects),
                (nameof(LoadReviewControls), LoadReviewControls),
            };
            var failed = 0;
            foreach (var test in tests)
            {
                try
                {
                    test.Run();
                    Console.WriteLine($"PASS {test.Name}");
                }
                catch (Exception exception)
                {
                    failed++;
                    Console.Error.WriteLine($"FAIL {test.Name}: {exception}");
                }
            }
            Console.WriteLine($"{tests.Length - failed}/{tests.Length} review tests passed.");
            return failed == 0 ? 0 : 1;
        }

        private static void ParseCapturedTextAndGenerateRealPatch()
        {
            var full = "diff --git a/file.txt b/file.txt\nindex 1111111111111111111111111111111111111111..2222222222222222222222222222222222222222 100644\n--- a/file.txt\n+++ b/file.txt\n@@ -1,3 +1,3 @@\n before\n-old\n+new\n after\n";
            var diff = Diff.ParsePatch(full).TextDiff;
            Check(ReviewPatch.ExtractText(diff, ReviewSide.Old) == "before\nold\nafter\n", "Capture must retain the complete old file text.");
            Check(ReviewPatch.ExtractText(diff, ReviewSide.New) == "before\nnew\nafter\n", "Capture must retain the complete new file text.");
            var patch = ReviewPatch.CreateContextPatch(full, diff);
            var parsed = Diff.ParsePatch(patch).TextDiff;
            Check(parsed.AddedLines == 1 && parsed.DeletedLines == 1, "Stored patches must remain parseable unified diffs.");
            var fixture = CreateRepository();
            File.WriteAllText(Path.Combine(fixture, "file.txt"), "before\nold\nafter\n");
            var patchPath = Path.Combine(s_root, Guid.NewGuid().ToString("N") + ".patch");
            File.WriteAllText(patchPath, patch);
            RunGit(fixture, "apply", "--check", patchPath);
        }

        private static void PreserveLineEndingsAndMissingFinalNewline()
        {
            var full = "diff --git a/f b/f\nindex 1111111..2222222 100644\n--- a/f\n+++ b/f\n@@ -1,2 +1,2 @@\n a\r\n-old\n\\ No newline at end of file\n+new\n\\ No newline at end of file\n";
            var diff = Diff.ParsePatch(full).TextDiff;
            Check(ReviewPatch.ExtractText(diff, ReviewSide.Old) == "a\r\nold", "Old CRLF and missing final newlines must survive capture.");
            Check(ReviewPatch.ExtractText(diff, ReviewSide.New) == "a\r\nnew", "New CRLF and missing final newlines must survive capture.");
            Check(ReviewPatch.CreateContextPatch(full, diff).Contains("\\ No newline at end of file", StringComparison.Ordinal), "Stored patches must retain final-newline markers.");
        }

        private static void KeepSnapshotIdentityIndependentOfReviewEdits()
        {
            var snapshot = CreateSnapshot("a\nb\nc\n");
            var id = snapshot.Id;
            snapshot.Files[0].Comments.Add(CreateComment(snapshot, 2));
            snapshot.Files[0].Progress = ReviewFileProgress.Reviewed;
            snapshot.ComputeId();
            Check(snapshot.Id == id, "Comment and progress edits must not change captured code identity.");
        }

        private static void MigrateUnchangedRangesAfterInsertedLines()
        {
            var old = CreateSnapshot("a\nb\ntarget\nc\nd\ne\nf\n");
            var original = CreateComment(old, 3);
            old.Files[0].Comments.Add(original);
            var next = CreateSnapshot("inserted\na\nb\ntarget\nc\nd\ne\nf\n");
            Migrate(old, next);
            var comment = next.Files[0].Comments[0];
            Check(comment.Id == original.Id && comment.Anchor.StartLine == 4, "Unchanged source ranges must follow inserted lines.");
            Check(comment.Anchor.Attachment == ReviewAttachment.Attached, "A uniquely mapped range must remain attached.");
            Check(original.Anchor.StartLine == 3 && comment.OriginSnapshot == old.Id, "Migration must preserve previous and original anchors.");
        }

        private static void MigrateInsertionImmediatelyBeforeRange()
        {
            var old = CreateSnapshot("a\nb\ntarget\nc\nd\n");
            old.Files[0].Comments.Add(CreateComment(old, 3));
            var next = CreateSnapshot("a\nb\ninserted\ntarget\nc\nd\n");
            Migrate(old, next);
            Check(next.Files[0].Comments[0].Anchor.StartLine == 4 && next.Files[0].Comments[0].Anchor.Attachment == ReviewAttachment.Attached,
                "An insertion just before a uniquely identified unchanged range must not invalidate its comment.");
        }

        private static void FlagChangedRangesWithoutResolvingThem()
        {
            var old = CreateSnapshot("a\nb\ntarget\nc\nd\n");
            var comment = CreateComment(old, 3);
            comment.State = ReviewCommentState.Todo;
            old.Files[0].Comments.Add(comment);
            var next = CreateSnapshot("a\nb\nreplacement\nc\nd\n");
            Migrate(old, next);
            Check(next.Files[0].Comments[0].Anchor.Attachment == ReviewAttachment.NeedsAttention, "Changed commented code must not be guessed into a new location.");
            Check(next.Files[0].Comments[0].State == ReviewCommentState.Todo, "Changing code must not resolve a finding.");
        }

        private static void RefuseAmbiguousRangeMigration()
        {
            var old = CreateSnapshot("a\nb\ntarget\nc\nd\nseparator\na\nb\ntarget\nc\nd\n");
            old.Files[0].Comments.Add(CreateComment(old, 3));
            var next = CreateSnapshot("inserted\na\nb\ntarget\nc\nd\nseparator\na\nb\ntarget\nc\nd\n");
            Migrate(old, next);
            Check(next.Files[0].Comments[0].Anchor.Attachment == ReviewAttachment.NeedsAttention, "Repeated source windows must not be silently reassigned.");
        }

        private static void KeepCommentsOutsideTheCurrentDiff()
        {
            var old = CreateSnapshot("a\nb\ntarget\nc\nd\n");
            old.Files[0].Comments.Add(CreateComment(old, 3));
            var next = new ReviewSnapshot { Source = ReviewSourceKind.Staged, IsLoaded = true };
            next.ComputeId();
            Migrate(old, next);
            Check(next.Files.Count == 1 && next.Files[0].IsOutsideDiff, "Files with older comments must remain navigable after leaving the diff.");
            Check(next.Files[0].Comments[0].Anchor.Attachment == ReviewAttachment.OutsideDiff, "Missing diff locations must be explicitly labelled.");

            var changed = CreateSnapshot("a\nb\ntarget\nc\nd\n");
            changed.Files[0].Patch = "diff --git a/f b/f\nindex 1111111..2222222 100644\n--- a/f\n+++ b/f\n@@ -100 +100 @@\n-old\n+new\n";
            Migrate(old, changed);
            Check(changed.Files[0].Comments[0].Anchor.Attachment == ReviewAttachment.OutsideDiff, "Code outside the visible hunks must not claim an inline attachment.");
        }

        private static void PreserveStatesAndFileProgressAcrossUpdates()
        {
            var old = CreateSnapshot("a\nb\ntarget\nc\nd\ne\nf\n");
            old.Files[0].Progress = ReviewFileProgress.Reviewed;
            foreach (var state in Enum.GetValues<ReviewCommentState>())
            {
                var comment = CreateComment(old, 3);
                comment.State = state;
                old.Files[0].Comments.Add(comment);
            }
            var same = CreateSnapshot("a\nb\ntarget\nc\nd\ne\nf\n");
            Migrate(old, same);
            Check(same.Files[0].Progress == ReviewFileProgress.Reviewed, "Unchanged files must keep their reviewed mark.");
            Check(same.Files[0].Comments.Select(x => x.State).SequenceEqual(Enum.GetValues<ReviewCommentState>()), "Every comment workflow state must survive migration.");
            var next = CreateSnapshot("a\nb\ntarget\nc\nd\ne\nf\nnew\n");
            Migrate(old, next);
            Check(next.Files[0].Progress == ReviewFileProgress.Changed, "Changed files must require renewed review without resetting comments.");
        }

        private static void MigrateRenamedFiles()
        {
            var old = CreateSnapshot("a\nb\ntarget\nc\nd\n");
            old.Files[0].Comments.Add(CreateComment(old, 3));
            var next = CreateSnapshot("a\nb\ntarget\nc\nd\n");
            next.Files[0].OldPath = old.Files[0].Path;
            next.Files[0].Path = "renamed.txt";
            next.ComputeId();
            Migrate(old, next);
            Check(next.Files[0].Comments.Count == 1 && next.Files[0].Comments[0].Anchor.Attachment == ReviewAttachment.Attached, "Unambiguous renames must retain comment attachment.");
        }

        private static void RoundTripXmlAndCapturedContent()
        {
            var directory = CreateDirectory();
            var snapshot = CreateSnapshot("\ufeffa\r\nb\r\nc");
            snapshot.Files[0].Path = "src/a & b.txt";
            snapshot.ComputeId();
            var comment = CreateComment(snapshot, 2);
            comment.Body = "Quote ]]> & <tag>\r\ncontrol: \0";
            snapshot.Files[0].Comments.Add(comment);
            var review = new Review { Title = "Review & title", GeneralText = "General\r\ntext", Snapshots = [snapshot], LastSnapshotId = snapshot.Id };
            var store = new ReviewStore(directory);
            store.SaveAsync(review).GetAwaiter().GetResult();
            var loaded = store.LoadReviews().Single();
            store.LoadSnapshot(loaded, loaded.Snapshots[0]);
            Check(loaded.GeneralText == review.GeneralText, "XML must preserve general-review line endings.");
            Check(loaded.Snapshots[0].Files[0].Comments[0].Body == comment.Body, "XML must preserve comment text, control characters, and CDATA delimiters.");
            Check(store.ReadContent(snapshot.Files[0].NewContent) == "\ufeffa\r\nb\r\nc", "Captured text must preserve BOMs and round-trip independently of XML normalization.");
            Check(loaded.Snapshots[0].Files[0].Patch == snapshot.Files[0].Patch, "XML must preserve the exact stored patch.");
        }

        private static void RejectExternalEntitiesAndStorageLinks()
        {
            var directory = CreateDirectory();
            var id = Guid.NewGuid().ToString("N");
            var root = Path.Combine(directory, "_reviews", id);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "review.xml"), $"<!DOCTYPE review [<!ENTITY external SYSTEM 'file:///etc/passwd'>]><review version='1' id='{id}'><general>&external;</general></review>");
            var store = new ReviewStore(directory);
            Check(store.LoadReviews().Count == 0 && store.LoadErrors.Count == 1, "Review XML must reject external entities without silently overwriting the file.");

            var linked = CreateDirectory();
            var outside = CreateDirectory();
            Directory.CreateSymbolicLink(Path.Combine(linked, "_reviews"), outside);
            var snapshot = CreateSnapshot("a\n");
            var failed = false;
            try
            {
                new ReviewStore(linked).SaveAsync(new Review { Snapshots = [snapshot] }).GetAwaiter().GetResult();
            }
            catch (InvalidDataException)
            {
                failed = true;
            }
            Check(failed && !Directory.EnumerateFileSystemEntries(outside).Any(), "Review writes must not follow repository-controlled storage links.");
        }

        private static void ExportOnlyActiveCommentsWithHonestAnchors()
        {
            var snapshot = CreateSnapshot("a\nb\nc\n");
            foreach (var state in Enum.GetValues<ReviewCommentState>())
            {
                var comment = CreateComment(snapshot, 2);
                comment.Body = "body_" + state;
                comment.State = state;
                snapshot.Files[0].Comments.Add(comment);
            }
            snapshot.Files[0].Comments[1].Anchor.Attachment = ReviewAttachment.NeedsAttention;
            var text = ReviewExport.CreateText(new Review { GeneralText = "general instructions" }, snapshot);
            Check(text.Contains("diff --git", StringComparison.Ordinal) && text.Contains("general instructions", StringComparison.Ordinal), "AI export must contain real patches and general instructions.");
            Check(text.Contains("body_ToReview", StringComparison.Ordinal) && text.Contains("body_Todo", StringComparison.Ordinal), "AI export must include active findings.");
            Check(!text.Contains("body_Resolved", StringComparison.Ordinal) && !text.Contains("body_Dismissed", StringComparison.Ordinal), "AI export must omit closed findings.");
            Check(text.Contains("Original snapshot:", StringComparison.Ordinal) && text.Contains("not attached to the current diff", StringComparison.Ordinal), "Uncertain comments must retain explicit original context.");
        }

        private static void SaveOnlyAfterReviewInteraction()
        {
            var directory = CreateDirectory();
            using var page = new Reviews(directory);
            var snapshot = CreateSnapshot("a\nb\nc\n");
            page.OpenPreview(snapshot);
            Check(!Directory.Exists(Path.Combine(directory, "_reviews")), "Browsing a captured preview must not create review files.");
            page.Context.BeginFileComment();
            Check(!Directory.Exists(Path.Combine(directory, "_reviews")), "Opening an empty comment editor must not create review files.");
            page.Context.Body = "finding";
            page.GeneralText = "general notes";
            page.FlushAsync().GetAwaiter().GetResult();
            Check(new ReviewStore(directory).LoadReviews().Single().GeneralText == "general notes", "The first finding must save the review and general text.");
            Check(page.CurrentSnapshot.Files[0].Comments.Count == 1 && page.SavedReviews.Count == 1, "Typing must update one comment, not create one per keystroke.");
        }

        private static void KeepFileProgressSeparateFromCommentStates()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\nc\n");
            snapshot.Files[0].Comments.Add(CreateComment(snapshot, 2));
            page.OpenPreview(snapshot);
            page.IsFileReviewed = true;
            Check(snapshot.Files[0].Comments[0].State == ReviewCommentState.ToReview, "Reviewing a file must not resolve its comments.");
            page.ResolveRemainingComments();
            Check(page.IsFileReviewed, "Resolving comments must not alter the file's review progress.");
        }

        private static void UndoWholeFileStateChanges()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\nc\n");
            foreach (var state in new[] { ReviewCommentState.ToReview, ReviewCommentState.Todo, ReviewCommentState.Dismissed })
            {
                var comment = CreateComment(snapshot, 2);
                comment.State = state;
                comment.Anchor.Attachment = ReviewAttachment.OutsideDiff;
                snapshot.Files[0].Comments.Add(comment);
            }
            page.OpenPreview(snapshot);
            page.FileFilter = "file";
            page.ResolveRemainingComments();
            Check(snapshot.Files[0].Comments[0].State == ReviewCommentState.Resolved && snapshot.Files[0].Comments[2].State == ReviewCommentState.Dismissed, "Resolve remaining must include out-of-diff comments but preserve dismissed findings.");
            page.UndoCommentStates();
            Check(snapshot.Files[0].Comments[0].State == ReviewCommentState.ToReview && snapshot.Files[0].Comments[1].State == ReviewCommentState.Todo, "Whole-file changes must support undo.");
            page.BulkState = ReviewCommentState.Todo;
            page.SetAllCommentStates();
            Check(snapshot.Files[0].Comments.All(x => x.State == ReviewCommentState.Todo), "Explicit Set all must apply to every comment in the file.");
        }

        private static void ReattachWithoutLosingOriginalContext()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\nc\n");
            var comment = CreateComment(snapshot, 2);
            comment.State = ReviewCommentState.Todo;
            comment.Anchor.Attachment = ReviewAttachment.NeedsAttention;
            snapshot.Files[0].Comments.Add(comment);
            page.OpenPreview(snapshot);
            page.Context.EditComment(comment);
            page.Context.BeginReattach();
            var original = comment.OriginAnchor.StartLine;
            var replacement = new ReviewAnchor { Side = ReviewSide.New, StartLine = 3, EndLine = 3, NewStartLine = 3, NewEndLine = 3, Quote = "c" };
            var attached = page.ReattachComment(snapshot.Files[0], replacement);
            Check(attached.Id == comment.Id && attached.State == ReviewCommentState.Todo, "Reattachment must retain identity and workflow state.");
            Check(attached.Anchor.StartLine == 3 && attached.OriginAnchor.StartLine == original, "Reattachment must preserve the original source anchor.");
        }

        private static void DisableStagingInBothReviewLayouts()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\nc\n");
            page.OpenPreview(snapshot);
            Check(page.Diff.IsReview && !page.Diff.CanStageChunks && !page.Diff.CanUnstageChunks, "Review mode must not enable staging commands.");
            var switched = page.Diff.SwitchMode();
            Check(switched.IsReview && !switched.CanStageChunks && !switched.CanUnstageChunks, "Switching diff layouts must retain review interaction rules.");
            page.Context.AcceptSelection(page.Diff.Data, new TextDiffSelectedChunk(0, 1, 1, 2, true, false));
            page.Context.Body = "multiline finding";
            Check(snapshot.Files[0].Comments[0].Anchor.StartLine == 1 && snapshot.Files[0].Comments[0].Anchor.EndLine == 2, "Multiline comments must use source coordinates, not indicator rows.");
        }

        private static void AnchorDeletedRangesOnTheOldSide()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nz\n");
            var oldText = "a\nremoved\nz\n";
            var oldHash = Review.ComputeHash(oldText);
            snapshot.Contents[oldHash] = oldText;
            snapshot.Files[0].OldContent = oldHash;
            snapshot.Files[0].Patch = "diff --git a/file.txt b/file.txt\nindex 1111111..2222222 100644\n--- a/file.txt\n+++ b/file.txt\n@@ -1,3 +1,2 @@\n a\n-removed\n z\n";
            snapshot.ComputeId();
            page.OpenPreview(snapshot);
            page.Context.AcceptSelection(page.Diff.Data, new TextDiffSelectedChunk(0, 1, 2, 3, true, false));
            page.Context.Body = "deleted-code finding";
            var comment = snapshot.Files[0].Comments.Single();
            Check(comment.Anchor.Side == ReviewSide.Old && comment.Anchor.StartLine == 2 && comment.Anchor.EndLine == 3,
                "A deletion selection with surrounding context must anchor to the old source, not just its surviving new context.");
            var contextLine = new TextDiffLine(TextDiffLineType.Normal, "z", 3, 2);
            Check(page.Context.FindComment(contextLine, ReviewPane.Old) != null && page.Context.FindComment(contextLine, ReviewPane.New) == null,
                "Old-side comment markers must not appear on the new side of a split diff.");
        }

        private static void CaptureStagedUnstagedAndLocalSources()
        {
            RunAsync(async () =>
            {
                var repository = CreateRepository();
                var file = Path.Combine(repository, "space name.txt");
                File.WriteAllText(file, "one\nbefore\n");
                RunGit(repository, "add", ".");
                RunGit(repository, "commit", "-qm", "initial");
                File.WriteAllText(file, "one\nstaged\n");
                RunGit(repository, "add", ".");
                File.WriteAllText(file, "one\nworking\n");
                File.WriteAllText(Path.Combine(repository, "extra.txt"), "untracked\n");
                var index = RunGit(repository, "ls-files", "--stage");
                var staged = await new CaptureReview(repository).CaptureAsync(ReviewSourceKind.Staged);
                Check(staged.Files.Count == 1 && staged.Contents[staged.Files[0].NewContent] == "one\nstaged\n", "Staged review must capture the index, not working-file contents.");
                var unstaged = await new CaptureReview(repository).CaptureAsync(ReviewSourceKind.Unstaged);
                var changed = unstaged.Files.Single(x => x.Path == "space name.txt");
                Check(unstaged.Contents[changed.OldContent] == "one\nstaged\n" && unstaged.Contents[changed.NewContent] == "one\nworking\n", "Unstaged review must compare index and working files.");
                Check(unstaged.Files.Any(x => x.Path == "extra.txt"), "Local reviews must include untracked text files.");
                var local = await new CaptureReview(repository).CaptureAsync(ReviewSourceKind.AllLocal);
                changed = local.Files.Single(x => x.Path == "space name.txt");
                Check(local.Contents[changed.OldContent] == "one\nbefore\n", "All local changes must compare against HEAD.");
                Check(index == RunGit(repository, "ls-files", "--stage"), "Capturing reviews must not modify the staged contents.");
            });
        }

        private static void CaptureRenamesDeletionsBinaryAndEmptyFiles()
        {
            RunAsync(async () =>
            {
                var repository = CreateRepository();
                File.WriteAllText(Path.Combine(repository, "rename.txt"), "original\n");
                File.WriteAllText(Path.Combine(repository, "delete.txt"), "to delete\n");
                RunGit(repository, "add", ".");
                RunGit(repository, "commit", "-qm", "initial");
                File.Move(Path.Combine(repository, "rename.txt"), Path.Combine(repository, "moved file.txt"));
                File.Move(Path.Combine(repository, "delete.txt"), Path.Combine(CreateDirectory(), "deleted.txt"));
                RunGit(repository, "add", "-A");
                File.WriteAllBytes(Path.Combine(repository, "binary.bin"), [0, 1, 255]);
                File.WriteAllText(Path.Combine(repository, "empty.txt"), string.Empty);
                var snapshot = await new CaptureReview(repository).CaptureAsync(ReviewSourceKind.AllLocal);
                Check(snapshot.Files.Single(x => x.Path == "moved file.txt").OldPath == "rename.txt", "Git rename identities must survive capture.");
                var deleted = snapshot.Files.Single(x => x.Path == "delete.txt");
                Check(snapshot.Contents[deleted.OldContent] == "to delete\n", "Deleted text must remain available for old-side comments.");
                Check(snapshot.Files.Single(x => x.Path == "binary.bin").IsBinary, "Binary changes must remain available for file comments.");
                Check(snapshot.Files.Any(x => x.Path == "empty.txt"), "New empty files must not disappear from captured reviews.");
            });
        }

        private static void CaptureCommitAndComparisonSources()
        {
            RunAsync(async () =>
            {
                var repository = CreateRepository();
                var file = Path.Combine(repository, "file.txt");
                File.WriteAllText(file, "initial\n");
                RunGit(repository, "add", ".");
                RunGit(repository, "commit", "-qm", "first");
                var first = RunGit(repository, "rev-parse", "HEAD").Trim();
                File.WriteAllText(file, "second\n");
                RunGit(repository, "add", ".");
                RunGit(repository, "commit", "-qm", "second");
                var second = RunGit(repository, "rev-parse", "HEAD").Trim();
                var commit = await new CaptureReview(repository).CaptureAsync(ReviewSourceKind.Commit, string.Empty, second);
                Check(commit.BaseRevision == first && commit.TargetRevision == second, "Commit review must use its first parent as base.");
                var comparison = await new CaptureReview(repository).CaptureAsync(ReviewSourceKind.Comparison, first, second);
                Check(comparison.Files[0].Patch == commit.Files[0].Patch, "Explicit comparisons must use the selected endpoints.");
                var root = await new CaptureReview(repository).CaptureAsync(ReviewSourceKind.Commit, string.Empty, first);
                Check(root.Files.Count == 1, "The initial commit must compare against an empty tree.");
            });
        }

        private static void KeepReviewsOutOfCaptureAndFingerprint()
        {
            RunAsync(async () =>
            {
                var repository = CreateRepository();
                File.WriteAllText(Path.Combine(repository, "file.txt"), "base\n");
                RunGit(repository, "add", ".");
                RunGit(repository, "commit", "-qm", "initial");
                File.WriteAllText(Path.Combine(repository, "file.txt"), "changed\n");
                var capture = await new CaptureReview(repository).CaptureAsync(ReviewSourceKind.AllLocal);
                Directory.CreateDirectory(Path.Combine(repository, "_reviews"));
                File.WriteAllText(Path.Combine(repository, "_reviews", "own.xml"), "review notes");
                RunGit(repository, "add", "_reviews");
                var next = await new CaptureReview(repository).CaptureAsync(ReviewSourceKind.AllLocal);
                Check(next.Id == capture.Id && next.SourceFingerprint == capture.SourceFingerprint, "Saving or staging review files must not change a review's source identity.");
                Check(next.Files.All(x => !Review.IsReviewPath(x.Path)), "Reviews must never review their own storage.");
            });
        }

        private static void KeepSnapshotsReadableWithoutGitObjects()
        {
            RunAsync(async () =>
            {
                var repository = CreateRepository();
                File.WriteAllText(Path.Combine(repository, "file.txt"), "base\n");
                RunGit(repository, "add", ".");
                RunGit(repository, "commit", "-qm", "initial");
                File.WriteAllText(Path.Combine(repository, "file.txt"), "reviewed\n");
                var snapshot = await new CaptureReview(repository).CaptureAsync(ReviewSourceKind.AllLocal);
                snapshot.Files[0].Comments.Add(CreateComment(snapshot, 1));
                var review = new Review { Snapshots = [snapshot], LastSnapshotId = snapshot.Id };
                var store = new ReviewStore(repository);
                await store.SaveAsync(review);
                Directory.Move(Path.Combine(repository, ".git"), Path.Combine(CreateDirectory(), "original_git"));
                File.WriteAllText(Path.Combine(repository, "file.txt"), "later edits\n");
                var loaded = store.LoadReviews().Single();
                store.LoadSnapshot(loaded, loaded.Snapshots[0]);
                Check(store.ReadContent(loaded.Snapshots[0].Files[0].NewContent) == "reviewed\n", "Review contents must not depend on retained Git objects or current files.");
            });
        }

        private static void LoadReviewControls()
        {
            _ = new SourceGit.Views.ReviewCommentEditor();
            _ = new SourceGit.Views.Reviews();
            _ = new SourceGit.Views.TextDiffView();
        }

        private static ReviewSnapshot CreateSnapshot(string text)
        {
            var path = "file.txt";
            var lines = ReviewPatch.SplitLines(text);
            var patch = $"diff --git a/{path} b/{path}\nnew file mode 100644\nindex 0000000000000000000000000000000000000000..2222222222222222222222222222222222222222\n--- /dev/null\n+++ b/{path}\n@@ -0,0 +1,{lines.Length} @@\n";
            for (var i = 0; i < lines.Length; i++)
            {
                patch += "+" + lines[i] + "\n";
                if (i == lines.Length - 1 && !text.EndsWith('\n'))
                    patch += "\\ No newline at end of file\n";
            }
            var hash = Review.ComputeHash(text);
            var snapshot = new ReviewSnapshot
            {
                Source = ReviewSourceKind.Staged,
                BaseRevision = "base",
                TargetRevision = "index",
                IsLoaded = true,
                Files = [new ReviewFile { Path = path, OldPath = path, Patch = patch, OldContent = Review.ComputeHash(string.Empty), NewContent = hash }],
            };
            snapshot.Contents[hash] = text;
            snapshot.Contents[Review.ComputeHash(string.Empty)] = string.Empty;
            snapshot.ComputeId();
            return snapshot;
        }

        private static ReviewComment CreateComment(ReviewSnapshot snapshot, int line)
        {
            var file = snapshot.Files[0];
            var text = snapshot.Contents[file.NewContent];
            var quote = ReviewPatch.SplitLines(text)[line - 1];
            var anchor = new ReviewAnchor { Side = ReviewSide.New, StartLine = line, EndLine = line, NewStartLine = line, NewEndLine = line, Quote = quote };
            return new ReviewComment { Body = "finding", Anchor = anchor.Copy(), OriginAnchor = anchor.Copy(), OriginPath = file.Path, OriginSnapshot = snapshot.Id };
        }

        private static void Migrate(ReviewSnapshot previous, ReviewSnapshot next)
        {
            ReviewMigration.MigrateComments(previous, next, hash => next.Contents.TryGetValue(hash, out var text) ? text : previous.Contents[hash]);
        }

        private static string CreateDirectory()
        {
            var path = Path.Combine(s_root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string CreateRepository()
        {
            var path = CreateDirectory();
            RunGit(path, "init", "-q");
            return path;
        }

        private static string RunGit(string repository, params string[] arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = repository,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.Environment["GIT_AUTHOR_NAME"] = "Review Tests";
            start.Environment["GIT_AUTHOR_EMAIL"] = "review_tests@example.invalid";
            start.Environment["GIT_COMMITTER_NAME"] = "Review Tests";
            start.Environment["GIT_COMMITTER_EMAIL"] = "review_tests@example.invalid";
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"git {string.Join(' ', arguments)}: {error}");
            return output;
        }

        private static void RunAsync(Func<Task> action)
        {
            Task.Run(action).GetAwaiter().GetResult();
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
