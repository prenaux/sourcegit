using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

using SourceGit.Commands;
using SourceGit.Models;
using SourceGit.ViewModels;

namespace SourceGit.ReviewTests
{
    internal static class Program
    {
        private static string s_root;
        private static Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime s_lifetime;

        [STAThread]
        private static int Main()
        {
            s_root = Path.GetFullPath(Path.Combine("_tmp", "review_tests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(s_root);
            Directory.SetCurrentDirectory(s_root);
            s_lifetime = new Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            AppBuilder.Configure<Application>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithLifetime(s_lifetime);
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
                (nameof(PreserveAllCommentStatesAcrossUpdates), PreserveAllCommentStatesAcrossUpdates),
                (nameof(PreserveFileCommentsAcrossBinaryIdentityChanges), PreserveFileCommentsAcrossBinaryIdentityChanges),
                (nameof(MigrateRenamedFiles), MigrateRenamedFiles),
                (nameof(RoundTripXmlAndCapturedContent), RoundTripXmlAndCapturedContent),
                (nameof(RejectExternalEntitiesAndStorageLinks), RejectExternalEntitiesAndStorageLinks),
                (nameof(ExportOnlyActiveCommentsWithHonestAnchors), ExportOnlyActiveCommentsWithHonestAnchors),
                (nameof(SaveOnlyAfterReviewInteraction), SaveOnlyAfterReviewInteraction),
                (nameof(KeepReviewedMembershipAcrossCommentStateChanges), KeepReviewedMembershipAcrossCommentStateChanges),
                (nameof(SetSelectedCommentsStateAppliesToTheWholeFile), SetSelectedCommentsStateAppliesToTheWholeFile),
                (nameof(ReattachWithoutLosingOriginalContext), ReattachWithoutLosingOriginalContext),
                (nameof(DisableStagingInBothReviewLayouts), DisableStagingInBothReviewLayouts),
                (nameof(AnchorDeletedRangesOnTheOldSide), AnchorDeletedRangesOnTheOldSide),
                (nameof(CaptureStagedUnstagedAndLocalSources), CaptureStagedUnstagedAndLocalSources),
                (nameof(CaptureRenamesDeletionsBinaryAndEmptyFiles), CaptureRenamesDeletionsBinaryAndEmptyFiles),
                (nameof(CaptureCommitAndComparisonSources), CaptureCommitAndComparisonSources),
                (nameof(KeepReviewsOutOfCaptureAndFingerprint), KeepReviewsOutOfCaptureAndFingerprint),
                (nameof(KeepSnapshotsReadableWithoutGitObjects), KeepSnapshotsReadableWithoutGitObjects),
                (nameof(KeepReviewEditorsOutOfOrdinaryDiffs), KeepReviewEditorsOutOfOrdinaryDiffs),
                (nameof(PersistOnlyReviewedItems), PersistOnlyReviewedItems),
                (nameof(CopyFindingsBeforeRelevantContext), CopyFindingsBeforeRelevantContext),
                (nameof(RemoveEmptyCommentsOnClose), RemoveEmptyCommentsOnClose),
                (nameof(ListLargeSourcesWithoutCapturingTheirContents), ListLargeSourcesWithoutCapturingTheirContents),
                (nameof(KeepLineContextSmallInsideLargeHunks), KeepLineContextSmallInsideLargeHunks),
                (nameof(CommentOnSelectedRangesWithToFixDefault), CommentOnSelectedRangesWithToFixDefault),
                (nameof(SaveGeneralSubjectWithoutCapturingFiles), SaveGeneralSubjectWithoutCapturingFiles),
                (nameof(MigrateRecordedContextWithoutFullSources), MigrateRecordedContextWithoutFullSources),
                (nameof(CheckOnlyRelevantFilesAndCancelObsoleteLoads), CheckOnlyRelevantFilesAndCancelObsoleteLoads),
                (nameof(ConvertLegacyReviewsWithoutLosingNotes), ConvertLegacyReviewsWithoutLosingNotes),
                (nameof(RefreshUnreviewedPreviewsWithoutChangingRecordedCode), RefreshUnreviewedPreviewsWithoutChangingRecordedCode),
                (nameof(ReopenSparseReviewsWithTheFullLiveFileList), ReopenSparseReviewsWithTheFullLiveFileList),
                (nameof(PreserveExactGeneralEditorTextWhileTyping), PreserveExactGeneralEditorTextWhileTyping),
                (nameof(ShowBothSourceRangesForMixedComments), ShowBothSourceRangesForMixedComments),
                (nameof(NavigateSectionBoundariesBeforeOtherSections), NavigateSectionBoundariesBeforeOtherSections),
                (nameof(EditHoveredCommentsWithoutDuplicatingFindings), EditHoveredCommentsWithoutDuplicatingFindings),
                (nameof(MeasureSharedEditorsAndPreserveTyping), MeasureSharedEditorsAndPreserveTyping),
                (nameof(KeepDockedCommentEditorAcrossNavigation), KeepDockedCommentEditorAcrossNavigation),
                (nameof(TrackReviewMarksInTheFileTree), TrackReviewMarksInTheFileTree),
                (nameof(KeepOneActiveFileWhileSelectingMultipleFiles), KeepOneActiveFileWhileSelectingMultipleFiles),
                (nameof(MarkFileAsOkOnlyWithoutToFixComments), MarkFileAsOkOnlyWithoutToFixComments),
                (nameof(ReuseTheSingleFileComment), ReuseTheSingleFileComment),
                (nameof(ShowDiffStatsForTheSelectedFile), ShowDiffStatsForTheSelectedFile),
                (nameof(ExportOnlyLoadedPatches), ExportOnlyLoadedPatches),
                (nameof(DeriveFileStateFromPatch), DeriveFileStateFromPatch),
                (nameof(HideReviewMarksOnFoldersAndPlainFiles), HideReviewMarksOnFoldersAndPlainFiles),
                (nameof(KeepVisualLinesValidWhenReviewStateChanges), KeepVisualLinesValidWhenReviewStateChanges),
                (nameof(KeepFileActionsInTheMenuForRecordedSources), KeepFileActionsInTheMenuForRecordedSources),
                (nameof(SortCommentsFileLevelThenByFirstLine), SortCommentsFileLevelThenByFirstLine),
                (nameof(FilterFilesByCommentState), FilterFilesByCommentState),
                (nameof(AvoidConsecutiveMenuSeparators), AvoidConsecutiveMenuSeparators),
                (nameof(FormatCommentRowsCompactly), FormatCommentRowsCompactly),
                (nameof(FocusCommentEditorAtTheEnd), FocusCommentEditorAtTheEnd),
                (nameof(ResizeEditorPanelWithSplitter), ResizeEditorPanelWithSplitter),
                (nameof(ShowReviewDiffContextMenu), ShowReviewDiffContextMenu),
                (nameof(SwitchTabsForCommentTargets), SwitchTabsForCommentTargets),
                (nameof(ReopenSavedReviewWithAllFilesAndFullAnnotatedDiff), ReopenSavedReviewWithAllFilesAndFullAnnotatedDiff),
                (nameof(ReopenHistoryReviewWithUncommentedSections), ReopenHistoryReviewWithUncommentedSections),
                (nameof(OpenReviewAndSnapshotSelectorsWithFullSources), OpenReviewAndSnapshotSelectorsWithFullSources),
                (nameof(KeepBrowsingDataOutOfSparseStorage), KeepBrowsingDataOutOfSparseStorage),
                (nameof(KeepRecordedContextWhenTheSourceIsUnavailable), KeepRecordedContextWhenTheSourceIsUnavailable),
                (nameof(CombinePathAndCommentPresenceFilters), CombinePathAndCommentPresenceFilters),
                (nameof(ShowClearLabelsAndCopyablePaths), ShowClearLabelsAndCopyablePaths),
                (nameof(SeparateChangedLocalSourcesFromRecordedContext), SeparateChangedLocalSourcesFromRecordedContext),
                (nameof(ReviewedFilteringMeansAnyNonemptyComment), ReviewedFilteringMeansAnyNonemptyComment),
                (nameof(OpenEmptyReviewsWithoutStartingASource), OpenEmptyReviewsWithoutStartingASource),
                (nameof(KeepReviewActionsOutsideTheSearchEditor), KeepReviewActionsOutsideTheSearchEditor),
                (nameof(DoNotExposeSourceCreationControlsInsideReviews), DoNotExposeSourceCreationControlsInsideReviews),
                (nameof(RemovingTheLastCommentMakesTheFileUnreviewed), RemovingTheLastCommentMakesTheFileUnreviewed),
                (nameof(DiscardObsoleteFileMarksWithoutLosingComments), DiscardObsoleteFileMarksWithoutLosingComments),
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
            snapshot.ComputeId();
            Check(snapshot.Id == id, "Comment edits must not change captured code identity.");
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
            comment.State = ReviewCommentState.ToFix;
            old.Files[0].Comments.Add(comment);
            var next = CreateSnapshot("a\nb\nreplacement\nc\nd\n");
            Migrate(old, next);
            Check(next.Files[0].Comments[0].Anchor.Attachment == ReviewAttachment.NeedsAttention, "Changed commented code must not be guessed into a new location.");
            Check(next.Files[0].Comments[0].State == ReviewCommentState.ToFix, "Changing code must not resolve a finding.");
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

        private static void PreserveAllCommentStatesAcrossUpdates()
        {
            var old = CreateSnapshot("a\nb\ntarget\nc\nd\ne\nf\n");
            foreach (var state in Enum.GetValues<ReviewCommentState>())
            {
                var comment = CreateComment(old, 3);
                comment.State = state;
                old.Files[0].Comments.Add(comment);
            }
            var next = CreateSnapshot("a\nb\ntarget\nc\nd\ne\nf\nnew\n");
            Migrate(old, next);
            Check(next.Files[0].Comments.Select(x => x.State).SequenceEqual(Enum.GetValues<ReviewCommentState>()), "Every comment workflow state must survive source updates.");
        }

        private static void PreserveFileCommentsAcrossBinaryIdentityChanges()
        {
            var old = CreateSnapshot("binary placeholder\n");
            var next = CreateSnapshot("other placeholder\n");
            old.Files[0].IsBinary = next.Files[0].IsBinary = true;
            old.Files[0].OldContent = old.Files[0].NewContent = string.Empty;
            next.Files[0].OldContent = next.Files[0].NewContent = string.Empty;
            old.Files[0].SourceIdentity = "old binary";
            next.Files[0].SourceIdentity = "new binary";
            old.Files[0].Comments.Add(new ReviewComment { Body = "Binary finding", State = ReviewCommentState.Fixed, Anchor = new ReviewAnchor { Side = ReviewSide.File } });
            Migrate(old, next);
            Check(next.Files[0].HasReviewWork && next.Files[0].Comments.Single().State == ReviewCommentState.Fixed,
                "Binary source changes must preserve file comments and their states.");
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
            Check(loaded.Snapshots[0].Files[0].Comments[0].OriginAnchor.Quote == "b\r", "Recorded source context must preserve line endings without a full-source cache.");
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
            Check(text.Contains("body_Comment", StringComparison.Ordinal) && text.Contains("body_ToFix", StringComparison.Ordinal), "Copy Review must include active findings.");
            Check(!text.Contains("body_Fixed", StringComparison.Ordinal) && !text.Contains("body_Dismissed", StringComparison.Ordinal), "Copy Review must omit closed findings.");
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

        private static void KeepReviewedMembershipAcrossCommentStateChanges()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\nc\n");
            snapshot.Files[0].Comments.Add(CreateComment(snapshot, 2));
            page.OpenPreview(snapshot);
            page.ReviewFilter = ReviewFileFilter.Reviewed;
            page.SetSelectedCommentsState(ReviewCommentState.Fixed);
            Check(page.Files.Count == 1 && snapshot.Files[0].Comments[0].State == ReviewCommentState.Fixed,
                "Fixing a finding must keep its file reviewed because the comment still exists.");
            page.SetSelectedCommentsState(ReviewCommentState.Dismissed);
            Check(page.Files.Count == 1, "Dismissed comments must still count as review work.");
        }

        private static void SetSelectedCommentsStateAppliesToTheWholeFile()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\nc\n");
            foreach (var state in new[] { ReviewCommentState.Comment, ReviewCommentState.ToFix, ReviewCommentState.Dismissed })
            {
                var comment = CreateComment(snapshot, 2);
                comment.State = state;
                comment.Anchor.Attachment = ReviewAttachment.OutsideDiff;
                snapshot.Files[0].Comments.Add(comment);
            }
            page.OpenPreview(snapshot);
            page.SetSelectedCommentsState(ReviewCommentState.Fixed);
            Check(snapshot.Files[0].Comments.All(x => x.State == ReviewCommentState.Fixed),
                "Set all comments must include comments outside the current diff.");
            page.SetSelectedCommentsState(ReviewCommentState.ToFix);
            Check(snapshot.Files[0].Comments.All(x => x.State == ReviewCommentState.ToFix),
                "Set all comments must apply the selected state to every comment in the file.");
            Check(page.SelectedToFixCount == 3, "The selected file must report its open tofix comments.");
        }

        private static void ReattachWithoutLosingOriginalContext()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\nc\n");
            var comment = CreateComment(snapshot, 2);
            comment.State = ReviewCommentState.ToFix;
            comment.Anchor.Attachment = ReviewAttachment.NeedsAttention;
            snapshot.Files[0].Comments.Add(comment);
            page.OpenPreview(snapshot);
            page.Context.EditComment(comment);
            page.Context.BeginReattach();
            var original = comment.OriginAnchor.StartLine;
            var replacement = new ReviewAnchor { Side = ReviewSide.New, StartLine = 3, EndLine = 3, NewStartLine = 3, NewEndLine = 3, Quote = "c" };
            var attached = page.ReattachComment(snapshot.Files[0], replacement);
            Check(attached.Id == comment.Id && attached.State == ReviewCommentState.ToFix, "Reattachment must retain identity and workflow state.");
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
            var outside = new TextDiffLine(TextDiffLineType.Normal, "a", 1, 1);
            Check(page.Context.FindComment(contextLine, ReviewPane.Old) != null && page.Context.FindComment(outside, ReviewPane.New) == null,
                "Mixed comment markers must stay within their recorded old and new source ranges.");
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
                var capture = new CaptureReview(repository);
                var staged = await capture.CaptureAsync(ReviewSourceKind.Staged);
                await capture.LoadFileAsync(staged, staged.Files[0]);
                Check(staged.Files.Count == 1 && staged.Contents[staged.Files[0].NewContent] == "one\nstaged\n", "Staged review must capture the index, not working-file contents.");
                var unstaged = await new CaptureReview(repository).CaptureAsync(ReviewSourceKind.Unstaged);
                var changed = unstaged.Files.Single(x => x.Path == "space name.txt");
                await capture.LoadFileAsync(unstaged, changed);
                Check(unstaged.Contents[changed.OldContent] == "one\nstaged\n" && unstaged.Contents[changed.NewContent] == "one\nworking\n", "Unstaged review must compare index and working files.");
                Check(unstaged.Files.Any(x => x.Path == "extra.txt"), "Local reviews must include untracked text files.");
                var local = await new CaptureReview(repository).CaptureAsync(ReviewSourceKind.AllLocal);
                changed = local.Files.Single(x => x.Path == "space name.txt");
                await capture.LoadFileAsync(local, changed);
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
                foreach (var file in snapshot.Files)
                    await new CaptureReview(repository).LoadFileAsync(snapshot, file);
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
                await new CaptureReview(repository).LoadFileAsync(commit, commit.Files[0]);
                await new CaptureReview(repository).LoadFileAsync(comparison, comparison.Files[0]);
                Check(commit.Subject == "second" && comparison.Subject.Contains("first -> second", StringComparison.Ordinal), "Commit subjects must be available in review selectors.");
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
                await new CaptureReview(repository).LoadFileAsync(snapshot, snapshot.Files[0]);
                snapshot.Files[0].Comments.Add(CreateComment(snapshot, 1));
                var review = new Review { Snapshots = [snapshot], LastSnapshotId = snapshot.Id };
                var store = new ReviewStore(repository);
                await store.SaveAsync(review);
                Directory.Move(Path.Combine(repository, ".git"), Path.Combine(CreateDirectory(), "original_git"));
                File.WriteAllText(Path.Combine(repository, "file.txt"), "later edits\n");
                var loaded = store.LoadReviews().Single();
                store.LoadSnapshot(loaded, loaded.Snapshots[0]);
                Check(loaded.Snapshots[0].Files[0].Comments[0].OriginAnchor.Quote == "reviewed" && loaded.Snapshots[0].Files[0].Patch.Contains("+reviewed", StringComparison.Ordinal),
                    "Recorded comment context must not depend on retained Git objects or current files.");
            });
        }

        private static void KeepReviewEditorsOutOfOrdinaryDiffs()
        {
            using var page = new Reviews(CreateDirectory());
            page.OpenPreview(CreateSnapshot("a\nb\n"));
            page.Context.BeginFileComment();
            page.Context.Body = "finding";
            var ordinary = new CombinedTextDiff(page.Diff.Option, page.Diff.Data, page.Diff);
            Check(!ordinary.IsReview, "Reusing diff state must not copy review ownership into ordinary diffs.");
            var view = new SourceGit.Views.TextDiffView { DataContext = page.Diff };
            view.DataContext = ordinary;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Check(view.FindControl<Avalonia.Controls.Border>("ReviewEditor") == null, "Ordinary diffs must not host a review editor.");
        }

        private static void PersistOnlyReviewedItems()
        {
            var repository = CreateDirectory();
            var snapshot = CreateSnapshot("a\nb\n");
            snapshot.Files[0].Comments.Add(CreateComment(snapshot, 1));
            for (var i = 0; i < 200; i++)
                snapshot.Files.Add(new ReviewFile { Path = $"untouched_{i}.txt", Patch = "unrelated source", OldContent = snapshot.Files[0].OldContent, NewContent = snapshot.Files[0].NewContent });
            snapshot.ComputeId();
            var review = new Review { Title = "A useful subject", Snapshots = [snapshot], LastSnapshotId = snapshot.Id };
            var store = new ReviewStore(repository);
            store.SaveAsync(review).GetAwaiter().GetResult();
            var root = Path.Combine(repository, Review.DirectoryName);
            Check(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == 2, "One finding must write only readable Markdown and compact metadata, not hundreds of snapshots or blobs.");
            var loaded = store.LoadReviews().Single();
            store.LoadSnapshot(loaded, loaded.Snapshots[0]);
            Check(loaded.Snapshots[0].Files.Count == 1, "Untouched files must not be persisted.");
            var markdown = File.ReadAllText(Path.Combine(root, review.Id, "review.md"));
            Check(markdown.Contains("A useful subject", StringComparison.Ordinal) && markdown.Contains("finding", StringComparison.Ordinal), "The review folder must contain directly readable review text.");
        }

        private static void CopyFindingsBeforeRelevantContext()
        {
            var snapshot = CreateSnapshot("a\nb\n");
            snapshot.Files[0].Comments.Add(CreateComment(snapshot, 1));
            snapshot.Files.Add(new ReviewFile { Path = "unreviewed.cpp", Patch = "UNRELATED_DIFF" });
            var text = ReviewExport.CreateText(new Review { Title = "subject" }, snapshot);
            Check(!text.Contains("UNRELATED_DIFF", StringComparison.Ordinal) && !text.Contains("unreviewed.cpp", StringComparison.Ordinal), "Copy Review must omit unrelated diffs and files.");
            Check(text.IndexOf("finding", StringComparison.Ordinal) < text.IndexOf("diff --git", StringComparison.Ordinal), "Findings must precede their supporting code context.");
        }

        private static void RemoveEmptyCommentsOnClose()
        {
            using var page = new Reviews(CreateDirectory());
            page.OpenPreview(CreateSnapshot("a\nb\n"));
            page.Context.BeginFileComment();
            page.Context.Body = "draft";
            page.Context.Body = "  \n";
            page.Context.CloseEditor();
            Check(page.Comments.Count == 0 && page.CurrentSnapshot.Files[0].Comments.Count == 0, "Closing an emptied comment must remove its list entry and captured context.");
        }

        private static void ListLargeSourcesWithoutCapturingTheirContents()
        {
            RunAsync(async () =>
            {
                var repository = CreateRepository();
                for (var i = 0; i < 300; i++)
                    File.WriteAllText(Path.Combine(repository, $"generated_{i}.txt"), new string('x', 4096));
                var snapshot = await new CaptureReview(repository).CaptureAsync(ReviewSourceKind.Unstaged);
                Check(snapshot.Files.Count == 300 && snapshot.Files.All(x => x.Patch.Length == 0) && snapshot.Contents.Count == 0,
                    "Opening a large source must list metadata without reading or capturing every file body.");
            });
        }

        private static void KeepLineContextSmallInsideLargeHunks()
        {
            var repository = CreateDirectory();
            var snapshot = CreateSnapshot(string.Join('\n', Enumerable.Range(1, 5000).Select(x => $"line_{x}")) + "\n");
            snapshot.Files[0].Comments.Add(CreateComment(snapshot, 2500));
            var review = new Review { Title = "One line", Snapshots = [snapshot], LastSnapshotId = snapshot.Id };
            new ReviewStore(repository).SaveAsync(review).GetAwaiter().GetResult();
            var files = Directory.GetFiles(Path.Combine(repository, Review.DirectoryName), "*", SearchOption.AllDirectories);
            Check(files.Length == 2 && files.Sum(x => new FileInfo(x).Length) < 16384, "A line comment inside a huge hunk must retain bounded context, not the whole hunk or source file.");
            var markdown = File.ReadAllText(files.Single(x => x.EndsWith(".md", StringComparison.Ordinal)));
            Check(markdown.Contains("line_2500", StringComparison.Ordinal) && !markdown.Contains("+line_1\n", StringComparison.Ordinal), "Saved review text must include only the commented region.");
        }

        private static void CommentOnSelectedRangesWithToFixDefault()
        {
            using var page = new Reviews(CreateDirectory());
            page.OpenPreview(CreateSnapshot("a\nb\nc\n"));
            var line = page.Diff.CreateReviewSelection(ReviewPane.Combined, 2, 2);
            var section = page.Diff.CreateReviewSelection(ReviewPane.Combined, 1, 3);
            Check(line.StartIdx == 2 && line.EndIdx == 2 && section.StartIdx == 1 && section.EndIdx == 3, "Comment targets must follow the selected source range without expanding to a patch hunk.");
            page.Context.AcceptSelection(page.Diff.Data, line);
            page.Context.Body = "line finding";
            Check(page.Context.State == ReviewCommentState.ToFix && page.Context.SelectedComment.Anchor.StartLine == 2, "New comments must default to tofix and retain the selected source line.");
            page.Context.AcceptSelection(page.Diff.Data, section);
            page.Context.Body = "section finding";
            Check(page.Context.SelectedComment.Anchor.StartLine == 1 && page.Context.SelectedComment.Anchor.EndLine == 3, "A selected section must retain its full source range.");
        }

        private static void SaveGeneralSubjectWithoutCapturingFiles()
        {
            var repository = CreateDirectory();
            using var page = new Reviews(repository);
            page.OpenPreview(CreateSnapshot("a\nb\n"));
            page.GeneralReviewMessage = "A clear subject\n\nGeneral instructions";
            page.FlushAsync().GetAwaiter().GetResult();
            var loaded = new ReviewStore(repository).LoadReviews().Single();
            Check(loaded.Title == "A clear subject" && loaded.GeneralText == "General instructions" && loaded.Snapshots[0].Files.Count == 0,
                "The commit-message-style editor must save its subject and body without capturing unreviewed files.");
            page.GeneralReviewMessage = "Subject only";
            page.FlushAsync().GetAwaiter().GetResult();
            Check(new ReviewStore(repository).LoadReviews().Single().Title == "Subject only", "A subject-only general review must be saved.");
        }

        private static void MigrateRecordedContextWithoutFullSources()
        {
            var repository = CreateDirectory();
            var previous = CreateSnapshot("a\nb\ntarget\nc\nd\ne\nf\n");
            previous.Files[0].Comments.Add(CreateComment(previous, 3));
            var store = new ReviewStore(repository);
            store.SaveAsync(new Review { Snapshots = [previous], LastSnapshotId = previous.Id }).GetAwaiter().GetResult();
            var loaded = store.LoadReviews().Single().Snapshots[0];
            var next = CreateSnapshot("inserted\na\nb\ntarget\nc\nd\ne\nf\n");
            ReviewMigration.MigrateComments(loaded, next, hash => next.Contents.TryGetValue(hash, out var text) ? text : null);
            Check(next.Files[0].Comments[0].Anchor.StartLine == 4 && next.Files[0].Comments[0].Anchor.Attachment == ReviewAttachment.Attached,
                "Comment migration must work from recorded context without persisted full source files.");
        }

        private static void CheckOnlyRelevantFilesAndCancelObsoleteLoads()
        {
            RunAsync(async () =>
            {
                var repository = CreateRepository();
                File.WriteAllText(Path.Combine(repository, "first.txt"), "first\n");
                File.WriteAllText(Path.Combine(repository, "other.txt"), "other\n");
                var capture = new CaptureReview(repository);
                var snapshot = await capture.CaptureAsync(ReviewSourceKind.Unstaged);
                var selected = snapshot.Files.Single(x => x.Path == "first.txt");
                await capture.LoadFileAsync(snapshot, selected);
                File.WriteAllText(Path.Combine(repository, "other.txt"), "unrelated edits\n");
                Check(!await capture.HaveFilesChangedAsync(snapshot, [selected]), "Source polling must ignore unrelated file edits.");
                File.WriteAllText(Path.Combine(repository, "first.txt"), "changed selection\n");
                Check(await capture.HaveFilesChangedAsync(snapshot, [selected]), "Source polling must notice edits to the reviewed or selected file.");
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                var cancelled = false;
                try
                {
                    await capture.LoadFileAsync(snapshot, snapshot.Files.Single(x => x.Path == "other.txt"), cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                Check(cancelled, "Obsolete file loads must honor cancellation.");
            });
        }

        private static void ConvertLegacyReviewsWithoutLosingNotes()
        {
            var repository = CreateDirectory();
            var snapshot = CreateSnapshot("a\nb\nc\n");
            var reviewId = Guid.NewGuid().ToString("N");
            var root = Path.Combine(repository, Review.DirectoryName, reviewId);
            var snapshots = Path.Combine(root, "snapshots", snapshot.Id);
            Directory.CreateDirectory(snapshots);
            var file = snapshot.Files[0];
            var anchor = new XElement("anchor", new XAttribute("side", "new"), new XAttribute("start", 2), new XAttribute("end", 2),
                new XAttribute("attachment", "attached"), new XElement("quote", "b"));
            var origin = new XElement(anchor) { Name = "originAnchor" };
            var comment = new XElement("comment", new XAttribute("id", Guid.NewGuid().ToString("N")), new XAttribute("state", "todo"),
                new XAttribute("created", DateTime.UtcNow.ToString("O")), new XAttribute("originSnapshot", snapshot.Id), new XAttribute("originPath", file.Path),
                new XElement("body", "Legacy finding"), anchor, origin);
            var record = new XElement("file", new XAttribute("version", 1), new XAttribute("path", file.Path), new XAttribute("oldPath", file.Path),
                new XAttribute("oldContent", file.OldContent), new XAttribute("newContent", file.NewContent), new XAttribute("progress", "unreviewed"),
                new XElement("patch", file.Patch), new XElement("comments", comment));
            File.WriteAllText(Path.Combine(snapshots, Review.ComputeHash(file.Path) + ".xml"), record.ToString());
            File.WriteAllText(Path.Combine(snapshots, "snapshot.xml"), new XElement("snapshot", new XAttribute("version", 1), new XAttribute("id", snapshot.Id),
                new XElement("file", new XAttribute("path", file.Path))).ToString());
            var metadata = new XElement("review", new XAttribute("version", 1), new XAttribute("id", reviewId), new XAttribute("title", "Legacy subject"),
                new XAttribute("lastSnapshot", snapshot.Id), new XElement("general", "Legacy notes"), new XElement("snapshots",
                    new XElement("snapshot", new XAttribute("id", snapshot.Id), new XAttribute("created", DateTime.UtcNow.ToString("O")),
                        new XAttribute("source", "staged"), new XAttribute("base", "base"), new XAttribute("target", "index"), new XAttribute("fingerprint", "legacy"))));
            File.WriteAllText(Path.Combine(root, "review.xml"), metadata.ToString());
            var contents = Path.Combine(repository, Review.DirectoryName, "_content");
            Directory.CreateDirectory(contents);
            File.WriteAllText(Path.Combine(contents, file.NewContent + ".txt"), snapshot.Contents[file.NewContent]);
            var store = new ReviewStore(repository);
            var loaded = store.LoadReviews().Single();
            Check(store.LoadErrors.Count == 0 && loaded.Title == "Legacy subject" && loaded.GeneralText == "Legacy notes", "Conversion must preserve existing review subjects and general notes.");
            var finding = loaded.Snapshots[0].Files[0].Comments[0];
            Check(finding.Body == "Legacy finding" && finding.State == ReviewCommentState.ToFix && finding.OriginAnchor.ContextPatch.Length > 0,
                "Conversion must preserve comment text, workflow state, and original code context.");
            Check(Directory.GetFiles(Path.Combine(repository, Review.DirectoryName), "*", SearchOption.AllDirectories).Length == 2,
                "Conversion must leave only readable review text and compact metadata in _reviews.");
            Check(Directory.Exists(Path.Combine(repository, Review.ArchiveDirectoryName, reviewId, "snapshots")) &&
                Directory.Exists(Path.Combine(repository, Review.ArchiveDirectoryName, "_content")), "Old generated captures must be archived outside the active review folder, not destroyed.");
            Check(store.LoadReviews().Single().Snapshots[0].Files[0].Comments[0].Body == "Legacy finding", "Converted reviews must reopen without the old snapshot or content directories.");
        }

        private static void RefreshUnreviewedPreviewsWithoutChangingRecordedCode()
        {
            var repository = CreateRepository();
            var path = Path.Combine(repository, "first.txt");
            File.WriteAllText(path, "before\n");
            File.WriteAllText(Path.Combine(repository, "second.txt"), "other\n");
            var snapshot = Task.Run(() => new CaptureReview(repository).CaptureAsync(ReviewSourceKind.Unstaged)).GetAwaiter().GetResult();
            using var page = new Reviews(repository);
            page.OpenPreview(snapshot);
            AwaitFileLoad(page.FileLoadTask);
            var first = page.SelectedFile;
            var second = page.Files.Single(x => x.Path == "second.txt");
            page.SelectedFile = second;
            AwaitFileLoad(page.FileLoadTask);
            Check(!first.Data.IsLoaded && first.Data.Patch.Length == 0, "Unreviewed preview data must be released when selecting another file.");
            File.WriteAllText(path, "fresh preview\n");
            page.SelectedFile = first;
            AwaitFileLoad(page.FileLoadTask);
            Check(first.Data.Patch.Contains("+fresh preview", StringComparison.Ordinal), "Unreviewed files must reload live code when selected again.");
            page.Context.BeginFileComment();
            page.Context.Body = "record this file";
            page.SelectedFile = second;
            AwaitFileLoad(page.FileLoadTask);
            File.WriteAllText(path, "later edits\n");
            page.SelectedFile = first;
            AwaitFileLoad(page.FileLoadTask);
            Check(first.Data.Patch.Contains("+fresh preview", StringComparison.Ordinal) && !first.Data.Patch.Contains("+later edits", StringComparison.Ordinal),
                "Recorded review code must not silently reload when the working file changes.");
        }

        private static void ReopenSparseReviewsWithTheFullLiveFileList()
        {
            var repository = CreateRepository();
            File.WriteAllText(Path.Combine(repository, "first.txt"), "reviewed\n");
            File.WriteAllText(Path.Combine(repository, "other.txt"), "unreviewed\n");
            var capture = new CaptureReview(repository);
            var snapshot = Task.Run(() => capture.CaptureAsync(ReviewSourceKind.Unstaged)).GetAwaiter().GetResult();
            Task.Run(() => capture.LoadFileAsync(snapshot, snapshot.Files[0])).GetAwaiter().GetResult();
            snapshot.Files[0].Comments.Add(CreateComment(snapshot, 1));
            var review = new Review { Snapshots = [snapshot], LastSnapshotId = snapshot.Id };
            new ReviewStore(repository).SaveAsync(review).GetAwaiter().GetResult();
            using var page = new Reviews(repository);
            AwaitFileLoad(page.OpenSourceAsync(ReviewSourceKind.Unstaged, string.Empty, string.Empty));
            Check(page.Files.Count == 2 && page.CurrentReview.Id == review.Id, "Reopening a sparse review must restore the full preview list without losing recorded findings.");
            File.WriteAllText(Path.Combine(repository, "other.txt"), "unrelated newer edits\n");
            AwaitFileLoad(page.OpenSourceAsync(ReviewSourceKind.Unstaged, string.Empty, string.Empty));
            Check(page.CurrentReview.Id == review.Id && page.CurrentSnapshot.Files.Single(x => x.Path == "first.txt").Comments.Count == 1,
                "Changes to unreviewed files must not create another review for unchanged recorded work.");
        }

        private static void AwaitFileLoad(Task task)
        {
            var timeout = TimeSpan.FromSeconds(15);
            var started = Stopwatch.StartNew();
            while (!task.IsCompleted && started.Elapsed < timeout)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
            Check(task.IsCompleted, "The selected-file load did not complete.");
            task.GetAwaiter().GetResult();
        }

        private static void PreserveExactGeneralEditorTextWhileTyping()
        {
            using var page = new Reviews(CreateDirectory());
            page.OpenPreview(CreateSnapshot("a\nb\n"));
            foreach (var text in new[] { "subject", "subject\n", "subject\n\n", "subject\n\n\n", "subject\n\n\nbody\n" })
            {
                page.GeneralReviewMessage = text;
                Check(page.GeneralReviewMessage == text, "Typing must not reconstruct the editor document, insert blank lines, or move the caret.");
            }
        }

        private static void ShowBothSourceRangesForMixedComments()
        {
            using var page = new Reviews(CreateDirectory());
            page.OpenPreview(CreateSnapshot("a\nb\n"));
            var anchor = new ReviewAnchor { Side = ReviewSide.New, StartLine = 20, EndLine = 23, OldStartLine = 10, OldEndLine = 12, NewStartLine = 20, NewEndLine = 23 };
            page.Context.BeginComment(anchor);
            Check(page.Context.AnchorLabel == "Old: 10-12, New: 20-23", "Mixed targets must display both old and new source coordinates.");
        }

        private static void NavigateSectionBoundariesBeforeOtherSections()
        {
            var lines = new List<TextDiffLine>
            {
                new(TextDiffLineType.Normal, "before", 1, 1),
                new(TextDiffLineType.Deleted, "old", 2, 0),
                new(TextDiffLineType.Added, "new", 0, 2),
                new(TextDiffLineType.Normal, "between", 3, 3),
                new(TextDiffLineType.Normal, "context", 4, 4),
                new(TextDiffLineType.Added, "second", 0, 5),
                new(TextDiffLineType.Normal, "after", 5, 6),
            };
            var navigation = new BlockNavigation(lines, 0);
            Func<int, int> getOffset = line => (line - 1) * 10;
            var position = 15;
            foreach (var expected in new[] { 30, 50, 60, 60 })
            {
                position = navigation.FindBoundaryOffset(position, BlockNavigationDirection.Next, getOffset);
                Check(position == expected, "Forward navigation must visit the current section end before the next section start.");
            }
            foreach (var expected in new[] { 50, 30, 10, 10 })
            {
                position = navigation.FindBoundaryOffset(position, BlockNavigationDirection.Prev, getOffset);
                Check(position == expected, "Backward navigation must visit section boundaries in reverse without repeating a stop.");
            }
        }

        private static void EditHoveredCommentsWithoutDuplicatingFindings()
        {
            using var page = new Reviews(CreateDirectory());
            page.OpenPreview(CreateSnapshot("a\nb\nc\n"));
            var chunk = page.Diff.CreateReviewSelection(ReviewPane.Combined, 2, 2);
            page.Context.OpenComment(page.Diff.Data, chunk, false);
            page.Context.Body = "first finding";
            var existing = page.Context.SelectedComment;
            page.Context.CloseEditor();
            page.Context.SetCommentTarget(existing, false);
            Check(page.Context.CommentActionLabel == App.Text("Reviews.EditComment"), "Hovering commented lines must offer Edit comment.");
            page.Context.OpenComment(page.Diff.Data, chunk, false);
            Check(ReferenceEquals(page.Context.SelectedComment, existing) && page.Comments.Count == 1, "Editing a hovered finding must not create another comment.");
            page.Context.CloseEditor();
            page.Context.SetCommentTarget(null, true);
            Check(page.Context.CommentActionLabel == App.Text("Reviews.CommentSelection"), "Explicit selections must offer Comment on selection.");
            page.Context.OpenComment(page.Diff.Data, chunk, true);
            page.Context.Body = "second finding";
            Check(page.Comments.Count == 2, "An explicit new selection must still support another finding on the same line.");
        }

        private static bool s_reviewStylesLoaded;

        private static void EnsureReviewStyles()
        {
            if (s_reviewStylesLoaded)
                return;
            s_reviewStylesLoaded = true;
            var application = Application.Current;
            var baseUri = new Uri("avares://SourceGit/");
            application.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            foreach (var file in new[] { "Resources/Icons.axaml", "Resources/Themes.axaml", "Resources/Locales/en_US.axaml" })
                application.Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(baseUri) { Source = new Uri(baseUri, file) });
            application.Resources["Fonts.Default"] = Avalonia.Media.FontFamily.Default;
            application.Resources["Fonts.Monospace"] = Avalonia.Media.FontFamily.Default;
            application.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
            application.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(baseUri) { Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml") });
            application.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(baseUri) { Source = new Uri(baseUri, "Resources/Styles.axaml") });
        }

        private static Window OpenReviewWindow(Reviews page)
        {
            EnsureReviewStyles();
            AwaitFileLoad(page.InitializeAsync());
            var window = new Window { Width = 1400, Height = 900, Content = new SourceGit.Views.Reviews { DataContext = page } };
            window.Show();
            PumpLayout(window);
            return window;
        }

        private static void PumpLayout(Window window)
        {
            for (var i = 0; i < 3; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
        }

        private static void MeasureSharedEditorsAndPreserveTyping()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\nc\n");
            snapshot.Source = ReviewSourceKind.Commit;
            page.OpenPreview(snapshot);
            page.UseSideBySide = false;
            page.ActiveTabIndex = 2;
            var window = OpenReviewWindow(page);
            try
            {
                var view = (SourceGit.Views.Reviews)window.Content;
                var tabs = view.FindControl<TabControl>("EditorTabs");
                var editor = view.FindControl<SourceGit.Views.CommitMessageTextEditor>("GeneralReviewEditor");
                PumpLayout(window);
                Check(tabs.Bounds.Width > 500 && editor.Bounds.Width > tabs.Bounds.Width * 0.5,
                    "The general review editor must fill the docked tab panel.");
                editor.TextArea.Selection = AvaloniaEdit.Editing.Selection.Create(editor.TextArea, 0, editor.Document.TextLength);
                editor.TextArea.PerformTextInput("Subject\n\nBody");
                foreach (var text in new[] { "\n", "\n", "next line", "\n" })
                {
                    var expected = editor.Text + text;
                    editor.TextArea.PerformTextInput(text);
                    PumpLayout(window);
                    Check(editor.Text == expected && page.GeneralReviewMessage == expected && editor.CaretOffset == expected.Length,
                        "Typing must preserve exact text and caret position in the shared general-review editor.");
                }
                var presenter = view.GetVisualDescendants().OfType<SourceGit.Views.ThemedTextDiffPresenter>().First();
                presenter.CaretOffset = presenter.Document.GetLineByNumber(2).Offset;
                presenter.MoveReviewBoundary(BlockNavigationDirection.Next, Avalonia.Input.KeyModifiers.Shift);
                Check(presenter.SelectedText == "a\nb\nc\n", "Shift boundary movement must select the complete section, including its final line break.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void KeepDockedCommentEditorAcrossNavigation()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot(string.Join('\n', Enumerable.Range(1, 180).Select(x => $"line_{x}")) + "\n");
            snapshot.Source = ReviewSourceKind.Commit;
            page.OpenPreview(snapshot);
            page.UseSideBySide = false;
            var window = OpenReviewWindow(page);
            try
            {
                var view = (SourceGit.Views.Reviews)window.Content;
                var diff = view.GetVisualDescendants().OfType<SourceGit.Views.TextDiffView>().First();
                var presenter = view.GetVisualDescendants().OfType<SourceGit.Views.ThemedTextDiffPresenter>().First();
                var chunk = page.Diff.CreateReviewSelection(ReviewPane.Combined, 10, 10);
                diff.RememberPresenter(presenter);
                page.Context.OpenComment(page.Diff.Data, chunk, false);
                PumpLayout(window);
                Check(page.ActiveTabIndex == 0, "A comment target must open the Comment tab.");
                var editor = view.FindControl<SourceGit.Views.ReviewCommentEditor>("CommentEditor");
                var body = editor.FindControl<SourceGit.Views.CommitMessageTextEditor>("BodyEditor");
                body.TextArea.PerformTextInput("docked finding");
                foreach (var text in new[] { "\n", "\n", "continued" })
                {
                    var inserted = text == "\n" ? Environment.NewLine : text;
                    var expected = body.Text + inserted;
                    body.TextArea.PerformTextInput(text);
                    PumpLayout(window);
                    Check(body.Text == expected && page.Context.Body == expected && body.CaretOffset == expected.Length,
                        "Typing in the docked editor must preserve text and caret position.");
                }
                var finding = page.Context.Body;
                presenter.TextArea.Selection = AvaloniaEdit.Editing.Selection.Create(presenter.TextArea,
                    presenter.Document.GetLineByNumber(12).Offset, presenter.Document.GetLineByNumber(13).Offset);
                PumpLayout(window);
                Check(page.Context.Body.Length == 0 && page.Comments.Single().Body == finding,
                    "Moving the caret must retarget the Comment tab and keep the saved finding.");
                presenter.TextArea.ClearSelection();
                presenter.CaretOffset = presenter.Document.GetLineByNumber(11).Offset;
                PumpLayout(window);
                Check(page.Context.Body == finding,
                    "Returning the caret to the commented lines must show that finding again.");
                var scroller = presenter.FindDescendantOfType<ScrollViewer>();
                scroller.Offset = new Vector(scroller.Offset.X, presenter.TextArea.TextView.DefaultLineHeight * 50);
                PumpLayout(window);
                Check(page.Context.Body == finding,
                    "Scrolling the commented range out of view must not change the Comment tab.");
                page.Context.Body = string.Empty;
                presenter.CaretOffset = presenter.Document.GetLineByNumber(14).Offset;
                PumpLayout(window);
                Check(page.Comments.Count == 0 && !page.CurrentSnapshot.Files[0].HasReviewWork,
                    "An emptied comment must be removed when the comment target changes.");
            }
            finally
            {
                window.Close();
            }
        }

        private static (string Repository, Review Review, string Commit) CreateSparseCommitReview()
        {
            var repository = CreateRepository();
            var lines = Enumerable.Range(1, 100).Select(x => $"original_{x}").ToArray();
            File.WriteAllText(Path.Combine(repository, "annotated.txt"), string.Join('\n', lines) + "\n");
            File.WriteAllText(Path.Combine(repository, "other.txt"), "before\n");
            RunGit(repository, "add", ".");
            RunGit(repository, "commit", "-qm", "base");
            lines[4] = "commented change";
            lines[79] = "uncommented distant change";
            File.WriteAllText(Path.Combine(repository, "annotated.txt"), string.Join('\n', lines) + "\n");
            File.WriteAllText(Path.Combine(repository, "other.txt"), "after\n");
            RunGit(repository, "add", ".");
            RunGit(repository, "commit", "-qm", "review subject");
            var commit = RunGit(repository, "rev-parse", "HEAD").Trim();
            var capture = new CaptureReview(repository);
            var snapshot = Task.Run(() => capture.CaptureAsync(ReviewSourceKind.Commit, string.Empty, commit)).GetAwaiter().GetResult();
            Task.Run(() => capture.LoadFileAsync(snapshot, snapshot.Files.Single(x => x.Path == "annotated.txt"))).GetAwaiter().GetResult();
            snapshot.Files[0].Comments.Add(CreateComment(snapshot, 5));
            var review = new Review { Title = "review subject", Snapshots = [snapshot], LastSnapshotId = snapshot.Id };
            var store = new ReviewStore(repository);
            store.SaveAsync(review).GetAwaiter().GetResult();
            var recorded = store.LoadReviews().Single().Snapshots[0].Files.Single();
            Check(!recorded.Patch.Contains("uncommented distant change", StringComparison.Ordinal), "The fixture must persist only commented context, not the full diff.");
            return (repository, review, commit);
        }

        private static void ReopenSavedReviewWithAllFilesAndFullAnnotatedDiff()
        {
            var fixture = CreateSparseCommitReview();
            using var page = new Reviews(fixture.Repository);
            AwaitFileLoad(page.InitializeAsync());
            page.SelectedComment = page.Comments.Single();
            AwaitFileLoad(page.FileLoadTask);
            Check(page.Context.SelectedComment?.Id == page.Comments.Single().Data.Id, "A comment selected while its full diff loads must still open after loading completes.");
            Check(page.Files.Count == 2, "Opening a saved review must recover the complete source file list, not just persisted review items.");
            Check(page.Diff.Data.Lines.Any(x => x.Content == "uncommented distant change"), "Annotated files must show uncommented sections when opened through the saved-review path.");
        }

        private static void ReopenHistoryReviewWithUncommentedSections()
        {
            var fixture = CreateSparseCommitReview();
            using var page = new Reviews(fixture.Repository);
            AwaitFileLoad(page.OpenSourceAsync(ReviewSourceKind.Commit, string.Empty, fixture.Commit));
            AwaitFileLoad(page.FileLoadTask);
            Check(page.Files.Count == 2 && page.CurrentReview.Id == fixture.Review.Id, "History must reopen the existing review with its full file list.");
            Check(page.Diff.Data.Lines.Any(x => x.Content == "uncommented distant change"), "History must load the full annotated-file diff instead of treating a saved excerpt as complete.");
        }

        private static void OpenReviewAndSnapshotSelectorsWithFullSources()
        {
            var fixture = CreateSparseCommitReview();
            var original = fixture.Review.Snapshots[0];
            var file = original.Files[0];
            var another = new ReviewSnapshot
            {
                Id = Review.ComputeHash("another recorded version"),
                Source = original.Source,
                BaseRevision = original.BaseRevision,
                TargetRevision = original.TargetRevision,
                SourceFingerprint = original.SourceFingerprint,
                Subject = original.Subject,
                IsLoaded = true,
                Files = [new ReviewFile
                {
                    Path = file.Path,
                    OldPath = file.OldPath,
                    OldContent = file.OldContent,
                    NewContent = file.NewContent,
                    Patch = file.Patch,
                    SourceIdentity = file.SourceIdentity,
                    Comments = file.Comments.ConvertAll(x => x.Copy()),
                }],
            };
            fixture.Review.Snapshots.Add(another);
            fixture.Review.LastSnapshotId = another.Id;
            new ReviewStore(fixture.Repository).SaveAsync(fixture.Review).GetAwaiter().GetResult();
            using var page = new Reviews(fixture.Repository);
            AwaitFileLoad(page.InitializeAsync());
            AwaitFileLoad(page.FileLoadTask);
            page.OpenPreview(CreateSnapshot("temporary preview\n"));
            page.SelectedReview = page.SavedReviews.Single();
            AwaitFileLoad(page.ReviewLoadTask);
            AwaitFileLoad(page.FileLoadTask);
            Check(page.Files.Count == 2 && page.Diff.Data.Lines.Any(x => x.Content == "uncommented distant change"), "The saved-review selector must restore complete file lists and full annotated-file diffs.");
            page.SelectedSnapshot = page.CurrentReview.Snapshots[0];
            AwaitFileLoad(page.ReviewLoadTask);
            AwaitFileLoad(page.FileLoadTask);
            Check(page.Files.Count == 2 && page.Diff.Data.Lines.Any(x => x.Content == "uncommented distant change"), "The snapshot selector must use the same complete browsing path.");
        }

        private static void KeepBrowsingDataOutOfSparseStorage()
        {
            var fixture = CreateSparseCommitReview();
            using var page = new Reviews(fixture.Repository);
            AwaitFileLoad(page.InitializeAsync());
            AwaitFileLoad(page.FileLoadTask);
            page.GeneralText = "General instructions after browsing";
            page.FlushAsync().GetAwaiter().GetResult();
            var store = new ReviewStore(fixture.Repository);
            var saved = store.LoadReviews().Single();
            var recorded = saved.Snapshots[0].Files.Single();
            Check(!recorded.Patch.Contains("uncommented distant change", StringComparison.Ordinal), "Loading the full browsing diff must not expand persisted comment context.");
            Check(!ReviewExport.CreateText(page.CurrentReview, page.CurrentSnapshot).Contains("uncommented distant change", StringComparison.Ordinal), "Copy Review must not copy uncommented browsing sections.");
            var index = page.Diff.Data.Lines.FindIndex(x => x.Content == "uncommented distant change");
            page.Context.OpenComment(page.Diff.Data, new TextDiffSelectedChunk(0, 0, index, index, true, false), true);
            page.Context.Body = "New finding on the distant section";
            page.FlushAsync().GetAwaiter().GetResult();
            saved = store.LoadReviews().Single();
            Check(saved.Snapshots[0].Files.Single().Comments.Count == 2 && saved.Snapshots[0].Files.Single().Patch.Contains("uncommented distant change", StringComparison.Ordinal),
                "New comments on previously uncommented sections must retain their actual context without losing existing findings.");
            page.SelectedFile = page.Files.Single(x => x.Path == "other.txt");
            AwaitFileLoad(page.FileLoadTask);
            page.Context.BeginFileComment();
            page.Context.Body = "Finding on another file";
            page.FlushAsync().GetAwaiter().GetResult();
            saved = store.LoadReviews().Single();
            Check(saved.Snapshots[0].Files.Count == 2 && saved.Snapshots[0].Files.Single(x => x.Path == "other.txt").Comments.Count == 1,
                "Reviewing an unpersisted file after reopening must add it to the saved review.");
            Check(Directory.GetFiles(Path.Combine(fixture.Repository, Review.DirectoryName), "*", SearchOption.AllDirectories).Length == 2,
                "Complete browsing must not recreate per-file snapshot documents or full-source blobs.");
        }

        private static void KeepRecordedContextWhenTheSourceIsUnavailable()
        {
            var fixture = CreateSparseCommitReview();
            Directory.Move(Path.Combine(fixture.Repository, ".git"), Path.Combine(CreateDirectory(), "original_git"));
            using var page = new Reviews(fixture.Repository);
            AwaitFileLoad(page.InitializeAsync());
            AwaitFileLoad(page.FileLoadTask);
            Check(page.Files.Count == 1 && page.Context != null && page.Comments.Count == 1,
                "Unavailable Git objects must not prevent reading saved findings and their captured code.");
            Check(page.FileViewNotice.Length > 0 && page.Status.Contains("Recorded context only", StringComparison.Ordinal),
                "A context-only fallback must be explicit rather than pretending to be a complete file diff.");
        }

        private static void CombinePathAndCommentPresenceFilters()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\n");
            snapshot.Files[0].Path = "src/reviewed.txt";
            snapshot.Files[0].Comments.Add(CreateComment(snapshot, 1));
            snapshot.Files.Add(new ReviewFile { Path = "src/unreviewed.txt" });
            snapshot.Files.Add(new ReviewFile { Path = "generated/other.txt" });
            page.OpenPreview(snapshot);
            page.ReviewFilter = ReviewFileFilter.Reviewed;
            Check(page.Files.Count == 1 && page.Files[0].Path == "src/reviewed.txt", "Reviewed filtering must follow nonempty comments.");
            page.ReviewFilter = ReviewFileFilter.Unreviewed;
            Check(page.Files.Count == 2, "Unreviewed filtering must include every file without comments.");
            page.FileFilter = "src/";
            Check(page.Files.Count == 1 && page.Files[0].Path == "src/unreviewed.txt", "Text and comment-presence filters must combine.");
            page.Context.BeginFileComment();
            page.Context.Body = "First finding";
            Check(page.Files.Count == 1 && page.Context.IsEditing, "Adding a first comment must not remove the file while its editor is active.");
            page.Context.CloseEditor();
            Check(page.Files.Count == 0, "Closing a first comment must update the active unreviewed filter.");
            page.FileFilter = string.Empty;
            page.ReviewFilter = ReviewFileFilter.All;
            Check(page.Files.Count == 3, "All-files filtering must restore every available file.");
        }

        private static void ShowClearLabelsAndCopyablePaths()
        {
            EnsureReviewStyles();
            using var page = new Reviews(CreateDirectory());
            foreach (var source in new[] { ReviewSourceKind.Staged, ReviewSourceKind.Unstaged, ReviewSourceKind.AllLocal })
            {
                var snapshot = CreateSnapshot("a\nb\n");
                snapshot.Source = source;
                page.OpenPreview(snapshot);
                Check(page.ComparisonLabel == App.Text("Reviews.Source." + source), "Local comparisons must show their clear source names, not index/worktree internals.");
            }
            var commit = CreateSnapshot("a\nb\n");
            commit.Source = ReviewSourceKind.Commit;
            commit.Files[0].Path = "src/deep/nested/important_file.txt";
            page.OpenPreview(commit);
            var window = OpenReviewWindow(page);
            var previous = s_lifetime.MainWindow;
            s_lifetime.MainWindow = window;
            try
            {
                var view = (SourceGit.Views.Reviews)window.Content;
                Check(view.FindControl<SelectableTextBlock>("ComparisonLabel") != null && page.ComparisonLabel.Contains(" -> ", StringComparison.Ordinal),
                    "Commit ranges must remain selectable and copyable.");
                Check(page.SelectedFileFullPath == Path.GetFullPath(Path.Combine(page.GetFullPath(string.Empty), commit.Files[0].Path)),
                    "The selected-file header must expose the full absolute path.");
                var menu = view.CreateFileContextMenu(page, view.FindControl<SourceGit.Views.ChangeCollectionView>("FileList"));
                var copyPath = menu.Items.OfType<MenuItem>().First(x => (string)x.Header == App.Text("CopyPath"));
                var copyFullPath = menu.Items.OfType<MenuItem>().First(x => (string)x.Header == App.Text("CopyFullPath"));
                copyPath.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
                var clipboard = App.GetClipboardTextAsync();
                AwaitFileLoad(clipboard);
                Check(clipboard.Result == commit.Files[0].Path, "Copy Path must copy the repository-relative path.");
                copyFullPath.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
                clipboard = App.GetClipboardTextAsync();
                AwaitFileLoad(clipboard);
                Check(clipboard.Result == page.SelectedFileFullPath, "Copy Full Path must copy the absolute path.");
            }
            finally
            {
                s_lifetime.MainWindow = previous;
                window.Close();
            }
        }

        private static void TrackReviewMarksInTheFileTree()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\nc\n");
            snapshot.Files[0].Path = "tofix.txt";
            snapshot.Files[0].Comments.Add(CreateComment(snapshot, 1));
            snapshot.Files.Add(new ReviewFile { Path = "done.txt", Comments = [new ReviewComment { Body = "ok", State = ReviewCommentState.Fixed }] });
            snapshot.Files.Add(new ReviewFile { Path = "open.txt" });
            page.OpenPreview(snapshot);
            var toFix = page.Files.Single(x => x.Path == "tofix.txt");
            var done = page.Files.Single(x => x.Path == "done.txt");
            var open = page.Files.Single(x => x.Path == "open.txt");
            Check(toFix.Change.HasReviewToFix && toFix.Change.ReviewToFixCount == 1, "A file with open findings must show the fix count.");
            Check(done.Change.IsReviewedWithoutToFix && !done.Change.HasReviewToFix, "A reviewed file without open findings must show the check mark.");
            Check(!open.Change.HasReviewComments && !open.Change.IsReviewedWithoutToFix, "An unreviewed file must show no review mark.");
            Check(page.Changes.Contains(open.Change), "The tree must use the same change instances as the selection model.");
            page.SelectedChanges = [open.Change];
            page.Context.BeginFileComment();
            page.Context.Body = "new finding";
            Check(open.Change.HasReviewToFix && open.Change.ReviewToFixCount == 1, "Adding a tofix comment must update the tree mark.");
            page.Context.State = ReviewCommentState.Comment;
            Check(open.Change.IsReviewedWithoutToFix && !open.Change.HasReviewToFix, "A comment-only file must show the reviewed mark.");
        }

        private static void KeepOneActiveFileWhileSelectingMultipleFiles()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\n");
            snapshot.Files[0].Path = "first.txt";
            snapshot.Files.Add(new ReviewFile { Path = "second.txt" });
            snapshot.Files.Add(new ReviewFile { Path = "third.txt" });
            page.OpenPreview(snapshot);
            var first = page.Files.Single(x => x.Path == "first.txt");
            var second = page.Files.Single(x => x.Path == "second.txt");
            page.SelectedChanges = [first.Change];
            Check(ReferenceEquals(page.SelectedFile, first), "A single selected row must become the active file.");
            page.SelectedChanges = [first.Change, second.Change];
            Check(ReferenceEquals(page.SelectedFile, first) && page.SelectedFileItems.Count == 2,
                "Multi-select must keep the active file and report every selected file.");
            page.SelectedChanges = [second.Change];
            Check(ReferenceEquals(page.SelectedFile, second), "A new single selection must move the active file.");
            page.FileFilter = "third";
            Check(page.Files.Count == 1 && page.SelectedFile != null && page.Files[0].Path == "third.txt",
                "Filtering must keep exactly one active file from the remaining rows.");
        }

        private static void MarkFileAsOkOnlyWithoutToFixComments()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\n");
            snapshot.Files.Add(new ReviewFile { Path = "open.txt" });
            snapshot.Files.Add(new ReviewFile { Path = "blocked.txt", Comments = [new ReviewComment { Body = "fix me" }] });
            page.OpenPreview(snapshot);
            var open = page.Files.Single(x => x.Path == "open.txt");
            var blocked = page.Files.Single(x => x.Path == "blocked.txt");
            page.SelectedChanges = [open.Change];
            Check(page.CanMarkFilesAsOk, "A file without open findings must allow Mark as OK.");
            page.MarkFilesAsOk();
            var ok = open.Data.Comments.Single();
            Check(ok.Body == "OK" && ok.State == ReviewCommentState.Dismissed, "Mark as OK must store a dismissed file comment with the OK text.");
            Check(open.Change.IsReviewedWithoutToFix, "A file marked OK must count as reviewed.");
            page.ReviewFilter = ReviewFileFilter.Unreviewed;
            Check(page.Files.All(x => x.Path != "open.txt"), "A file marked OK must leave the Unreviewed filter.");
            page.ReviewFilter = ReviewFileFilter.All;
            page.SelectedChanges = [blocked.Change];
            Check(!page.CanMarkFilesAsOk && page.SelectedToFixCount == 1, "A file with open findings must block Mark as OK and report the count.");
            page.SelectedChanges = [open.Change, blocked.Change];
            Check(!page.CanMarkFilesAsOk, "A mixed selection must disable Mark as OK as a unit.");
        }

        private static void ReuseTheSingleFileComment()
        {
            using var page = new Reviews(CreateDirectory());
            page.OpenPreview(CreateSnapshot("a\nb\n"));
            page.OpenFileComment();
            Check(page.ActiveTabIndex == 1 && page.FileContext.IsEditing, "Edit file comment must open the File comment tab.");
            Check(!page.FileContext.ShowAnchorLabel && page.Context.ShowAnchorLabel,
                "The File comment tab must not repeat the whole-file anchor label.");
            page.FileContext.Body = "first comment";
            page.FileContext.CloseEditor();
            page.OpenFileComment();
            Check(page.FileContext.Body == "first comment", "Edit file comment must reopen the same file comment.");
            page.FileContext.Body = "second comment";
            page.FileContext.CloseEditor();
            var file = page.Files[0].Data;
            Check(file.Comments.Count(x => x.Anchor.Side == ReviewSide.File) == 1 && file.Comments.Single().Body == "second comment",
                "A file must keep exactly one file-level comment.");
            page.OpenFileComment();
            page.FileContext.Body = string.Empty;
            page.FileContext.CloseEditor();
            Check(!file.HasReviewWork && !page.Files[0].Change.HasReviewComments, "Clearing the file comment must return the file to unreviewed.");
        }

        private static void ShowDiffStatsForTheSelectedFile()
        {
            using var page = new Reviews(CreateDirectory());
            page.OpenPreview(CreateSnapshot("a\nb\nc\n"));
            Check(page.Diff != null && page.AddedLines == 3 && page.DeletedLines == 0 && page.Diff.Data.AddedLines == 3,
                "The diff header must report the selected file's added and deleted line counts.");
        }

        private static void ExportOnlyLoadedPatches()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\n");
            snapshot.Files.Add(new ReviewFile { Path = "recorded_only.txt", Comments = [new ReviewComment { Body = "kept" }] });
            page.OpenPreview(snapshot);
            page.SelectedFile = page.Files.Single(x => x.Path == "recorded_only.txt");
            Check(!page.CanExportPatch && page.GetSelectedPatchText().Length == 0,
                "A recorded-context-only file must not export a patch that would not apply.");
            page.SelectedFile = page.Files.Single(x => x.Path == "file.txt");
            Check(page.CanExportPatch, "A loaded source file must allow patch export.");
            var patch = page.GetSelectedPatchText();
            Check(patch.StartsWith("diff --git", StringComparison.Ordinal) && patch.Contains("+a", StringComparison.Ordinal),
                "Patch export must use the file's complete patch, not its comment context.");
        }

        private static void DeriveFileStateFromPatch()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\n");
            snapshot.Files.Add(new ReviewFile
            {
                Path = "deleted.txt",
                Patch = "diff --git a/deleted.txt b/deleted.txt\ndeleted file mode 100644\n--- a/deleted.txt\n+++ /dev/null\n@@ -1 +0,0 @@\n-old\n",
            });
            snapshot.Files.Add(new ReviewFile
            {
                Path = "renamed_new.txt",
                OldPath = "renamed_old.txt",
                Patch = "diff --git a/renamed_old.txt b/renamed_new.txt\nsimilarity index 100%\nrename from renamed_old.txt\nrename to renamed_new.txt\n",
            });
            snapshot.Files.Add(new ReviewFile
            {
                Path = "modified.txt",
                Patch = "diff --git a/modified.txt b/modified.txt\nindex 1111111..2222222 100644\n--- a/modified.txt\n+++ b/modified.txt\n@@ -1 +1 @@\n-old\n+new\n",
            });
            page.OpenPreview(snapshot);
            Check(page.Files.Single(x => x.Path == "file.txt").Change.Index == ChangeState.Added,
                "A new file patch must show the added state before the file is loaded.");
            Check(page.Files.Single(x => x.Path == "deleted.txt").Change.Index == ChangeState.Deleted,
                "A deleted file patch must show the deleted state instead of a regular modification.");
            Check(page.Files.Single(x => x.Path == "renamed_new.txt").Change.Index == ChangeState.Renamed,
                "A changed old path must show the renamed state.");
            Check(page.Files.Single(x => x.Path == "modified.txt").Change.Index == ChangeState.Modified,
                "A regular patch must show the modified state.");
        }

        private static void HideReviewMarksOnFoldersAndPlainFiles()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\n");
            snapshot.Files[0].Path = "src/alpha.txt";
            snapshot.Files[0].Comments.Add(CreateComment(snapshot, 1));
            snapshot.Files.Add(new ReviewFile { Path = "src/beta.txt" });
            page.OpenPreview(snapshot);
            var window = OpenReviewWindow(page);
            try
            {
                var view = (SourceGit.Views.Reviews)window.Content;
                var tree = view.FindControl<SourceGit.Views.ChangeCollectionView>("FileList");
                var folderMarks = tree.GetVisualDescendants()
                    .OfType<StackPanel>()
                    .Where(x => x.Classes.Contains("review_mark") &&
                        x.FindAncestorOfType<ListBoxItem>()?.DataContext is ViewModels.ChangeTreeNode { IsFolder: true })
                    .ToList();
                Check(folderMarks.Count > 0 && folderMarks.All(x => !x.IsVisible),
                    "Folder rows must never show a review mark.");
                page.ViewMode = ChangeViewMode.List;
                PumpLayout(window);
                var marks = tree.GetVisualDescendants()
                    .OfType<StackPanel>()
                    .Where(x => x.Classes.Contains("review_mark"))
                    .ToList();
                Check(marks.Count(x => x.IsVisible) == 1,
                    "Only the file with comments must show a review mark.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void KeepVisualLinesValidWhenReviewStateChanges()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\nc\n");
            snapshot.Files[0].Comments.Add(CreateComment(snapshot, 2));
            page.OpenPreview(snapshot);
            var window = OpenReviewWindow(page);
            try
            {
                var view = (SourceGit.Views.Reviews)window.Content;
                var presenter = view.GetVisualDescendants().OfType<SourceGit.Views.ThemedTextDiffPresenter>().First();
                PumpLayout(window);
                Check(presenter.TextArea.TextView.VisualLinesValid, "The diff must be laid out before the state check.");
                page.SelectedComment = page.Comments.First();
                PumpLayout(window);
                page.Context.Refresh();
                Check(presenter.TextArea.TextView.VisualLinesValid,
                    "Review state changes must not invalidate diff visual lines, which crashes AvaloniaEdit pointer handling.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void KeepFileActionsInTheMenuForRecordedSources()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\n");
            snapshot.Source = ReviewSourceKind.Commit;
            page.OpenPreview(snapshot);
            var window = OpenReviewWindow(page);
            try
            {
                var view = (SourceGit.Views.Reviews)window.Content;
                var menu = view.CreateFileContextMenu(page, view.FindControl<SourceGit.Views.ChangeCollectionView>("FileList"));
                var headers = menu.Items.OfType<MenuItem>().Select(x => x.Header?.ToString()).ToList();
                Check(headers.Contains(App.Text("Open")) && headers.Contains(App.Text("RevealFile")) &&
                    headers.Contains(App.Text("FileHistory")) && headers.Contains(App.Text("Blame") + " (HEAD-only)"),
                    "Recorded commit reviews must keep the standard file actions, including custom actions.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void SortCommentsFileLevelThenByFirstLine()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\nc\nd\ne\n");
            var line4 = CreateComment(snapshot, 4);
            line4.Body = "line4";
            var range = CreateComment(snapshot, 2);
            range.Body = "range";
            range.Anchor.EndLine = 3;
            range.Anchor.NewEndLine = 3;
            var line2 = CreateComment(snapshot, 2);
            line2.Body = "line2";
            var whole = new ReviewComment { Body = "whole", Anchor = new ReviewAnchor { Side = ReviewSide.File } };
            snapshot.Files[0].Comments.Add(line4);
            snapshot.Files[0].Comments.Add(range);
            snapshot.Files[0].Comments.Add(whole);
            snapshot.Files[0].Comments.Add(line2);
            page.OpenPreview(snapshot);
            Check(page.Comments.Select(x => x.Body).SequenceEqual(new[] { "whole", "line2", "range", "line4" }),
                "Comments must sort the whole-file comment first, then by first line, then by range length.");
        }

        private static void FilterFilesByCommentState()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\n");
            snapshot.Files[0].Path = "tofix.txt";
            snapshot.Files[0].Comments.Add(new ReviewComment { Body = "fix", State = ReviewCommentState.ToFix });
            snapshot.Files.Add(new ReviewFile { Path = "fixed.txt", Comments = [new ReviewComment { Body = "done", State = ReviewCommentState.Fixed }] });
            snapshot.Files.Add(new ReviewFile { Path = "dismissed.txt", Comments = [new ReviewComment { Body = "skip", State = ReviewCommentState.Dismissed }] });
            snapshot.Files.Add(new ReviewFile
            {
                Path = "mixed.txt",
                Comments =
                [
                    new ReviewComment { Body = "note", State = ReviewCommentState.Comment },
                    new ReviewComment { Body = "done too", State = ReviewCommentState.Fixed },
                ],
            });
            snapshot.Files.Add(new ReviewFile { Path = "none.txt" });
            page.OpenPreview(snapshot);
            page.ReviewFilter = ReviewFileFilter.ToFix;
            Check(page.Files.Count == 1 && page.Files[0].Path == "tofix.txt", "The tofix filter must match files with at least one tofix comment.");
            page.ReviewFilter = ReviewFileFilter.Fixed;
            Check(page.Files.Count == 2 && page.Files.Select(x => x.Path).OrderBy(x => x).SequenceEqual(new[] { "fixed.txt", "mixed.txt" }),
                "The fixed filter must include every file with a fixed comment.");
            page.ReviewFilter = ReviewFileFilter.Comment;
            Check(page.Files.Count == 1 && page.Files[0].Path == "mixed.txt", "The comment filter must match files with a comment-state finding.");
            page.ReviewFilter = ReviewFileFilter.Dismissed;
            Check(page.Files.Count == 1 && page.Files[0].Path == "dismissed.txt", "The dismissed filter must match dismissed findings.");
            page.ReviewFilter = ReviewFileFilter.All;
            Check(page.Files.Count == 5, "The all-files filter must restore every available file.");
        }

        private static void AvoidConsecutiveMenuSeparators()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\n");
            snapshot.Source = ReviewSourceKind.Commit;
            page.OpenPreview(snapshot);
            var window = OpenReviewWindow(page);
            try
            {
                var view = (SourceGit.Views.Reviews)window.Content;
                var menu = view.CreateFileContextMenu(page, view.FindControl<SourceGit.Views.ChangeCollectionView>("FileList"));
                var headers = menu.Items.OfType<MenuItem>().Select(x => x.Header?.ToString()).ToList();
                Check(headers.Count > 0 && headers[0] != "-" && headers[^1] != "-",
                    "The file menu must not start or end with a separator.");
                Check(!headers.Zip(headers.Skip(1)).Any(x => x.First == "-" && x.Second == "-"),
                    "The file menu must not contain two separators in a row.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void FormatCommentRowsCompactly()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\nc\nd\n");
            var comment = CreateComment(snapshot, 2);
            comment.Body = "first line\nsecond line";
            comment.Anchor.EndLine = 3;
            comment.Anchor.NewEndLine = 3;
            snapshot.Files[0].Comments.Add(comment);
            page.OpenPreview(snapshot);
            var row = page.Comments.Single();
            Check(row.FirstLine == "first line", "A comment row must show the first nonempty body line.");
            Check(row.Details == "tofix - New: 2-3", $"A comment row must combine state and location, but was: {row.Details}");
            Check(!row.Details.Contains("Attached", StringComparison.Ordinal), "Attached comments must not repeat the meaningless attached label.");
            comment.Anchor.Attachment = ReviewAttachment.NeedsAttention;
            row.Refresh();
            Check(row.Details.Contains("Needs attention", StringComparison.Ordinal),
                "A comment that needs attention must still say so in the compact row.");
        }

        private static void FocusCommentEditorAtTheEnd()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\nc\n");
            var comment = CreateComment(snapshot, 2);
            comment.Body = "first line\nsecond line";
            snapshot.Files[0].Comments.Add(comment);
            page.OpenPreview(snapshot);
            var window = OpenReviewWindow(page);
            try
            {
                var view = (SourceGit.Views.Reviews)window.Content;
                page.SelectedComment = page.Comments.Single();
                PumpLayout(window);
                var editor = view.FindControl<SourceGit.Views.ReviewCommentEditor>("CommentEditor");
                var body = editor.FindControl<SourceGit.Views.CommitMessageTextEditor>("BodyEditor");
                Check(body.Text == "first line\nsecond line" && body.TextArea.Caret.Offset == body.Text.Length,
                    "Focusing a comment editor must place the caret at the end of its text.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void ResizeEditorPanelWithSplitter()
        {
            using var page = new Reviews(CreateDirectory());
            page.OpenPreview(CreateSnapshot("a\nb\nc\n"));
            var window = OpenReviewWindow(page);
            try
            {
                var view = (SourceGit.Views.Reviews)window.Content;
                var tabs = view.FindControl<TabControl>("EditorTabs");
                var splitter = view.FindControl<GridSplitter>("EditorSplitter");
                PumpLayout(window);
                Check(splitter != null && Grid.GetRow(splitter) == 4 && double.IsNaN(tabs.Height),
                    "The editor panel must sit below a draggable splitter instead of a fixed height.");
                var initial = tabs.Bounds.Height;
                var grid = (Grid)tabs.Parent;
                grid.RowDefinitions[5].Height = new GridLength(initial + 80);
                PumpLayout(window);
                Check(tabs.Bounds.Height > initial + 40, "The editor panel must follow its resizable row.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void ShowReviewDiffContextMenu()
        {
            using var page = new Reviews(CreateDirectory());
            page.OpenPreview(CreateSnapshot("a\nb\nc\nd\n"));
            var window = OpenReviewWindow(page);
            try
            {
                var view = (SourceGit.Views.Reviews)window.Content;
                var presenter = view.GetVisualDescendants().OfType<SourceGit.Views.ThemedTextDiffPresenter>().First();
                var menu = presenter.CreateTextContextMenu(3, 4);
                var headers = menu.Items.OfType<MenuItem>().Select(x => x.Header?.ToString()).ToList();
                Check(headers.Contains(App.Text("Diff.ContextMenu.Line", 4)), "The review diff menu must keep the line header.");
                Check(headers.Contains(App.Text("Copy")), "The review diff menu must offer Copy.");
                Check(headers.Contains(App.Text("FileCM.CopyAsPatch")), "The review diff menu must offer Copy as Patch.");
                Check(headers.Contains(App.Text("OpenInExternalMergeTool")), "The review diff menu must offer the external merge tool.");
                Check(!headers.Zip(headers.Skip(1)).Any(x => x.First == "-" && x.Second == "-"),
                    "The review diff menu must not contain two separators in a row.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void SwitchTabsForCommentTargets()
        {
            using var page = new Reviews(CreateDirectory());
            page.OpenPreview(CreateSnapshot("a\nb\nc\nd\n"));
            page.ActiveTabIndex = 2;
            var chunk = page.Diff.CreateReviewSelection(ReviewPane.Combined, 2, 2);
            page.Context.OpenComment(page.Diff.Data, chunk, false);
            Check(page.ActiveTabIndex == 0 && page.Context.IsEditing, "A comment target must focus the Comment tab.");
            page.Context.Body = "finding";
            page.Context.CloseEditor();
            page.ActiveTabIndex = 2;
            page.SelectedComment = page.Comments.Single();
            Check(page.ActiveTabIndex == 0 && page.Context.Body == "finding", "Selecting a comment must show it in the Comment tab.");
            page.ActiveTabIndex = 2;
            page.OpenFileComment();
            Check(page.ActiveTabIndex == 1 && page.FileContext.IsEditing, "Edit file comment must focus the File comment tab.");
        }

        private static void SeparateChangedLocalSourcesFromRecordedContext()
        {
            var repository = CreateRepository();
            var path = Path.Combine(repository, "file.txt");
            var lines = Enumerable.Range(1, 100).Select(x => $"line_{x}").ToArray();
            File.WriteAllText(path, string.Join('\n', lines) + "\n");
            RunGit(repository, "add", ".");
            RunGit(repository, "commit", "-qm", "base");
            lines[4] = "recorded finding location";
            lines[79] = "original unreviewed section";
            File.WriteAllText(path, string.Join('\n', lines) + "\n");
            var capture = new CaptureReview(repository);
            var snapshot = Task.Run(() => capture.CaptureAsync(ReviewSourceKind.AllLocal)).GetAwaiter().GetResult();
            Task.Run(() => capture.LoadFileAsync(snapshot, snapshot.Files[0])).GetAwaiter().GetResult();
            snapshot.Files[0].Comments.Add(CreateComment(snapshot, 5));
            new ReviewStore(repository).SaveAsync(new Review { Snapshots = [snapshot], LastSnapshotId = snapshot.Id }).GetAwaiter().GetResult();
            using (var unchanged = new Reviews(repository))
            {
                AwaitFileLoad(unchanged.InitializeAsync());
                AwaitFileLoad(unchanged.FileLoadTask);
                Check(unchanged.Diff.Data.Lines.Any(x => x.Content == "original unreviewed section") && unchanged.FileViewNotice.Length == 0,
                    "Unchanged local reviews must recover complete browsing diffs, not only their saved findings.");
            }
            lines[79] = "later local edits";
            File.WriteAllText(path, string.Join('\n', lines) + "\n");
            using var page = new Reviews(repository);
            AwaitFileLoad(page.InitializeAsync());
            AwaitFileLoad(page.FileLoadTask);
            Check(page.FileViewNotice.Length > 0 && !page.Diff.Data.Lines.Any(x => x.Content == "later local edits"),
                "Changed local sources must not silently replace recorded code or place old comments on unverified current lines.");
            AwaitFileLoad(page.CheckSourceAsync());
            Check(page.SourceChanged, "A context-only view of changed local code must keep Update snapshot available.");
            AwaitFileLoad(page.UpdateSnapshot());
            AwaitFileLoad(page.FileLoadTask);
            Check(page.Diff.Data.Lines.Any(x => x.Content == "later local edits") && page.Comments.Count == 1 && page.FileViewNotice.Length == 0,
                "An explicit update must load the complete new diff and conservatively preserve existing findings.");
        }

        private static void ReviewedFilteringMeansAnyNonemptyComment()
        {
            using var page = new Reviews(CreateDirectory());
            var snapshot = CreateSnapshot("a\nb\n");
            foreach (var state in Enum.GetValues<ReviewCommentState>())
                snapshot.Files.Add(new ReviewFile { Path = state + ".txt", Comments = [new ReviewComment { Body = "finding", State = state }] });
            snapshot.Files.Add(new ReviewFile { Path = "empty.txt", Comments = [new ReviewComment { Body = "  \n" }] });
            page.OpenPreview(snapshot);
            page.ReviewFilter = ReviewFileFilter.Reviewed;
            Check(page.Files.Count == 4, "Reviewed must mean at least one nonempty comment in any workflow state.");
            page.ReviewFilter = ReviewFileFilter.Unreviewed;
            Check(page.Files.Count == 2, "Unreviewed must mean no nonempty comments, without a separate manual file mark.");
        }

        private static void OpenEmptyReviewsWithoutStartingASource()
        {
            var repository = CreateRepository();
            using var page = new Reviews(repository);
            AwaitFileLoad(page.InitializeAsync());
            Check(!page.HasReview && page.CurrentSnapshot == null && page.Files.Count == 0,
                "Opening an empty Reviews page must not silently create a staged preview.");
            Check(!Directory.Exists(Path.Combine(repository, Review.DirectoryName)), "The empty Reviews page must not create review data.");
        }

        private static void KeepReviewActionsOutsideTheSearchEditor()
        {
            EnsureReviewStyles();
            var view = new SourceGit.Views.WorkingCopy();
            var window = new Window { Width = 1000, Height = 700, Content = view };
            window.Show();
            try
            {
                PumpLayout(window);
                var search = view.FindControl<TextBox>("LocalChangesSearchBox");
                var button = view.FindControl<Button>("LocalChangesReviewsButton");
                var clear = view.FindControl<Button>("LocalChangesClearFilterButton");
                search.Text = new string('x', 200);
                clear.IsVisible = true;
                foreach (var width in new[] { 1000, 700 })
                {
                    window.Width = width;
                    PumpLayout(window);
                    var searchOrigin = search.TranslatePoint(default, view).Value;
                    var buttonOrigin = button.TranslatePoint(default, view).Value;
                    Check(search.Bounds.Width > 100 && button.Bounds.Width > 0 && buttonOrigin.X >= searchOrigin.X + search.Bounds.Width,
                        "The Reviews button must have its own right-hand layout column, even with long search text and a narrow panel.");
                    Check(ReferenceEquals(clear.FindAncestorOfType<TextBox>(), search), "The clear-search button must remain inside the search editor.");
                }
                button.Flyout.ShowAt(button);
                PumpLayout(window);
                var actions = ((MenuFlyout)button.Flyout).Items.OfType<MenuItem>().Select(x => x.Header?.ToString()).ToArray();
                Check(actions.SequenceEqual(new[] { "Review Staged Changes", "Review Unstaged Changes", "Review All Local Changes" }),
                    $"Local-change review action labels: {string.Join(" | ", actions)}.");
                button.Flyout.Hide();
            }
            finally
            {
                window.Close();
            }
        }

        private static void DoNotExposeSourceCreationControlsInsideReviews()
        {
            using var page = new Reviews(CreateDirectory());
            page.OpenPreview(CreateSnapshot("a\nb\n"));
            var view = new SourceGit.Views.Reviews { DataContext = page };
            Check(!view.GetLogicalDescendants().OfType<ComboBox>().Any(x => x.ItemsSource?.GetType() == typeof(ReviewSourceKind[])),
                "Reviews must not contain another source-creation selector.");
            Check(view.GetLogicalDescendants().OfType<CheckBox>().Count() == 1,
                "Reviews must retain the diff layout toggle without adding a separate file-reviewed checkbox.");
            Check(view.FindControl<SourceGit.Views.ChangeCollectionView>("FileList") != null,
                "Reviews must use the regular change file list with its view modes and context menus.");
            Check(view.FindControl<TabControl>("EditorTabs") != null,
                "Reviews must keep its comment editing in one docked tab panel.");
            var filterBox = view.FindControl<TextBox>("FileFilterBox");
            Check(filterBox != null && filterBox.CornerRadius.TopLeft == 12 && filterBox.InnerLeftContent != null,
                "The file filter must use the rounded search box used by Local changes.");
        }

        private static void RemovingTheLastCommentMakesTheFileUnreviewed()
        {
            var repository = CreateDirectory();
            using var page = new Reviews(repository);
            page.OpenPreview(CreateSnapshot("a\nb\n"));
            page.Context.BeginFileComment();
            page.Context.Body = "Only finding";
            page.Context.CloseEditor();
            page.FlushAsync().GetAwaiter().GetResult();
            page.ReviewFilter = ReviewFileFilter.Reviewed;
            page.Context.EditComment(page.Comments.Single().Data);
            page.Context.Body = string.Empty;
            Check(page.Files.Count == 1, "Clearing a comment must not remove the selected file before the editor closes.");
            page.Context.CloseEditor();
            Check(page.Files.Count == 0, "Removing the last nonempty comment must remove the file from Reviewed.");
            page.ReviewFilter = ReviewFileFilter.Unreviewed;
            Check(page.Files.Count == 1 && !page.Files[0].Data.HasReviewWork, "A file without comments must become Unreviewed.");
            page.FlushAsync().GetAwaiter().GetResult();
            Check(new ReviewStore(repository).LoadReviews().Single().Snapshots[0].Files.Count == 0,
                "An emptied file must not retain saved code or a separate reviewed mark.");
        }

        private static void DiscardObsoleteFileMarksWithoutLosingComments()
        {
            var repository = CreateDirectory();
            var snapshot = CreateSnapshot("a\nb\n");
            snapshot.Files[0].Comments.Add(CreateComment(snapshot, 1));
            snapshot.Files.Add(new ReviewFile { Path = "mark_only.txt", Comments = [new ReviewComment { Body = "temporary fixture note" }] });
            var review = new Review { Title = "Keep this subject", GeneralText = "Keep these notes", Snapshots = [snapshot], LastSnapshotId = snapshot.Id };
            var store = new ReviewStore(repository);
            store.SaveAsync(review).GetAwaiter().GetResult();
            var path = Path.Combine(repository, Review.DirectoryName, review.Id, "review.xml");
            var xml = XElement.Load(path, LoadOptions.PreserveWhitespace);
            foreach (var file in xml.Descendants("file"))
            {
                file.SetAttributeValue("progress", "obsolete reviewed mark");
                if ((string)file.Attribute("path") == "mark_only.txt")
                    file.Element("comments").RemoveNodes();
            }
            File.WriteAllText(path, xml.ToString());
            var loaded = store.LoadReviews().Single();
            Check(loaded.Title == review.Title && loaded.GeneralText == review.GeneralText && loaded.Snapshots[0].Files.Count == 1 &&
                loaded.Snapshots[0].Files[0].Comments.Single().Body == "finding", "Obsolete marks must be ignored without losing comments, subjects, or general notes.");
            store.SaveAsync(loaded).GetAwaiter().GetResult();
            Check(!XElement.Load(path).Descendants("file").Any(x => x.Attribute("progress") != null), "Saving must remove obsolete progress attributes instead of retaining compatibility fields.");
        }

        private static void LoadReviewControls()
        {
            var editor = new SourceGit.Views.ReviewCommentEditor();
            Check(editor.FindControl<SourceGit.Views.CommitMessageTextEditor>("BodyEditor").ShowSubjectGuide == false,
                "Comments must use the shared editor without commit-subject decorations.");
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
                SourceFingerprint = hash,
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
