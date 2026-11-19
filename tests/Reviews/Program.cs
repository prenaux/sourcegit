using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.VisualTree;

using SourceGit.Models;
using SourceGit.ViewModels;

namespace SourceGit.ReviewTests
{
    internal static class Program
    {
        private const double MaxReviewTextTopGap = 16;
        private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(30);
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
            CommitGraph.SetDefaultPens();
            LoadApplicationResources();
            Preferences.Instance.UseSideBySideDiff = false;
            Preferences.Instance.UseFullTextDiff = false;
            Preferences.Instance.IgnoreWhitespaceChangesInDiff = false;

            var tests = new (string Name, Action Run)[]
            {
                (nameof(CreateAnchorsForSelectedRanges), CreateAnchorsForSelectedRanges),
                (nameof(KeepContextPatchSmallInsideLargeHunks), KeepContextPatchSmallInsideLargeHunks),
                (nameof(AttachUnchangedComments), AttachUnchangedComments),
                (nameof(FollowMovedCodeInTheDiff), FollowMovedCodeInTheDiff),
                (nameof(FlagChangedCodeWithoutMovingTheComment), FlagChangedCodeWithoutMovingTheComment),
                (nameof(RefuseAmbiguousMatches), RefuseAmbiguousMatches),
                (nameof(UseContextToFindRepeatedCode), UseContextToFindRepeatedCode),
                (nameof(MarkCommentsOutsideTheDiff), MarkCommentsOutsideTheDiff),
                (nameof(KeepRecordedStateForPartialDiffs), KeepRecordedStateForPartialDiffs),
                (nameof(RoundTripReviewStorage), RoundTripReviewStorage),
                (nameof(ReadTheLastSnapshotOfVersionTwoReviews), ReadTheLastSnapshotOfVersionTwoReviews),
                (nameof(RejectExternalEntitiesAndStorageLinks), RejectExternalEntitiesAndStorageLinks),
                (nameof(ExportActiveCommentsToTheClipboard), ExportActiveCommentsToTheClipboard),
                (nameof(SaveOnlyAfterReviewInteraction), SaveOnlyAfterReviewInteraction),
                (nameof(ArchiveDeletedReviews), ArchiveDeletedReviews),
                (nameof(ListSavedReviews), ListSavedReviews),
                (nameof(UseOneLocalReviewUntilANewOneStarts), UseOneLocalReviewUntilANewOneStarts),
                (nameof(FindCommitReviewsByRevision), FindCommitReviewsByRevision),
                (nameof(CommentOnSelectedRangesWithToFixDefault), CommentOnSelectedRangesWithToFixDefault),
                (nameof(EditTheSameRangeWithoutDuplicates), EditTheSameRangeWithoutDuplicates),
                (nameof(RemoveEmptyCommentsWhenTheTargetChanges), RemoveEmptyCommentsWhenTheTargetChanges),
                (nameof(KeepFileCommentTargetWhenTheSelectedFileChanges), KeepFileCommentTargetWhenTheSelectedFileChanges),
                (nameof(ReuseTheSingleFileComment), ReuseTheSingleFileComment),
                (nameof(KeepEditorTargetsWhenTheHostSelectsTheSameFileAgain), KeepEditorTargetsWhenTheHostSelectsTheSameFileAgain),
                (nameof(MarkFileAsOkWithTheCommentState), MarkFileAsOkWithTheCommentState),
                (nameof(SetCommentsStateForAllSelectedFiles), SetCommentsStateForAllSelectedFiles),
                (nameof(TrackReviewMarksOnChanges), TrackReviewMarksOnChanges),
                (nameof(FilterFilesByCommentState), FilterFilesByCommentState),
                (nameof(FollowRenamedFiles), FollowRenamedFiles),
                (nameof(SortCommentsFileLevelThenByFirstLine), SortCommentsFileLevelThenByFirstLine),
                (nameof(MoveCommentToSelectedLines), MoveCommentToSelectedLines),
                (nameof(SplitGeneralMessageIntoTitleAndNotes), SplitGeneralMessageIntoTitleAndNotes),
                (nameof(ReplaceDeletedReviewInTheSession), ReplaceDeletedReviewInTheSession),
                (nameof(AllowStagingAndCommentsInTheSameDiff), AllowStagingAndCommentsInTheSameDiff),
                (nameof(NavigateSectionBoundariesBeforeOtherSections), NavigateSectionBoundariesBeforeOtherSections),
                (nameof(MoveCaretToBoundariesWithoutAReview), MoveCaretToBoundariesWithoutAReview),
                (nameof(RevealSelectedCommentInTheDiff), RevealSelectedCommentInTheDiff),
                (nameof(GotoNextAndPreviousComment), GotoNextAndPreviousComment),
                (nameof(TypeCommentsInTheReviewPanel), TypeCommentsInTheReviewPanel),
                (nameof(ShowCommitTabOnlyWithCommitContent), ShowCommitTabOnlyWithCommitContent),
                (nameof(LoadReviewControls), LoadReviewControls),
                (nameof(KeepCommentsWhenAFileIsStaged), KeepCommentsWhenAFileIsStaged),
                (nameof(ReviewAndSaveARangeOfCommits), ReviewAndSaveARangeOfCommits),
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

        private static void CreateAnchorsForSelectedRanges()
        {
            var diff = CreateDiff("@@ 1 1", " before", "-old one", "-old two", "+new one", " after");

            var added = CreateCapturedAnchor(diff, 4, 4, ReviewPane.Combined);
            Check(added.Side == ReviewSide.New && added.NewStartLine == 2 && added.NewEndLine == 2 && !added.HasOldRange,
                "A selection of added lines must anchor to the new side only.");
            Check(added.Quote == "new one" && added.ContextText == "before\nnew one\nafter" && added.ContextStartLine == 1,
                "An anchor must record its lines and the lines around them.");

            var deleted = CreateCapturedAnchor(diff, 2, 3, ReviewPane.Combined);
            Check(deleted.Side == ReviewSide.Old && deleted.OldStartLine == 2 && deleted.OldEndLine == 3 && !deleted.HasNewRange,
                "A selection of deleted lines must anchor to the old side.");
            Check(deleted.Quote == "old one\nold two", "An old-side anchor must quote the deleted lines.");

            var mixed = CreateCapturedAnchor(diff, 2, 4, ReviewPane.Combined);
            Check(mixed.Side == ReviewSide.New && mixed.HasOldRange && mixed.OldStartLine == 2 && mixed.OldEndLine == 3 && mixed.NewStartLine == 2,
                "A selection of deleted and added lines must keep both ranges.");

            var context = CreateCapturedAnchor(diff, 1, 1, ReviewPane.Combined);
            Check(context.Side == ReviewSide.New && context.NewStartLine == 1 && !context.HasOldRange,
                "A context line must anchor to the new side only.");

            var oldPane = CreateCapturedAnchor(diff, 1, 4, ReviewPane.Old);
            Check(oldPane.Side == ReviewSide.Old && oldPane.OldStartLine == 1 && oldPane.OldEndLine == 3 && !oldPane.HasNewRange,
                "A selection in the old pane must ignore the added lines.");

            Check(CreateCapturedAnchor(diff, 0, 0, ReviewPane.Combined) == null, "A hunk header is not a comment target.");
            Check(mixed.ContextPatch.StartsWith("@@ -1,4 +1,3 @@\n before\n-old one\n", StringComparison.Ordinal),
                "The recorded patch must be one hunk with correct line counts.");
        }

        private static void KeepContextPatchSmallInsideLargeHunks()
        {
            var diff = CreateAddedFileDiff(200);
            var anchor = CreateCapturedAnchor(diff, 100, 100, ReviewPane.Combined);
            var rows = ReviewAnchors.SplitLines(anchor.ContextPatch);
            Check(rows.Length == 2 * ReviewAnchors.ContextLineCount + 2 && rows[0] == "@@ -0,0 +96,9 @@",
                "A line comment must record a bounded context, even inside a large hunk.");
            Check(ReviewAnchors.SplitLines(anchor.ContextText).Length == 2 * ReviewAnchors.ContextLineCount + 1,
                "The recorded context lines must stay bounded.");
        }

        private static void AttachUnchangedComments()
        {
            var diff = CreateDiff("@@ 1 1", " a", "+b", " c");
            var file = CreateFileWithComment(diff, 2);
            var located = new List<ReviewComment>();
            var changed = ReviewAnchors.AttachComments(file, diff, ReviewAttachPolicy.Authoritative, located);
            Check(!changed && located.Count == 1 && file.Comments[0].Anchor.Attachment == ReviewAttachment.Attached,
                "A comment on unchanged code must stay attached without a write.");
        }

        private static void FollowMovedCodeInTheDiff()
        {
            var file = CreateFileWithComment(CreateDiff("@@ 1 1", " a", "+target", " c"), 2);
            var next = CreateDiff("@@ 1 1", "+inserted one", "+inserted two", " a", "+target", " c");
            var located = new List<ReviewComment>();
            var changed = ReviewAnchors.AttachComments(file, next, ReviewAttachPolicy.Authoritative, located);
            var anchor = file.Comments[0].Anchor;
            Check(changed && located.Count == 1 && anchor.NewStartLine == 4 && anchor.NewEndLine == 4 && anchor.Attachment == ReviewAttachment.Attached,
                "A comment must follow its code when lines are inserted above it.");
            Check(anchor.ContextStartLine == 1 && anchor.ContextPatch.Contains("+target", StringComparison.Ordinal),
                "A moved comment must record its new context.");
        }

        private static void FlagChangedCodeWithoutMovingTheComment()
        {
            var file = CreateFileWithComment(CreateDiff("@@ 1 1", " a", "+target", " c"), 2);
            var original = file.Comments[0].Anchor.Copy();
            var located = new List<ReviewComment>();
            var changed = ReviewAnchors.AttachComments(file, CreateDiff("@@ 1 1", " a", "+rewritten", " c"), ReviewAttachPolicy.Authoritative, located);
            var anchor = file.Comments[0].Anchor;
            Check(changed && located.Count == 0 && anchor.Attachment == ReviewAttachment.NeedsAttention,
                "A comment on rewritten code must need attention.");
            Check(anchor.NewStartLine == original.NewStartLine && anchor.Quote == "target" && anchor.ContextPatch == original.ContextPatch,
                "A comment that needs attention must keep its last known location and code.");
        }

        private static void RefuseAmbiguousMatches()
        {
            var file = CreateFileWithComment(CreateDiff("@@ 1 1", " a", "+}", " c"), 2);
            var located = new List<ReviewComment>();
            ReviewAnchors.AttachComments(file, CreateDiff("@@ 1 1", "+x", "+}", "+y", "+}", "+z"), ReviewAttachPolicy.Authoritative, located);
            Check(located.Count == 1 && file.Comments[0].Anchor.NewStartLine == 2,
                "Code that is still at its recorded line must stay attached there.");

            file = CreateFileWithComment(CreateDiff("@@ 1 1", " a", "+}", " c"), 2);
            located.Clear();
            ReviewAnchors.AttachComments(file, CreateDiff("@@ 1 1", "+x", "+y", "+}", "+z", "+}"), ReviewAttachPolicy.Authoritative, located);
            Check(located.Count == 0 && file.Comments[0].Anchor.Attachment == ReviewAttachment.NeedsAttention,
                "A comment must not move when several places match and none keeps its context.");
        }

        private static void UseContextToFindRepeatedCode()
        {
            var file = CreateFileWithComment(CreateDiff("@@ 1 1", " first", "+}", " second"), 2);
            var located = new List<ReviewComment>();
            ReviewAnchors.AttachComments(file, CreateDiff("@@ 1 1", "+other", "+}", "+more", " first", "+}", " second"), ReviewAttachPolicy.Authoritative, located);
            Check(located.Count == 1 && file.Comments[0].Anchor.NewStartLine == 5,
                "The recorded context must select the right place among repeated lines.");
        }

        private static void MarkCommentsOutsideTheDiff()
        {
            var file = CreateFileWithComment(CreateDiff("@@ 10 10", " a", "+target", " c"), 2);
            var located = new List<ReviewComment>();
            ReviewAnchors.AttachComments(file, CreateDiff("@@ 40 40", " x", "+y", " z"), ReviewAttachPolicy.Authoritative, located);
            Check(located.Count == 0 && file.Comments[0].Anchor.Attachment == ReviewAttachment.OutsideDiff,
                "A comment whose lines the diff does not show must be outside the current diff.");
        }

        private static void KeepRecordedStateForPartialDiffs()
        {
            var file = CreateFileWithComment(CreateDiff("@@ 1 1", " a", "+target", " c"), 2);
            var located = new List<ReviewComment>();
            var changed = ReviewAnchors.AttachComments(file, CreateDiff("@@ 1 1", " a", "+rewritten", " c"), ReviewAttachPolicy.Partial, located);
            Check(!changed && located.Count == 0 && file.Comments[0].Anchor.Attachment == ReviewAttachment.Attached,
                "A partial diff must not change the state of a comment that it does not show.");
        }

        private static void RoundTripReviewStorage()
        {
            var directory = CreateDirectory();
            var review = new Review
            {
                Title = "Title",
                GeneralText = "Notes\nsecond line",
                Source = ReviewSourceKind.Comparison,
                BaseRevision = "aaa",
                TargetRevision = "bbb",
            };
            var file = new ReviewFile { Path = "src/file.txt", OldPath = "src/old.txt" };
            file.Comments.Add(new ReviewComment
            {
                Body = "line finding",
                State = ReviewCommentState.Fixed,
                Anchor = new ReviewAnchor
                {
                    Side = ReviewSide.New,
                    OldStartLine = 2,
                    OldEndLine = 2,
                    NewStartLine = 3,
                    NewEndLine = 4,
                    Attachment = ReviewAttachment.NeedsAttention,
                    Quote = "a\nb",
                    ContextText = "x\na\nb\ny",
                    ContextStartLine = 2,
                    ContextPatch = "@@ -2,1 +2,3 @@\n x\n+a\n+b\n",
                },
            });
            file.Comments.Add(new ReviewComment { Body = "control \u0001 character", Anchor = new ReviewAnchor { Side = ReviewSide.File } });
            file.Comments.Add(new ReviewComment { Body = "  ", Anchor = new ReviewAnchor { Side = ReviewSide.New, NewStartLine = 9, NewEndLine = 9 } });
            review.Files.Add(file);
            review.Files.Add(new ReviewFile { Path = "untouched.txt" });
            new ReviewStore(directory).Save(review);

            var store = new ReviewStore(directory);
            var loaded = store.LoadReviews().Single();
            Check(store.LoadErrors.Count == 0 && loaded.IsSaved && loaded.Id == review.Id && loaded.Title == "Title" && loaded.GeneralText == "Notes\nsecond line",
                "A saved review must load with its title and notes.");
            Check(loaded.Source == ReviewSourceKind.Comparison && loaded.BaseRevision == "aaa" && loaded.TargetRevision == "bbb",
                "A saved review must keep its source.");
            Check(loaded.Files.Count == 1 && loaded.Files[0].OldPath == "src/old.txt" && loaded.Files[0].Comments.Count == 2,
                "Only files and comments with text must be stored.");
            var anchor = loaded.Files[0].Comments[0].Anchor;
            Check(loaded.Files[0].Comments[0].State == ReviewCommentState.Fixed && anchor.Side == ReviewSide.New &&
                anchor.OldStartLine == 2 && anchor.OldEndLine == 2 && anchor.NewStartLine == 3 && anchor.NewEndLine == 4 &&
                anchor.Attachment == ReviewAttachment.NeedsAttention && anchor.Quote == "a\nb" && anchor.ContextText == "x\na\nb\ny" &&
                anchor.ContextStartLine == 2 && anchor.ContextPatch == "@@ -2,1 +2,3 @@\n x\n+a\n+b\n",
                "A saved anchor must load unchanged.");
            Check(loaded.Files[0].Comments[1].Body == "control \u0001 character", "Text that XML cannot hold must survive storage.");

            var document = File.ReadAllText(Path.Combine(directory, "_reviews", review.Id, "review.md"));
            Check(document.Contains("line finding", StringComparison.Ordinal) && document.Contains("### fixed", StringComparison.Ordinal),
                "The readable document must contain every finding.");
        }

        private static void ReadTheLastSnapshotOfVersionTwoReviews()
        {
            var directory = CreateDirectory();
            var id = Guid.NewGuid().ToString("N");
            var root = Path.Combine(directory, "_reviews", id);
            Directory.CreateDirectory(root);
            string CreateSnapshot(string snapshot, string start, string extra) =>
                $"<snapshot id='{snapshot}' created='2026-11-14T10:00:00.0000000Z' source='staged' base='HEAD' target='index' fingerprint='f' subject='s'>" +
                "<file path='a.txt' oldPath='' oldContent='' newContent='' identity='' binary='false' outsideDiff='false' captureHash='x'><patch></patch><comments>" +
                "<comment id='c1' state='tofix' created='2026-11-14T10:00:00.0000000Z' originSnapshot='s1' originPath='a.txt'><body>first</body>" +
                $"<anchor side='new' start='{start}' end='{start}' oldStart='0' oldEnd='0' newStart='0' newEnd='0' attachment='attached' contextStart='1' canMap='true'>" +
                "<quote>q</quote><context>c</context><patch>p</patch></anchor>" +
                "<originAnchor side='new' start='3' end='3' attachment='attached'><quote>q</quote></originAnchor></comment>" +
                extra + "</comments></file></snapshot>";
            var second = "<comment id='c2' state='dismissed' created='2026-11-15T10:00:00.0000000Z' originSnapshot='s2' originPath='a.txt'><body>second</body>" +
                "<anchor side='file' start='0' end='0' attachment='attached'/><originAnchor side='file' attachment='attached'/></comment>";
            File.WriteAllText(Path.Combine(root, "review.xml"),
                $"<review version='2' id='{id}' title='Old review' lastSnapshot='s2' generalEdited='true'><general>notes</general><snapshots>" +
                CreateSnapshot("s1", "3", string.Empty) + CreateSnapshot("s2", "5", second) + "</snapshots></review>");

            var store = new ReviewStore(directory);
            var review = store.LoadReviews().SingleOrDefault();
            Check(review != null && store.LoadErrors.Count == 0, "A version 2 review must load: " + string.Join("; ", store.LoadErrors));
            Check(review.Title == "Old review" && review.GeneralText == "notes" && review.Source == ReviewSourceKind.Local,
                "A version 2 review of local changes must become a local review.");
            var comments = review.Files.Single().Comments;
            Check(comments.Count == 2 && comments[0].Anchor.NewStartLine == 5 && comments[0].Anchor.Quote == "q" &&
                comments[1].State == ReviewCommentState.Dismissed && comments[1].Anchor.Side == ReviewSide.File,
                "The comments of the last snapshot must load with their ranges.");
        }

        private static void RejectExternalEntitiesAndStorageLinks()
        {
            var directory = CreateDirectory();
            var id = Guid.NewGuid().ToString("N");
            var root = Path.Combine(directory, "_reviews", id);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "review.xml"), $"<!DOCTYPE review [<!ENTITY external SYSTEM 'file:///etc/passwd'>]><review version='3' id='{id}'><general>&external;</general></review>");
            var store = new ReviewStore(directory);
            Check(store.LoadReviews().Count == 0 && store.LoadErrors.Count == 1, "Review XML must reject external entities.");

            var linked = CreateDirectory();
            var outside = CreateDirectory();
            Directory.CreateSymbolicLink(Path.Combine(linked, "_reviews"), outside);
            var failed = false;
            try
            {
                new ReviewStore(linked).Save(new Review { Title = "linked" });
            }
            catch (InvalidDataException)
            {
                failed = true;
            }
            Check(failed && !Directory.EnumerateFileSystemEntries(outside).Any(), "Review writes must not follow storage links.");
        }

        private static void ExportActiveCommentsToTheClipboard()
        {
            var review = new Review { Title = "Title", Source = ReviewSourceKind.Commit, BaseRevision = "aaa", TargetRevision = "bbb" };
            var diff = CreateDiff("@@ 1 1", " a", "+b", " c");
            var file = new ReviewFile { Path = "file.txt" };
            foreach (var state in Enum.GetValues<ReviewCommentState>())
            {
                var comment = new ReviewComment { Body = "body_" + state, State = state, Anchor = CreateCapturedAnchor(diff, 2, 2, ReviewPane.Combined) };
                file.Comments.Add(comment);
            }
            file.Comments[1].Anchor.Attachment = ReviewAttachment.NeedsAttention;
            review.Files.Add(file);

            var text = ReviewExport.CreateText(review);
            Check(text.Contains("body_Comment", StringComparison.Ordinal) && text.Contains("body_ToFix", StringComparison.Ordinal) &&
                !text.Contains("body_Fixed", StringComparison.Ordinal) && !text.Contains("body_Dismissed", StringComparison.Ordinal),
                "Copy Review must contain the comment and tofix findings only.");
            Check(text.Contains("Location: file.txt [New lines 2-2]", StringComparison.Ordinal) && text.Contains("```diff", StringComparison.Ordinal) &&
                text.Contains("@@ -1,2 +1,3 @@\n a\n+b\n c\n```", StringComparison.Ordinal),
                "Each finding must come with its location and code.");
            Check(text.Contains("Attachment: NeedsAttention", StringComparison.Ordinal), "A finding that is not attached must say so.");
            Check(text.IndexOf("body_Comment", StringComparison.Ordinal) < text.IndexOf("Location:", StringComparison.Ordinal),
                "A finding must come before its location.");

            var document = ReviewExport.CreateDocument(review);
            Check(document.Contains("body_Fixed", StringComparison.Ordinal) && document.Contains("body_Dismissed", StringComparison.Ordinal),
                "The saved document must contain every state.");
        }

        private static void SaveOnlyAfterReviewInteraction()
        {
            var session = CreateCommitSession(out var reviews, out var directory);
            var change = new Change { Path = "file.txt" };
            var diff = ShowFile(session, change, CreateDiff("@@ 1 1", " a", "+b", " c"));
            session.SetChanges([change]);
            reviews.Flush();
            Check(!Directory.Exists(Path.Combine(directory, "_reviews")), "Opening a review and a file must not write a review.");

            AddComment(session, diff, 2, 2, "finding");
            reviews.Flush();
            var saved = Path.Combine(directory, "_reviews", session.Review.Id);
            Check(File.Exists(Path.Combine(saved, "review.xml")) && File.Exists(Path.Combine(saved, "review.md")),
                "The first comment must write the review.");
            Check(new ReviewStore(directory).LoadReviews().Single().Files.Single().Comments.Single().Body == "finding",
                "The written review must contain the comment.");
        }

        private static void ArchiveDeletedReviews()
        {
            var session = CreateCommitSession(out var reviews, out var directory);
            var change = new Change { Path = "file.txt" };
            AddComment(session, ShowFile(session, change, CreateDiff("@@ 1 1", " a", "+b", " c")), 2, 2, "finding");
            reviews.Flush();
            var id = session.Review.Id;

            reviews.RefreshItems();
            reviews.SelectedItem = reviews.Items.Single();
            reviews.DeleteSelected();
            Check(reviews.Items.Count == 0 && !Directory.Exists(Path.Combine(directory, "_reviews", id)),
                "A deleted review must leave the active storage.");
            Check(File.Exists(Path.Combine(directory, "_toremove", "_reviews", id, "review.xml")),
                "A deleted review must move to the removal folder.");
            Check(new Reviews(directory).FindOrCreateReview(ReviewSourceKind.Commit, "base", "target", "Subject").Files.Count == 0,
                "A deleted review must not load again.");
        }

        private static void ListSavedReviews()
        {
            var session = CreateCommitSession(out var reviews, out _);
            var change = new Change { Path = "file.txt" };
            var diff = ShowFile(session, change, CreateDiff("@@ 1 1", " a", "+b", " c"));
            AddComment(session, diff, 2, 2, "finding");
            session.MarkFilesAsOk([new Change { Path = "other.txt" }]);

            reviews.RefreshItems();
            var item = reviews.Items.Single();
            Check(ReferenceEquals(reviews.SelectedItem, item) && reviews.HasSelection, "The list must select its first review.");
            Check(item.Title == "Subject" && item.Counts == App.Text("Reviews.Counts", 1, 1, 0, 0), "A list row must show the title and the counts per state.");
            Check(reviews.SelectedText.Contains("finding", StringComparison.Ordinal) && reviews.SelectedText.Contains("other.txt", StringComparison.Ordinal),
                "The selected review must show as text.");

            var page = new SourceGit.Views.Reviews { DataContext = reviews };
            var window = OpenWindow(page);
            try
            {
                Check(page.FindControl<ListBox>("ReviewList").ItemCount == 1 && ReferenceEquals(page.FindControl<ListBox>("ReviewList").SelectedItem, reviews.SelectedItem),
                    "The REVIEWS page must list the saved review and select it.");
                Check(page.FindControl<SourceGit.Views.AIResponseView>("ReviewText").Text.Contains("finding", StringComparison.Ordinal),
                    "The REVIEWS page must show the selected review as text.");
                var actions = page.FindControl<Button>("DeleteReviewButton");
                Check(actions.IsEffectivelyEnabled, "The actions must be available for the selected review.");

                var textTop = page.FindControl<Control>("ReviewText").TranslatePoint(default, page)?.Y ?? double.MaxValue;
                var actionsBottom = actions.TranslatePoint(new Point(0, actions.Bounds.Height), page)?.Y ?? 0;
                Check(textTop - actionsBottom < MaxReviewTextTopGap, "The review text must start at the top of its area.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void UseOneLocalReviewUntilANewOneStarts()
        {
            var directory = CreateDirectory();
            var reviews = new Reviews(directory);
            var session = new ReviewSession(reviews, ReviewTab.Commit);
            session.OpenLocal();
            var first = session.Review;
            Check(first != null && session.IsLocal, "Local changes must have a review.");
            session.OpenLocal();
            Check(ReferenceEquals(session.Review, first), "Opening the local review again must keep it.");

            var change = new Change { Path = "file.txt", WorkTree = ChangeState.Modified };
            session.SetChanges([change]);
            var diff = ShowLocalFile(session, change, CreateDiff("@@ 1 1", " a", "+b", " c"));
            AddComment(session, diff, 2, 2, "finding");
            reviews.Flush();
            Check(change.ReviewToFixCount == 1, "The local review must mark the commented file.");

            var reopened = new ReviewSession(new Reviews(directory), ReviewTab.Commit);
            reopened.OpenLocal();
            Check(reopened.Review.Id == first.Id && reopened.Review.Files.Single().Comments.Single().Body == "finding",
                "The saved local review must continue in the next session.");

            session.StartNewLocalReview();
            Check(!ReferenceEquals(session.Review, first) && session.Review.Files.Count == 0 && session.SelectedChange == change,
                "A new local review must start empty and keep the selected file.");
            Check(change.ReviewToFixCount == 0 && session.IsCurrentDiff(diff) && session.Comments.Count == 0,
                "A new local review must clear the marks and keep the diff.");
            session.OpenLocal();
            Check(!ReferenceEquals(session.Review, first), "The new local review must stay the active one.");
        }

        private static void FindCommitReviewsByRevision()
        {
            var session = CreateCommitSession(out var reviews, out var directory);
            var change = new Change { Path = "file.txt" };
            AddComment(session, ShowFile(session, change, CreateDiff("@@ 1 1", " a", "+b", " c")), 2, 2, "finding");
            var review = session.Review;

            session.Open(ReviewSourceKind.Commit, "base", "other", "Other");
            Check(!ReferenceEquals(session.Review, review) && session.Review.Files.Count == 0 && !session.HasFile,
                "Another commit must open its own review without a selected file.");
            session.Open(ReviewSourceKind.Commit, "any", "target", "Subject");
            Check(ReferenceEquals(session.Review, review), "The same commit must open its review again.");
            session.Open(ReviewSourceKind.Comparison, "target", "base", "compare");
            Check(!ReferenceEquals(session.Review, review), "A comparison must not open the review of a commit.");
            session.Close();
            Check(!session.HasReview, "A closed session must have no review.");

            reviews.Flush();
            var next = new ReviewSession(new Reviews(directory), ReviewTab.Comment);
            next.Open(ReviewSourceKind.Commit, "base", "target", "Subject");
            Check(next.Review.Id == review.Id, "A saved commit review must load for its commit.");
        }

        private static void CommentOnSelectedRangesWithToFixDefault()
        {
            var session = CreateCommitSession(out _, out _);
            var change = new Change { Path = "file.txt" };
            var diff = ShowFile(session, change, CreateDiff("@@ 1 1", " a", "+b", "+c", " d"));
            session.ActiveTab = ReviewTab.General;
            var focusRequests = 0;
            session.FocusEditorRequested += (_, _) => focusRequests++;

            session.OpenComment(diff, diff.CreateReviewSelection(ReviewPane.Combined, 2, 3), true);
            Check(session.ActiveTab == ReviewTab.Comment && focusRequests == 1 && session.LineDraft.HasTarget && session.Comments.Count == 0,
                "A comment target must open the Comment tab without creating a comment.");
            session.LineDraft.Body = "finding";
            var comment = session.Review.Files.Single().Comments.Single();
            Check(comment.State == ReviewCommentState.ToFix && comment.Anchor.NewStartLine == 2 && comment.Anchor.NewEndLine == 3 && comment.Anchor.Quote == "b\nc",
                "The first text must create a tofix comment on the selected range.");
            Check(session.Comments.Count == 1 && ReferenceEquals(session.SelectedComment.Data, comment), "The new comment must show selected in the list.");
            Check(session.FindComment(diff, diff.Data.Lines[2], ReviewPane.Combined) == comment && session.IsActiveLine(diff, diff.Data.Lines[3], ReviewPane.Combined),
                "The diff must find the new comment on its lines.");

            session.LineDraft.State = ReviewCommentState.Comment;
            Check(comment.State == ReviewCommentState.Comment, "The state choice must change the comment.");
        }

        private static void EditTheSameRangeWithoutDuplicates()
        {
            var session = CreateCommitSession(out _, out _);
            var change = new Change { Path = "file.txt" };
            var diff = ShowFile(session, change, CreateDiff("@@ 1 1", " a", "+b", "+c", " d"));
            AddComment(session, diff, 2, 2, "first");
            var first = session.LineDraft.Comment;

            session.TrackComment(diff, diff.CreateReviewSelection(ReviewPane.Combined, 4, 4), false);
            Check(session.LineDraft.Comment == null && session.LineDraft.HasTarget, "Moving the caret away must target a new comment.");
            session.TrackComment(diff, diff.CreateReviewSelection(ReviewPane.Combined, 2, 2), false);
            Check(ReferenceEquals(session.LineDraft.Comment, first) && session.LineDraft.Body == "first",
                "Moving the caret to a commented line must show its comment.");

            session.OpenComment(diff, diff.CreateReviewSelection(ReviewPane.Combined, 2, 2), true);
            session.LineDraft.Body = "second";
            Check(session.Comments.Count == 2, "An explicit selection must allow a second comment on the same line.");

            session.OpenComment(diff, diff.TrimChunkToChanges(new TextDiffSelectedChunk(0, 0, 0, 4, true, false)), false);
            Check(session.LineDraft.Comment == null && session.LineDraft.Anchor.NewStartLine == 2 && session.LineDraft.Anchor.NewEndLine == 3,
                "A comment on a hovered block must target its changed lines only.");
        }

        private static void RemoveEmptyCommentsWhenTheTargetChanges()
        {
            var session = CreateCommitSession(out _, out _);
            var change = new Change { Path = "file.txt" };
            var diff = ShowFile(session, change, CreateDiff("@@ 1 1", " a", "+b", "+c", " d"));
            session.SetChanges([change]);
            AddComment(session, diff, 2, 2, "finding");
            session.LineDraft.Body = "  ";
            Check(session.Review.Files.Single().Comments.Count == 1 && !change.HasReviewComments,
                "A comment without text must not count as a finding.");

            session.TrackComment(diff, diff.CreateReviewSelection(ReviewPane.Combined, 3, 3), false);
            Check(session.Review.Files.Single().Comments.Count == 0 && session.Comments.Count == 0,
                "A comment without text must go away when the target changes.");

            AddComment(session, diff, 2, 2, "again");
            session.LineDraft.Body = string.Empty;
            session.LineDraft.DropEmptyComment();
            Check(session.Comments.Count == 0 && session.LineDraft.HasTarget, "Dropping an empty comment must keep its target.");
            session.LineDraft.Body = "typed again";
            Check(session.Comments.Count == 1 && session.LineDraft.Comment.Anchor.NewStartLine == 2, "New text must create the comment on the same target again.");
        }

        private static void KeepFileCommentTargetWhenTheSelectedFileChanges()
        {
            var session = CreateCommitSession(out _, out _);
            var first = new Change { Path = "first.txt" };
            var second = new Change { Path = "second.txt" };
            session.SetChanges([first, second]);
            session.SelectFile(first);
            session.ActiveTab = ReviewTab.FileComment;

            session.SelectFile(second);
            Check(session.FileDraft.HasTarget, "The File comment tab must have a target right after a file selection.");
            session.FileDraft.Body = "file note";
            var file = session.Review.FindFile("second.txt");
            Check(file != null && file.Comments.Single().Body == "file note" && file.Comments[0].Anchor.Side == ReviewSide.File,
                "The File comment tab must edit the comment of the newly selected file.");
            Check(session.Review.FindFile("first.txt") == null && second.HasReviewComments && !first.HasReviewComments,
                "The comment must not go to the file that was selected before.");

            session.SelectFile(first);
            Check(session.FileDraft.Body.Length == 0, "Another file must show its own file comment.");
            session.SelectFile(second);
            Check(session.FileDraft.Body == "file note", "Coming back to a file must show its file comment again.");
            session.SelectFile(null);
            Check(!session.FileDraft.HasTarget, "No selected file means no file comment target.");
        }

        private static void ReuseTheSingleFileComment()
        {
            var session = CreateCommitSession(out _, out _);
            var change = new Change { Path = "file.txt" };
            session.SetChanges([change]);
            session.SelectFile(change);
            session.OpenFileComment();
            Check(session.ActiveTab == ReviewTab.FileComment && !session.FileDraft.ShowAnchorLabel && session.LineDraft.ShowAnchorLabel,
                "Edit file comment must open the File comment tab, which shows no range label.");
            session.FileDraft.Body = "first";
            session.FileDraft.Body = "second";
            var file = session.Review.Files.Single();
            Check(file.Comments.Count == 1 && file.Comments[0].Body == "second", "A file must keep exactly one file comment.");

            session.FileDraft.Body = string.Empty;
            session.FileDraft.DropEmptyComment();
            Check(!file.HasReviewWork && !change.HasReviewComments, "Clearing the file comment must return the file to unreviewed.");
        }

        private static void KeepEditorTargetsWhenTheHostSelectsTheSameFileAgain()
        {
            var session = CreateCommitSession(out _, out _);
            var change = new Change { Path = "file.txt" };
            var diff = ShowFile(session, change, CreateDiff("@@ 1 1", " a", "+b", " c"));
            AddComment(session, diff, 2, 2, "finding");

            var refreshed = new Change { Path = "file.txt" };
            session.SetChanges([refreshed]);
            session.SelectFile(refreshed);
            Check(session.IsCurrentDiff(diff) && session.LineDraft.Body == "finding" && refreshed.ReviewToFixCount == 1,
                "A list refresh must keep the diff, the editor content and the marks.");

            session.SetDiffSource(null, null);
            Check(!session.IsCurrentDiff(diff), "A new diff of the file must replace the content that is not carried over.");
        }

        private static void MarkFileAsOkWithTheCommentState()
        {
            var session = CreateCommitSession(out _, out _);
            var open = new Change { Path = "open.txt" };
            var noted = new Change { Path = "noted.txt" };
            var blocked = new Change { Path = "blocked.txt" };
            session.SetChanges([open, noted, blocked]);
            AddComment(session, ShowFile(session, blocked, CreateDiff("@@ 1 1", " a", "+b", " c")), 2, 2, "fix me");
            session.SelectFile(noted);
            session.FileDraft.Body = "a note";
            session.FileDraft.State = ReviewCommentState.Dismissed;

            session.SelectFile(open);
            session.Filter = ReviewFileFilter.Unreviewed;
            var filterChanges = 0;
            session.FilterChanged += (_, _) => filterChanges++;
            session.MarkFilesAsOk([open, noted]);

            var ok = session.Review.FindFile("open.txt").Comments.Single();
            Check(ok.Body == "OK" && ok.State == ReviewCommentState.Comment && ok.Anchor.Side == ReviewSide.File,
                "Mark as OK must store a file comment with the comment state.");
            var replaced = session.Review.FindFile("noted.txt").Comments.Single();
            Check(replaced.Body == "OK" && replaced.State == ReviewCommentState.Comment, "Mark as OK must replace an existing file comment.");
            Check(open.IsReviewedWithoutToFix && session.FileDraft.Body == "OK", "A file marked OK must show as reviewed, also in the editor.");
            Check(filterChanges == 1 && !session.MatchesFilter(open), "A file marked OK must leave the Unreviewed filter.");

            Check(session.CountToFix([open, blocked]) == 1, "The tofix count must cover every selected file.");
            session.MarkFilesAsOk([blocked]);
            Check(session.Review.FindFile("blocked.txt").Comments.Count == 1, "A file with tofix comments must not be marked OK.");
        }

        private static void SetCommentsStateForAllSelectedFiles()
        {
            var session = CreateCommitSession(out _, out _);
            var first = new Change { Path = "first.txt" };
            var second = new Change { Path = "second.txt" };
            var third = new Change { Path = "third.txt" };
            session.SetChanges([first, second, third]);
            foreach (var change in new[] { first, second, third })
            {
                var diff = ShowFile(session, change, CreateDiff("@@ 1 1", " a", "+b", "+c", " d"));
                AddComment(session, diff, 2, 2, "one");
                session.OpenComment(diff, diff.CreateReviewSelection(ReviewPane.Combined, 3, 3), true);
                session.LineDraft.Body = "two";
            }

            session.SetCommentsState([first, second], ReviewCommentState.Fixed);
            Check(session.Review.FindFile("first.txt").Comments.TrueForAll(x => x.State == ReviewCommentState.Fixed) &&
                session.Review.FindFile("second.txt").Comments.TrueForAll(x => x.State == ReviewCommentState.Fixed),
                "The state must apply to every comment of every selected file.");
            Check(session.Review.FindFile("third.txt").Comments.TrueForAll(x => x.State == ReviewCommentState.ToFix) &&
                first.ReviewToFixCount == 0 && third.ReviewToFixCount == 2 && session.LineDraft.State == ReviewCommentState.ToFix,
                "Other files must keep their states.");
        }

        private static void TrackReviewMarksOnChanges()
        {
            var session = CreateCommitSession(out _, out _);
            var change = new Change { Path = "file.txt" };
            var plain = new Change { Path = "plain.txt" };
            session.SetChanges([change, plain]);
            var diff = ShowFile(session, change, CreateDiff("@@ 1 1", " a", "+b", " c"));
            Check(!change.HasReviewComments && !change.HasReviewToFix, "A file without comments must have no mark.");

            AddComment(session, diff, 2, 2, "finding");
            Check(change.HasReviewToFix && change.ReviewToFixCount == 1 && !plain.HasReviewComments, "A tofix comment must show the count mark.");
            session.LineDraft.State = ReviewCommentState.Fixed;
            Check(change.IsReviewedWithoutToFix, "A file without tofix comments must show the reviewed mark.");

            var reloaded = new Change { Path = "file.txt" };
            session.SetChanges([reloaded]);
            Check(reloaded.IsReviewedWithoutToFix, "New change objects must get the marks of the review.");
            session.Close();
            Check(!reloaded.HasReviewComments, "Closing the review must clear the marks.");
        }

        private static void FilterFilesByCommentState()
        {
            var session = CreateCommitSession(out _, out _);
            var changes = new List<Change>();
            foreach (var state in Enum.GetValues<ReviewCommentState>())
            {
                var change = new Change { Path = state + ".txt" };
                changes.Add(change);
                AddComment(session, ShowFile(session, change, CreateDiff("@@ 1 1", " a", "+b", " c")), 2, 2, "finding");
                session.LineDraft.State = state;
            }
            var plain = new Change { Path = "plain.txt" };
            changes.Add(plain);

            Check(ReferenceEquals(session.FilterChanges(changes), changes), "No filter must return the same list.");
            var filterChanges = 0;
            session.FilterChanged += (_, _) => filterChanges++;
            session.Filter = ReviewFileFilter.Reviewed;
            Check(filterChanges == 1 && session.FilterChanges(changes).Count == 4 && !session.MatchesFilter(plain), "Reviewed means at least one comment with text.");
            session.Filter = ReviewFileFilter.Unreviewed;
            Check(session.FilterChanges(changes).Single() == plain, "Unreviewed means no comment with text.");
            foreach (var (filter, name) in new[] { (ReviewFileFilter.Comment, "Comment"), (ReviewFileFilter.ToFix, "ToFix"), (ReviewFileFilter.Fixed, "Fixed"), (ReviewFileFilter.Dismissed, "Dismissed") })
            {
                session.Filter = filter;
                Check(session.FilterChanges(changes).Single().Path == name + ".txt", $"The {name} filter must keep the files with that state.");
            }
        }

        private static void FollowRenamedFiles()
        {
            var session = CreateCommitSession(out _, out _);
            var change = new Change { Path = "old_name.txt" };
            AddComment(session, ShowFile(session, change, CreateDiff("@@ 1 1", " a", "+b", " c")), 2, 2, "finding");

            var renamed = new Change { Path = "new_name.txt", OriginalPath = "old_name.txt", Index = ChangeState.Renamed };
            session.SetChanges([renamed]);
            Check(renamed.ReviewToFixCount == 1, "A renamed file must show the marks of its comments.");
            session.SelectFile(renamed);
            var file = session.Review.Files.Single();
            Check(file.Path == "new_name.txt" && file.OldPath == "old_name.txt" && session.Comments.Count == 1,
                "The comments must follow a renamed file to its new path.");
        }

        private static void SortCommentsFileLevelThenByFirstLine()
        {
            var session = CreateCommitSession(out _, out _);
            var change = new Change { Path = "file.txt" };
            var diff = ShowFile(session, change, CreateAddedFileDiff(20));
            session.OpenComment(diff, diff.CreateReviewSelection(ReviewPane.Combined, 10, 12), true);
            session.LineDraft.Body = "range";
            session.OpenComment(diff, diff.CreateReviewSelection(ReviewPane.Combined, 10, 10), true);
            session.LineDraft.Body = "line";
            AddComment(session, diff, 3, 3, "early");
            session.FileDraft.Body = "whole file";
            Check(string.Join(",", session.Comments.Select(x => x.FirstLine)) == "whole file,early,line,range",
                "The list must show the file comment first, then the comments by line and by length.");
            Check(session.CommentsCount == "(4)" && session.Comments[1].Details == $"{App.Text("Reviews.State.ToFix")} - {App.Text("Reviews.Range.New", 3, 3)}",
                "A list row must show the state and the range.");
        }

        private static void MoveCommentToSelectedLines()
        {
            var session = CreateCommitSession(out _, out _);
            var change = new Change { Path = "file.txt" };
            var diff = ShowFile(session, change, CreateDiff("@@ 1 1", " a", "+b", "+c", " d"));
            AddComment(session, diff, 2, 2, "finding");
            var comment = session.LineDraft.Comment;
            comment.Anchor.Attachment = ReviewAttachment.NeedsAttention;
            session.LineDraft.Refresh();
            Check(session.LineDraft.CanReattach && session.LineDraft.AttachmentLabel == App.Text("Reviews.Attachment.NeedsAttention"),
                "A comment that needs attention must offer a move.");

            session.LineDraft.BeginReattach();
            Check(session.HasPendingReattach && session.CommentActionLabel == App.Text("Reviews.AttachComment"), "A pending move must change the comment action.");
            session.TrackComment(diff, diff.CreateReviewSelection(ReviewPane.Combined, 3, 3), false);
            Check(session.HasPendingReattach, "Moving the caret must not finish the move.");
            session.OpenComment(diff, diff.CreateReviewSelection(ReviewPane.Combined, 3, 3), false);
            Check(!session.HasPendingReattach && comment.Anchor.NewStartLine == 3 && comment.Anchor.Attachment == ReviewAttachment.Attached &&
                comment.Anchor.Quote == "c" && session.Review.Files.Single().Comments.Count == 1,
                "The comment action must attach the comment to the selected lines.");

            session.LineDraft.BeginReattach();
            session.FocusDiff();
            Check(!session.HasPendingReattach, "Escape must cancel a pending move.");
        }

        private static void SplitGeneralMessageIntoTitleAndNotes()
        {
            var session = CreateCommitSession(out var reviews, out var directory);
            Check(session.GeneralReviewMessage == "Subject\n\n", "The general message must start with the subject of the commit.");
            foreach (var text in new[] { "New title", "New title\n", "New title\n\n", "New title\n\nBody", "New title\n\nBody\n" })
            {
                session.GeneralReviewMessage = text;
                Check(session.GeneralReviewMessage == text, "The editor text must stay exactly as typed.");
            }
            Check(session.Review.Title == "New title" && session.Review.GeneralText == "Body\n", "The first line is the title, the rest are the notes.");
            reviews.Flush();
            Check(new ReviewStore(directory).LoadReviews().Single().GeneralText == "Body\n", "General notes alone must save the review.");
        }

        private static void ReplaceDeletedReviewInTheSession()
        {
            var session = CreateCommitSession(out var reviews, out _);
            var change = new Change { Path = "file.txt" };
            session.SetChanges([change]);
            var diff = ShowFile(session, change, CreateDiff("@@ 1 1", " a", "+b", " c"));
            AddComment(session, diff, 2, 2, "finding");
            reviews.Flush();
            var deleted = session.Review;

            reviews.RefreshItems();
            reviews.DeleteSelected();
            Check(!ReferenceEquals(session.Review, deleted) && session.Review.TargetRevision == "target" && session.Comments.Count == 0,
                "A session must continue with a new review when its review is deleted.");
            Check(!change.HasReviewComments && session.IsCurrentDiff(diff) && session.SelectedChange == change,
                "The session must keep the selected file and its diff.");
            AddComment(session, diff, 2, 2, "new finding");
            reviews.Flush();
            Check(session.Review.IsSaved && !deleted.IsSaved, "Comments after the deletion must go to the new review.");
        }

        private static void AllowStagingAndCommentsInTheSameDiff()
        {
            var directory = CreateDirectory();
            var session = new ReviewSession(new Reviews(directory), ReviewTab.Commit);
            session.OpenLocal();
            var unstaged = new Change { Path = "file.txt", WorkTree = ChangeState.Modified };
            var diff = ShowLocalFile(session, unstaged, CreateDiff("@@ 1 1", " a", "+b", " c"));
            Check(diff.IsReview && diff.CanStageChunks && !diff.CanUnstageChunks, "A diff of unstaged changes must offer Stage and Comment.");

            var staged = new CombinedTextDiff(new DiffOption(new Change { Path = "file.txt", Index = ChangeState.Modified }, false), diff.Data) { Review = session };
            Check(staged.IsReview && staged.CanUnstageChunks && !staged.CanStageChunks, "A diff of staged changes must offer Unstage and Comment.");
            var history = new CombinedTextDiff(new DiffOption("base", "target", unstaged), diff.Data) { Review = session };
            Check(!history.CanStageChunks && !history.CanUnstageChunks, "A diff of commits must not offer staging.");
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

        private static void MoveCaretToBoundariesWithoutAReview()
        {
            var data = CreateDiff("@@ 1 1", " a", "+b", "+c", " d", " e", "-f", " g");
            var diff = new CombinedTextDiff(new DiffOption("base", "target", new Change { Path = "file.txt" }), data);
            var window = OpenWindow(new SourceGit.Views.TextDiffView { DataContext = diff });
            try
            {
                var presenter = window.GetVisualDescendants().OfType<SourceGit.Views.ThemedTextDiffPresenter>().First();
                presenter.MoveCaretToBoundary(BlockNavigationDirection.Next, Avalonia.Input.KeyModifiers.None);
                Check(presenter.TextArea.Caret.Line == 3, "The first move must go to the start of the first block of changes.");
                presenter.MoveCaretToBoundary(BlockNavigationDirection.Next, Avalonia.Input.KeyModifiers.Shift);
                Check(presenter.TextArea.Caret.Line == 5 && presenter.SelectedText == "b\nc\n", "Shift must select the block up to its end.");
                presenter.MoveCaretToBoundary(BlockNavigationDirection.Next, Avalonia.Input.KeyModifiers.None);
                Check(presenter.TextArea.Caret.Line == 7 && presenter.TextArea.Selection.IsEmpty, "The next move must go to the start of the next block.");
                presenter.MoveCaretToBoundary(BlockNavigationDirection.Prev, Avalonia.Input.KeyModifiers.None);
                Check(presenter.TextArea.Caret.Line == 5, "A move back must go to the end of the previous block.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void RevealSelectedCommentInTheDiff()
        {
            var session = CreateCommitSession(out _, out _);
            var change = new Change { Path = "file.txt" };
            var diff = ShowFile(session, change, CreateAddedFileDiff(400));
            AddComment(session, diff, 20, 20, "near the top");
            AddComment(session, diff, 300, 300, "far below");
            session.FileDraft.Body = "whole file";
            var window = OpenWindow(new SourceGit.Views.TextDiffView { DataContext = diff });
            try
            {
                var presenter = window.GetVisualDescendants().OfType<SourceGit.Views.ThemedTextDiffPresenter>().First();
                var scroller = presenter.FindDescendantOfType<ScrollViewer>();
                var lineHeight = presenter.TextArea.TextView.DefaultLineHeight;
                session.ActiveTab = ReviewTab.General;

                session.SelectedComment = session.Comments.Single(x => x.FirstLine == "near the top");
                PumpLayout(window);
                Check(presenter.TextArea.Caret.Line == 21, "Selecting a comment must move the diff caret to its first line.");
                Check(session.ActiveTab == ReviewTab.Comment && session.LineDraft.Body == "near the top", "Selecting a comment must show it in the Comment tab.");

                session.SelectedComment = session.Comments.Single(x => x.FirstLine == "far below");
                PumpLayout(window);
                Check(presenter.TextArea.Caret.Line == 301 && session.LineDraft.Body == "far below", "Selecting another comment must move the diff caret again.");
                Check(scroller.Offset.Y > lineHeight * 200, "Selecting a comment must scroll the diff to it.");

                scroller.Offset = new Vector(0, 0);
                PumpLayout(window);
                Check(scroller.Offset.Y < lineHeight, "The test must scroll away from the comment first.");
                session.ShowComment(session.SelectedComment.Data);
                PumpLayout(window);
                Check(presenter.TextArea.Caret.Line == 301 && scroller.Offset.Y > lineHeight * 200 && session.LineDraft.Body == "far below",
                    "A click on the selected comment must go to it again.");

                session.SelectedComment = session.Comments.Single(x => x.FirstLine == "whole file");
                Check(session.ActiveTab == ReviewTab.FileComment, "Selecting the file comment must open the File comment tab.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void GotoNextAndPreviousComment()
        {
            var session = CreateCommitSession(out _, out _);
            var change = new Change { Path = "file.txt" };
            var diff = ShowFile(session, change, CreateAddedFileDiff(120));
            AddComment(session, diff, 10, 10, "first");
            AddComment(session, diff, 60, 62, "second");
            AddComment(session, diff, 110, 110, "third");
            var window = OpenWindow(new SourceGit.Views.TextDiffView { DataContext = diff });
            try
            {
                var presenter = window.GetVisualDescendants().OfType<SourceGit.Views.ThemedTextDiffPresenter>().First();
                presenter.TextArea.Caret.Line = 1;
                var visited = new List<string>();
                for (var i = 0; i < 4; i++)
                {
                    presenter.GotoComment(BlockNavigationDirection.Next);
                    PumpLayout(window);
                    visited.Add($"{presenter.TextArea.Caret.Line}:{session.LineDraft.Body}");
                }
                Check(string.Join(",", visited) == "11:first,61:second,111:third,111:third", "Next comment must visit each comment once in line order.");

                presenter.GotoComment(BlockNavigationDirection.Prev);
                PumpLayout(window);
                Check(presenter.TextArea.Caret.Line == 61 && session.SelectedComment.FirstLine == "second", "Previous comment must go back one comment.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void TypeCommentsInTheReviewPanel()
        {
            var session = CreateCommitSession(out _, out _);
            var change = new Change { Path = "file.txt" };
            var diff = ShowFile(session, change, CreateAddedFileDiff(60));
            var panel = new SourceGit.Views.ReviewPanel { DataContext = session };
            var view = new SourceGit.Views.TextDiffView { DataContext = diff };
            var grid = new Grid { RowDefinitions = new RowDefinitions("*,200") };
            Grid.SetRow(panel, 1);
            grid.Children.Add(view);
            grid.Children.Add(panel);
            var window = OpenWindow(grid);
            try
            {
                var presenter = view.GetVisualDescendants().OfType<SourceGit.Views.ThemedTextDiffPresenter>().First();
                var editor = panel.FindControl<SourceGit.Views.ReviewCommentEditor>("CommentEditor");
                var body = editor.FindControl<SourceGit.Views.CommitMessageTextEditor>("BodyEditor");

                presenter.CaretOffset = presenter.Document.GetLineByNumber(11).Offset;
                PumpLayout(window);
                Check(session.LineDraft.HasTarget && session.LineDraft.Anchor.NewStartLine == 10, "The Comment tab must target the line of the diff caret.");

                presenter.AddReviewComment();
                PumpLayout(window);
                Check(body.TextArea.IsFocused, "The comment action must focus the comment editor.");
                body.TextArea.PerformTextInput("docked finding");
                foreach (var text in new[] { "\n", "\n", "continued" })
                {
                    var inserted = text == "\n" ? Environment.NewLine : text;
                    var expected = body.Text + inserted;
                    body.TextArea.PerformTextInput(text);
                    PumpLayout(window);
                    Check(body.Text == expected && session.LineDraft.Body == expected && body.CaretOffset == expected.Length,
                        "Typing must keep the text and the caret position.");
                }
                var finding = session.LineDraft.Body;
                Check(session.Comments.Count == 1 && session.Review.Files.Single().Comments.Single().Body == finding, "Typed text must create the comment.");

                presenter.TextArea.Selection = AvaloniaEdit.Editing.Selection.Create(presenter.TextArea,
                    presenter.Document.GetLineByNumber(13).Offset, presenter.Document.GetLineByNumber(15).Offset);
                PumpLayout(window);
                Check(session.LineDraft.Body.Length == 0 && session.LineDraft.Anchor.NewStartLine == 12 && session.LineDraft.Anchor.NewEndLine == 13 &&
                    session.Comments.Single().FirstLine == "docked finding",
                    "A selection must retarget the Comment tab to the selected lines and keep the saved comment.");

                presenter.TextArea.ClearSelection();
                presenter.CaretOffset = presenter.Document.GetLineByNumber(11).Offset;
                PumpLayout(window);
                Check(session.LineDraft.Body == finding && body.Text == finding, "Returning the caret to a commented line must show its comment.");

                session.OpenFileComment();
                PumpLayout(window);
                var fileBody = panel.FindControl<SourceGit.Views.ReviewCommentEditor>("FileCommentEditor").FindControl<SourceGit.Views.CommitMessageTextEditor>("BodyEditor");
                Check(fileBody.TextArea.IsFocused, "Edit file comment must focus the file comment editor.");
                fileBody.TextArea.PerformTextInput("file note");
                PumpLayout(window);
                Check(session.Review.Files.Single().FileComment?.Body == "file note", "Text in the File comment tab must create the file comment.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void ShowCommitTabOnlyWithCommitContent()
        {
            var history = new SourceGit.Views.ReviewPanel { DataContext = CreateCommitSession(out _, out _) };
            var local = new SourceGit.Views.ReviewPanel
            {
                DataContext = new ReviewSession(new Reviews(CreateDirectory()), ReviewTab.Commit),
                CommitContent = new TextBlock { Text = "commit" },
            };
            var grid = new Grid { RowDefinitions = new RowDefinitions("*,*") };
            Grid.SetRow(local, 1);
            grid.Children.Add(history);
            grid.Children.Add(local);
            var window = OpenWindow(grid);
            try
            {
                var historyTabs = history.FindControl<TabControl>("EditorTabs");
                var localTabs = local.FindControl<TabControl>("EditorTabs");
                Check(!history.FindControl<TabItem>("CommitTab").IsVisible && historyTabs.SelectedIndex == (int)ReviewTab.Comment,
                    "History must show the three review tabs and start on Comment.");
                Check(local.FindControl<TabItem>("CommitTab").IsVisible && localTabs.SelectedIndex == (int)ReviewTab.Commit && localTabs.ItemCount == 4,
                    "Local changes must show Commit as the first of four tabs.");
                Check(!local.FindControl<Border>("CommentListBorder").IsVisible && history.FindControl<Border>("CommentListBorder").IsVisible,
                    "The comment list must show on the comment tabs only.");
                history.ShowComments = false;
                PumpLayout(window);
                Check(!history.FindControl<Border>("CommentListBorder").IsVisible, "The comment list must fold away.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void LoadReviewControls()
        {
            var editor = new SourceGit.Views.ReviewCommentEditor();
            Check(editor.FindControl<SourceGit.Views.CommitMessageTextEditor>("BodyEditor").ShowSubjectGuide == false,
                "Comments must use the shared editor without commit-subject decorations.");
            _ = new SourceGit.Views.Reviews { DataContext = new Reviews(CreateDirectory()) };
            _ = new SourceGit.Views.ReviewFilterBox();
            _ = new SourceGit.Views.DiffView();
            _ = new SourceGit.Views.WorkingCopy();
            _ = new SourceGit.Views.CommitChanges();
            _ = new SourceGit.Views.RevisionCompare();
        }

        private static void KeepCommentsWhenAFileIsStaged()
        {
            var fixture = CreateRepository();
            var path = Path.Combine(fixture, "file.txt");
            File.WriteAllText(path, string.Join('\n', Enumerable.Range(1, 30).Select(x => $"line_{x}")) + "\n");
            RunGit(fixture, "add", "file.txt");
            RunGit(fixture, "commit", "-q", "-m", "first");
            var lines = Enumerable.Range(1, 30).Select(x => $"line_{x}").ToList();
            lines[14] = "changed line";
            File.WriteAllText(path, string.Join('\n', lines) + "\n");

            var repository = OpenRepository(fixture);
            try
            {
                repository.SelectedViewIndex = 1;
                var workingCopy = (WorkingCopy)repository.SelectedView;
                WaitUntil(() => workingCopy.Unstaged.Exists(x => x.Path == "file.txt"), "Local changes must list the modified file.");
                var session = workingCopy.Review;
                Check(session.HasReview && session.IsLocal && session.ActiveTab == ReviewTab.Commit, "Local changes must open a local review on the Commit tab.");

                var unstaged = workingCopy.Unstaged.Find(x => x.Path == "file.txt");
                workingCopy.SelectedUnstaged = [unstaged];
                var diff = WaitForDiff(() => workingCopy.DetailContext, session);
                Check(diff.CanStageChunks && diff.IsReview, "The standard diff of an unstaged file must allow staging and comments.");
                var index = diff.Data.Lines.FindIndex(x => x.Type == TextDiffLineType.Added);
                AddComment(session, diff, index, index, "check this change");
                Check(unstaged.ReviewToFixCount == 1, "The Unstaged list must show the mark of the comment.");
                CheckLocalChangesView(workingCopy);

                var staging = workingCopy.StageChangesAsync([unstaged], null);
                WaitUntil(() => staging.IsCompleted && workingCopy.Staged.Exists(x => x.Path == "file.txt") && !workingCopy.Unstaged.Exists(x => x.Path == "file.txt"),
                    "The file must move to the Staged list.");
                var staged = workingCopy.Staged.Find(x => x.Path == "file.txt");
                Check(staged.ReviewToFixCount == 1, "The Staged list must show the mark of the comment.");

                workingCopy.SelectedStaged = [staged];
                var stagedDiff = WaitForDiff(() => workingCopy.DetailContext, session);
                Check(stagedDiff.CanUnstageChunks, "The diff of a staged file must allow unstaging.");
                var comment = session.Comments.Single().Data;
                Check(comment.Body == "check this change" && comment.Anchor.Attachment == ReviewAttachment.Attached && comment.Anchor.NewStartLine == 15,
                    "The comment must stay attached to its line after staging.");
                Check(session.FindComment(stagedDiff, stagedDiff.Data.Lines.Find(x => x.Type == TextDiffLineType.Added), ReviewPane.Combined) == comment,
                    "The staged diff must show the comment on the changed line.");

                lines.Insert(0, "inserted at the top");
                File.WriteAllText(path, string.Join('\n', lines) + "\n");
                RunGit(fixture, "add", "file.txt");
                repository.MarkWorkingCopyDirtyManually();
                WaitUntil(() => workingCopy.DetailContext is DiffContext { Content: TextDiffContext content } && session.IsCurrentDiff(content) &&
                    !ReferenceEquals(content, stagedDiff) && content.Data.AddedLines == 2, "The diff must reload after the file changed.");
                Check(comment.Anchor.NewStartLine == 16 && comment.Anchor.Attachment == ReviewAttachment.Attached,
                    "The comment must follow its code when the file changes above it.");

                session.Filter = ReviewFileFilter.Unreviewed;
                Check(!workingCopy.VisibleStaged.Exists(x => x.Path == "file.txt"), "The Unreviewed filter must hide the commented file.");
                session.Filter = ReviewFileFilter.All;
                Check(workingCopy.VisibleStaged.Exists(x => x.Path == "file.txt"), "The All files filter must show the file again.");
            }
            finally
            {
                repository.Close();
            }

            var saved = new ReviewStore(fixture).LoadReviews().Single();
            Check(saved.Source == ReviewSourceKind.Local && saved.Files.Single().Comments.Single().Anchor.NewStartLine == 16,
                "Closing the repository must write the review.");
        }

        private static void CheckLocalChangesView(WorkingCopy workingCopy)
        {
            var session = workingCopy.Review;
            session.ActiveTab = ReviewTab.Commit;
            var window = OpenWindow(new SourceGit.Views.WorkingCopy { DataContext = workingCopy });
            try
            {
                var panel = window.GetVisualDescendants().OfType<SourceGit.Views.ReviewPanel>().Single();
                var tabs = panel.FindControl<TabControl>("EditorTabs");
                Check(ReferenceEquals(panel.DataContext, session) && tabs.SelectedIndex == (int)ReviewTab.Commit,
                    "Local changes must show the review panel on its Commit tab.");

                var toolbox = window.GetVisualDescendants().OfType<SourceGit.Views.CommitMessageToolBox>().Single();
                workingCopy.CommitMessage = "message from the test";
                PumpLayout(window);
                Check(toolbox.CommitMessage == "message from the test", "The Commit tab must edit the commit message of Local changes.");
                workingCopy.CommitMessage = string.Empty;

                var diffView = window.GetVisualDescendants().OfType<SourceGit.Views.DiffView>().Single();
                Check(diffView.FindControl<Button>("ReviewMenuButton").IsVisible, "The standard diff view must offer the review actions.");
                Check(window.GetVisualDescendants().OfType<SourceGit.Views.ReviewFilterBox>().Single().IsVisible, "Local changes must offer the review filter.");
                Check(diffView.GetVisualDescendants().OfType<ToggleButton>().Any(x => x.IsVisible), "The standard diff toolbar must stay available.");

                session.OpenFileComment();
                PumpLayout(window);
                Check(tabs.SelectedIndex == (int)ReviewTab.FileComment && panel.FindControl<Border>("CommentListBorder").IsVisible,
                    "Edit file comment must open its tab with the comment list.");
                Check(panel.FindControl<ListBox>("CommentList").ItemCount == 1, "The comment list must show the comment of the selected file.");

                var presenter = diffView.FindActivePresenter();
                var diff = (TextDiffContext)presenter.DataContext;
                var firstChange = diff.Data.Lines.FindIndex(x => x.Type is TextDiffLineType.Added or TextDiffLineType.Deleted);
                presenter.TextArea.Caret.Line = 1;
                ClickHotKeyButton(diffView, "Ctrl+Down");
                Check(presenter.TextArea.Caret.Line == firstChange + 1, "Ctrl+Down must move the caret to the start of the first block of changes.");
                ClickHotKeyButton(diffView, "Ctrl+Alt+Down");
                PumpLayout(window);
                Check(session.ActiveTab == ReviewTab.Comment && session.LineDraft.Body == "check this change",
                    "Ctrl+Alt+Down must go to the next comment and show it in the Comment tab.");
                presenter.TextArea.Caret.Line = firstChange + 1;
                ClickHotKeyButton(diffView, "Ctrl+M");
                PumpLayout(window);
                Check(session.LineDraft.HasTarget && session.LineDraft.Comment == null && session.LineDraft.Anchor.Side == ReviewSide.Old,
                    "Ctrl+M must target a new comment on the caret line.");
                session.ActiveTab = ReviewTab.Commit;
            }
            finally
            {
                window.Close();
            }
        }

        private static void ClickHotKeyButton(Control view, string gesture)
        {
            var hotKey = Avalonia.Input.KeyGesture.Parse(gesture);
            var button = view.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.HotKey, hotKey));
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        }

        private static void CheckHistoryView(Histories histories)
        {
            var window = OpenWindow(new SourceGit.Views.Histories { DataContext = histories });
            try
            {
                var panel = window.GetVisualDescendants().OfType<SourceGit.Views.ReviewPanel>().Single();
                Check(panel.IsVisible && ReferenceEquals(panel.DataContext, histories.Review) && panel.Bounds.Height > 100,
                    "History must show the review panel below the detail of the selection.");
                Check(!panel.FindControl<TabItem>("CommitTab").IsVisible, "History must not show a Commit tab.");
                Check(window.GetVisualDescendants().OfType<SourceGit.Views.ReviewFilterBox>().Any(x => x.IsVisible), "History must offer the review filter.");
                var diffView = window.GetVisualDescendants().OfType<SourceGit.Views.DiffView>().Single();
                Check(diffView.FindControl<Button>("ReviewMenuButton").IsVisible, "The diff view of History must offer the review actions.");
            }
            finally
            {
                window.Close();
            }
        }

        private static void ReviewAndSaveARangeOfCommits()
        {
            var fixture = CreateRepository();
            var path = Path.Combine(fixture, "file.txt");
            foreach (var name in new[] { "first", "second", "third", "fourth" })
            {
                File.AppendAllText(path, $"line of {name}\n");
                RunGit(fixture, "add", "file.txt");
                RunGit(fixture, "commit", "-q", "-m", name);
            }

            var repository = OpenRepository(fixture);
            try
            {
                repository.SelectedViewIndex = 0;
                var histories = (Histories)repository.SelectedView;
                WaitUntil(() => histories.Commits.Count == 4, "History must list the four commits.");
                var commits = histories.Commits;
                Check(commits[0].Subject == "fourth" && commits[3].Subject == "first", "History must list the newest commit first.");

                histories.Select(new List<Commit> { commits[1], commits[0], commits[2] });
                var compare = histories.DetailContext as RevisionCompare;
                Check(compare != null && ReferenceEquals(compare.StartPoint, commits[2]) && ReferenceEquals(compare.EndPoint, commits[0]),
                    "A selection of more than two commits must compare its oldest commit with its newest commit.");
                var session = histories.Review;
                Check(session.HasReview && session.Review.Source == ReviewSourceKind.Comparison &&
                    session.Review.BaseRevision == commits[2].SHA && session.Review.TargetRevision == commits[0].SHA && histories.ReviewArea.Value > 0,
                    "The compare must open the review of its two commits.");

                WaitUntil(() => !compare.IsLoading && compare.SelectedChanges is { Count: 1 }, "The compare must list the changed file.");
                var diff = WaitForDiff(() => compare.DiffContext, session);
                var index = diff.Data.Lines.FindIndex(x => x.Type == TextDiffLineType.Added);
                AddComment(session, diff, index, index, "range finding");
                Check(compare.SelectedChanges[0].ReviewToFixCount == 1, "The file list of the compare must show the mark.");
                CheckHistoryView(histories);

                var patchPath = Path.Combine(s_root, Guid.NewGuid().ToString("N") + ".patch");
                var saving = compare.SaveChangesAsPatchAsync(null, patchPath);
                WaitUntil(() => saving.IsCompleted, "The patch must be written.");
                var patch = File.ReadAllText(patchPath);
                Check(patch.Contains("+line of third", StringComparison.Ordinal) && patch.Contains("+line of fourth", StringComparison.Ordinal) &&
                    !patch.Contains("+line of second", StringComparison.Ordinal) && patch.Split("diff --git").Length == 2,
                    "Save as Patch must write one patch that equals the shown compare.");

                histories.Select(new List<Commit> { commits[1] });
                var detail = histories.DetailContext as CommitDetail;
                Check(detail != null && session.Review.Source == ReviewSourceKind.Commit && session.Review.TargetRevision == commits[1].SHA && session.Comments.Count == 0,
                    "A single commit must open its own review.");
                histories.Select(new List<Commit> { commits[0], commits[2] });
                Check(histories.DetailContext is RevisionCompare && session.Review.Files.Single().Comments.Single().Body == "range finding",
                    "Selecting the same two commits again must open the saved review.");

                histories.Select(new List<Commit>());
                Check(!session.HasReview && histories.ReviewArea.Value == 0, "No selection must close the review panel.");
            }
            finally
            {
                repository.Close();
            }
        }

        private static void LoadApplicationResources()
        {
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

        private static Window OpenWindow(Control content)
        {
            var window = new Window { Width = 1400, Height = 900, Content = content };
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

        private static void WaitUntil(Func<bool> condition, string message)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                if (condition())
                    return;
                if (watch.Elapsed > s_waitTimeout)
                    throw new InvalidOperationException("Timeout: " + message);
                Thread.Sleep(10);
            }
        }

        private static TextDiffContext WaitForDiff(Func<object> getDiffContext, ReviewSession session)
        {
            TextDiffContext result = null;
            WaitUntil(() =>
            {
                result = (getDiffContext() as DiffContext)?.Content as TextDiffContext;
                return result != null && session.IsCurrentDiff(result);
            }, "The diff of the selected file must load and attach to the review.");
            return result;
        }

        private static Repository OpenRepository(string path)
        {
            var normalized = path.Replace('\\', '/');
            var repository = new Repository(false, normalized, normalized + "/.git");
            repository.Open();
            return repository;
        }

        // Each row starts with ' ', '+' or '-'. A row "@@ <old> <new>" starts a hunk at these line numbers.
        private static TextDiff CreateDiff(params string[] rows)
        {
            var diff = new TextDiff();
            var oldLine = 1;
            var newLine = 1;
            foreach (var row in rows)
            {
                if (row.StartsWith("@@", StringComparison.Ordinal))
                {
                    var parts = row.Split(' ');
                    oldLine = int.Parse(parts[1]);
                    newLine = int.Parse(parts[2]);
                    diff.Lines.Add(new TextDiffLine(TextDiffLineType.Indicator, row, 0, 0));
                }
                else if (row[0] == '+')
                {
                    diff.Lines.Add(new TextDiffLine(TextDiffLineType.Added, row[1..], 0, newLine++));
                    diff.AddedLines++;
                }
                else if (row[0] == '-')
                {
                    diff.Lines.Add(new TextDiffLine(TextDiffLineType.Deleted, row[1..], oldLine++, 0));
                    diff.DeletedLines++;
                }
                else
                {
                    diff.Lines.Add(new TextDiffLine(TextDiffLineType.Normal, row[1..], oldLine++, newLine++));
                }
            }
            diff.MaxLineNumber = Math.Max(oldLine, newLine);
            return diff;
        }

        private static TextDiff CreateAddedFileDiff(int lineCount)
        {
            var rows = new List<string> { "@@ 1 1" };
            rows.AddRange(Enumerable.Range(1, lineCount).Select(x => $"+line_{x}"));
            return CreateDiff(rows.ToArray());
        }

        private static ReviewAnchor CreateCapturedAnchor(TextDiff diff, int first, int last, ReviewPane pane)
        {
            var anchor = ReviewAnchors.CreateAnchor(diff, first, last, pane);
            if (anchor != null)
                ReviewAnchors.CaptureCode(anchor, diff);
            return anchor;
        }

        private static ReviewFile CreateFileWithComment(TextDiff diff, int index)
        {
            var file = new ReviewFile { Path = "file.txt" };
            file.Comments.Add(new ReviewComment { Body = "finding", Anchor = CreateCapturedAnchor(diff, index, index, ReviewPane.Combined) });
            return file;
        }

        private static ReviewSession CreateCommitSession(out Reviews reviews, out string directory)
        {
            directory = CreateDirectory();
            reviews = new Reviews(directory);
            var session = new ReviewSession(reviews, ReviewTab.Comment);
            session.Open(ReviewSourceKind.Commit, "base", "target", "Subject");
            return session;
        }

        private static CombinedTextDiff ShowFile(ReviewSession session, Change change, TextDiff data)
        {
            session.SelectFile(change);
            var diff = new CombinedTextDiff(new DiffOption("base", "target", change), data) { Review = session };
            session.AttachDiff(null, diff);
            return diff;
        }

        private static CombinedTextDiff ShowLocalFile(ReviewSession session, Change change, TextDiff data)
        {
            session.SelectFile(change);
            var diff = new CombinedTextDiff(new DiffOption(change, true), data) { Review = session };
            session.AttachDiff(null, diff);
            return diff;
        }

        // Comments on the diff rows from `first` to `last`. More than one row counts as a selection.
        private static void AddComment(ReviewSession session, TextDiffContext diff, int first, int last, string body)
        {
            session.OpenComment(diff, diff.CreateReviewSelection(ReviewPane.Combined, first, last), first != last);
            session.LineDraft.Body = body;
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
            RunGit(path, "config", "core.autocrlf", "false");
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

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
