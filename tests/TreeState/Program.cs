using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;

using SourceGit.Models;
using SourceGit.ViewModels;
using SourceGit.Views;

namespace SourceGit.Tests
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            AppBuilder.Configure<Application>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
            Application.Current.Resources["Text.Tree.ExpandAllChildren"] = "Expand all children";

            var tests = new (string Name, Action Run)[]
            {
                (nameof(KeepExistingChangeDefaults), KeepExistingChangeDefaults),
                (nameof(KeepCollapseAcrossEmptyChanges), KeepCollapseAcrossEmptyChanges),
                (nameof(KeepHiddenChildCollapsedAfterRefresh), KeepHiddenChildCollapsedAfterRefresh),
                (nameof(KeepCollapseAcrossViewModes), KeepCollapseAcrossViewModes),
                (nameof(RestoreChoicesAcrossViewsWithoutSharingTrees), RestoreChoicesAcrossViewsWithoutSharingTrees),
                (nameof(KeepCompactCollapseAcrossShapeChanges), KeepCompactCollapseAcrossShapeChanges),
                (nameof(ExpandCompactedFoldersExplicitly), ExpandCompactedFoldersExplicitly),
                (nameof(KeepExistingBranchDefaults), KeepExistingBranchDefaults),
                (nameof(KeepManualBranchCollapse), KeepManualBranchCollapse),
                (nameof(KeepTagChoicesAcrossEmptyAndHiddenNodes), KeepTagChoicesAcrossEmptyAndHiddenNodes),
                (nameof(KeepSubmoduleChoicesAcrossEmptyAndHiddenNodes), KeepSubmoduleChoicesAcrossEmptyAndHiddenNodes),
                (nameof(ExpandOnlyRequestedChangeSubtree), ExpandOnlyRequestedChangeSubtree),
                (nameof(ExposeExplicitExpandMenuAction), ExposeExplicitExpandMenuAction),
                (nameof(KeepRevisionFileChoicesWithCachedChildren), KeepRevisionFileChoicesWithCachedChildren),
                (nameof(PersistManualStates), PersistManualStates),
                (nameof(KeepLargeTreeStateSparse), KeepLargeTreeStateSparse),
                (nameof(KeepStateSafeDuringBackgroundReads), KeepStateSafeDuringBackgroundReads),
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

            Console.WriteLine($"{tests.Length - failed}/{tests.Length} tree state tests passed.");
            return failed == 0 ? 0 : 1;
        }

        private static void KeepExistingChangeDefaults()
        {
            var state = new TreeExpansionState();
            var tree = ChangeTreeNode.Build([new Change { Path = "src/nested/file.cs" }], state, false);
            Check(tree[0].IsExpanded, "The existing expanded root default must not change.");
            Check(tree[0].Children[0].IsExpanded, "The existing expanded child default must not change.");
            Check(state.ManualStates.Count == 0, "Building a tree must not record automatic defaults as manual choices.");
        }

        private static void KeepCollapseAcrossEmptyChanges()
        {
            var view = CreateChangeView(new TreeExpansionState());
            view.ToggleNodeIsExpanded(GetChangeTree(view).Tree[0]);
            view.Changes = [];
            Check(view.Content == null, "An empty change list must still clear its presentation.");

            view.Changes = [new Change { Path = "src/nested/file.cs" }];
            Check(!GetChangeTree(view).Tree[0].IsExpanded, "An empty list after staging or committing must not forget a manual collapse.");
        }

        private static void KeepHiddenChildCollapsedAfterRefresh()
        {
            var view = CreateChangeView(new TreeExpansionState());
            var root = GetChangeTree(view).Tree[0];
            view.ToggleNodeIsExpanded(root.Children[0]);
            view.ToggleNodeIsExpanded(root);
            view.Changes = [new Change { Path = "src/nested/other.cs" }];

            root = GetChangeTree(view).Tree[0];
            Check(!root.IsExpanded, "A manually collapsed parent must remain collapsed.");
            Check(!root.Children[0].IsExpanded, "A hidden child must retain its manual collapse.");
            view.ToggleNodeIsExpanded(root);
            Check(GetChangeTree(view).Rows.Count == 2, "Expanding the parent must not expand the manually collapsed child.");
        }

        private static void KeepCollapseAcrossViewModes()
        {
            var view = CreateChangeView(new TreeExpansionState());
            view.ToggleNodeIsExpanded(GetChangeTree(view).Tree[0]);
            view.ViewMode = ChangeViewMode.List;
            view.ViewMode = ChangeViewMode.Grid;
            view.ViewMode = ChangeViewMode.Tree;
            Check(!GetChangeTree(view).Tree[0].IsExpanded, "Switching presentation modes must not forget a manual collapse.");
        }

        private static void RestoreChoicesAcrossViewsWithoutSharingTrees()
        {
            var repository = new RepositoryUIStates();
            var view = CreateChangeView(repository.UnstagedTreeExpansion);
            view.ToggleNodeIsExpanded(GetChangeTree(view).Tree[0]);

            var recreated = CreateChangeView(repository.UnstagedTreeExpansion);
            Check(!GetChangeTree(recreated).Tree[0].IsExpanded, "Recreating the view must restore manual choices.");
            Check(GetChangeTree(CreateChangeView(repository.StagedTreeExpansion)).Tree[0].IsExpanded, "Staged and unstaged trees must not share manual choices.");
            Check(GetChangeTree(CreateChangeView(new RepositoryUIStates().UnstagedTreeExpansion)).Tree[0].IsExpanded, "Different repositories must not share manual choices.");
        }

        private static void KeepCompactCollapseAcrossShapeChanges()
        {
            var state = new TreeExpansionState();
            var view = CreateChangeView(state);
            view.EnableCompactFolders = true;
            view.ToggleNodeIsExpanded(GetChangeTree(view).Tree[0]);
            view.Changes = [new Change { Path = "src/nested/file.cs" }, new Change { Path = "src/other/file.cs" }];
            Check(!GetChangeTree(view).Tree[0].IsExpanded, "A compact folder must remain collapsed when another sibling appears.");

            view.EnableCompactFolders = false;
            var root = GetChangeTree(view).Tree[0];
            Check(!root.IsExpanded && !root.Children[0].IsExpanded, "Uncompacting must preserve the collapsed represented paths.");
            view.ToggleNodeIsExpanded(root);
            Check(!root.Children[0].IsExpanded, "Opening the parent must not erase the child's manual collapse.");
        }

        private static void ExpandCompactedFoldersExplicitly()
        {
            var state = new TreeExpansionState();
            state.SetExpanded("src", false);
            state.SetExpanded("src/nested", false);
            var tree = ChangeTreeNode.Build([new Change { Path = "src/nested/file.cs" }], state, true);
            Check(!tree[0].IsExpanded, "Compaction must respect manual collapse overrides.");
            tree[0].ExpandAllChildren(state);

            tree = ChangeTreeNode.Build([new Change { Path = "src/nested/file.cs" }], state, false);
            Check(tree[0].IsExpanded && tree[0].Children[0].IsExpanded, "Explicit recursive expansion must expand every represented folder.");
        }

        private static void KeepExistingBranchDefaults()
        {
            var state = new TreeExpansionState();
            var builder = BuildBranches(state);
            Check(builder.Locals.Find(x => x.Path == "refs/heads/feature").IsExpanded, "The current branch folder must keep its existing initial expansion.");
            Check(!builder.Locals.Find(x => x.Path == "refs/heads/other").IsExpanded, "Other branch folders must keep their existing initial collapse.");
            Check(state.ManualStates.Count == 0, "Automatic current-branch expansion must not record a manual choice.");
        }

        private static void KeepManualBranchCollapse()
        {
            var state = new TreeExpansionState();
            state.SetExpanded("refs/heads/feature", false);
            Check(!BuildBranches(state).Locals.Find(x => x.Path == "refs/heads/feature").IsExpanded, "A refresh must not override a manual current-branch folder collapse.");

            var filtered = new BranchTreeNode.Builder(BranchSortMode.Name, BranchSortMode.Name);
            filtered.SetExpansionState(state);
            filtered.SetExpandedNodes(["refs/heads/feature"]);
            filtered.Run([new Branch { Name = "feature/current", IsLocal = true, IsCurrent = true }], [], true);
            Check(!filtered.Locals[0].IsExpanded, "Neither filtering nor a previous expansion may override a later manual collapse.");

            state.SetExpanded("refs/heads/feature", true);
            Check(BuildBranches(state).Locals.Find(x => x.Path == "refs/heads/feature").IsExpanded, "An explicit expansion must replace the manual collapse.");
        }

        private static void KeepTagChoicesAcrossEmptyAndHiddenNodes()
        {
            var state = new TreeExpansionState();
            var tags = new List<Tag> { new Tag { Name = "release/nested/v1" } };
            var tree = TagCollectionAsTree.Build(tags, state);
            Check(!tree.Tree[0].IsExpanded && state.ManualStates.Count == 0, "Tag trees must keep their existing collapsed default without recording it.");
            tree.ToggleExpand(tree.Tree[0]);
            tree.ToggleExpand(tree.Tree[0].Children[0]);
            tree.ToggleExpand(tree.Tree[0]);
            TagCollectionAsTree.Build([], state);
            tree = TagCollectionAsTree.Build(tags, state);
            Check(!tree.Tree[0].IsExpanded && tree.Tree[0].Children[0].IsExpanded, "Tag refreshes must preserve both hidden-child and parent choices.");
            tree.ToggleExpand(tree.Tree[0]);
            Check(tree.Rows.Count == 3, "Reopening a tag parent must restore its manually expanded child.");
        }

        private static void KeepSubmoduleChoicesAcrossEmptyAndHiddenNodes()
        {
            var state = new TreeExpansionState();
            var modules = new List<Submodule> { new Submodule { Path = "libs/nested/module" } };
            var tree = SubmoduleCollectionAsTree.Build(modules, state);
            Check(!tree.Tree[0].IsExpanded && state.ManualStates.Count == 0, "Submodule trees must keep their existing collapsed default without recording it.");
            tree.ToggleExpand(tree.Tree[0]);
            tree.ToggleExpand(tree.Tree[0].Children[0]);
            tree.ToggleExpand(tree.Tree[0]);
            SubmoduleCollectionAsTree.Build([], state);
            tree = SubmoduleCollectionAsTree.Build(modules, state);
            Check(!tree.Tree[0].IsExpanded && tree.Tree[0].Children[0].IsExpanded, "Submodule refreshes must preserve both hidden-child and parent choices.");
            tree.ExpandAllChildren(tree.Tree[0]);
            Check(tree.Rows.Count == 3, "Explicit recursive expansion must expose the submodule descendants.");
        }

        private static void ExpandOnlyRequestedChangeSubtree()
        {
            var view = CreateChangeView(new TreeExpansionState());
            view.Changes = [new Change { Path = "src/nested/file.cs" }, new Change { Path = "docs/nested/file.md" }];
            var tree = GetChangeTree(view);
            foreach (var root in tree.Tree)
                view.ToggleNodeIsExpanded(root);

            view.ExpandAllChildren(tree.Tree.Find(x => x.FullPath == "src"));
            tree = GetChangeTree(view);
            Check(tree.Tree.Find(x => x.FullPath == "src").IsExpanded, "The requested subtree must expand.");
            Check(!tree.Tree.Find(x => x.FullPath == "docs").IsExpanded, "Expanding one subtree must not change a different manually collapsed folder.");
        }

        private static void ExposeExplicitExpandMenuAction()
        {
            var view = CreateChangeView(new TreeExpansionState());
            view.ToggleNodeIsExpanded(GetChangeTree(view).Tree[0]);
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = "Existing action" });
            view.CreateTreeContextMenu(menu);
            Check(menu.Items.Count == 3, "The expand action must retain existing actions and add a separator.");
            var expand = (MenuItem)menu.Items[2];
            Check((string)expand.Header == "Expand all children", "The menu must identify the explicit recursive expansion action.");
            expand.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(GetChangeTree(view).Tree[0].IsExpanded, "Only activating the menu action must override a manual collapse.");
        }

        private static void KeepRevisionFileChoicesWithCachedChildren()
        {
            var state = new TreeExpansionState();
            var leaf = new RevisionFileTreeNode { Backend = new SourceGit.Models.Object { Type = ObjectType.Blob, Path = "src/nested/file.cs" } };
            var child = new RevisionFileTreeNode { Backend = new SourceGit.Models.Object { Type = ObjectType.Tree, Path = "src/nested" }, Children = [leaf] };
            var root = new RevisionFileTreeNode { Backend = new SourceGit.Models.Object { Type = ObjectType.Tree, Path = "src" }, Children = [child] };
            var view = new RevisionFileTreeView { ExpansionState = state };
            view.Rows.Add(root);
            Check(!root.IsExpanded && state.ManualStates.Count == 0, "Revision files must keep their existing initial defaults.");
            view.ToggleNodeIsExpandedAsync(root).GetAwaiter().GetResult();
            view.ToggleNodeIsExpandedAsync(child).GetAwaiter().GetResult();
            view.ToggleNodeIsExpandedAsync(child).GetAwaiter().GetResult();
            view.ToggleNodeIsExpandedAsync(root).GetAwaiter().GetResult();
            view.ToggleNodeIsExpandedAsync(root).GetAwaiter().GetResult();
            Check(!child.IsExpanded && view.Rows.Count == 2, "Reopening a revision folder must not reopen a manually collapsed child.");
            view.ExpandAllChildrenAsync(root).GetAwaiter().GetResult();
            Check(child.IsExpanded && view.Rows.Count == 3, "Explicit expansion must include cached revision descendants.");
        }

        private static void PersistManualStates()
        {
            var folder = Path.Combine(Environment.CurrentDirectory, "_tmp", "tree_state_tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var states = RepositoryUIStates.Load(folder);
            states.UnstagedTreeExpansion.SetExpanded("src", false);
            states.BranchTreeExpansion.SetExpanded("refs/heads/feature", false);
            states.RevisionFileTreeExpansion.SetExpanded("src/nested", true);
            states.Unload(string.Empty);
            Check(File.Exists(Path.Combine(folder, "sourcegit.uistates")), "The generated JSON serializer must save the state file.");

            states = RepositoryUIStates.Load(folder);
            Check(!states.UnstagedTreeExpansion.GetIsExpanded("src", true), "A saved manual collapse must survive reopening the repository.");
            Check(!states.BranchTreeExpansion.GetIsExpanded("refs/heads/feature", true), "A saved branch collapse must survive reopening the repository.");
            Check(states.RevisionFileTreeExpansion.GetIsExpanded("src/nested", false), "A saved manual expansion must survive reopening the repository.");
            Check(states.StagedTreeExpansion.ManualStates.Count == 0, "Saving one tree must not create choices for another tree.");
        }

        private static void KeepLargeTreeStateSparse()
        {
            const int FolderCount = 10000;
            const int CollapsedFolderIndex = 123;
            var changes = new List<Change>();
            for (var i = 0; i < FolderCount; i++)
                changes.Add(new Change { Path = $"src/folder{i}/file.cs" });

            var state = new TreeExpansionState();
            var tree = ChangeTreeNode.Build(changes, state, false);
            Check(state.ManualStates.Count == 0, "A large initial tree must not store automatic expansion states.");
            var folder = tree[0].Children[CollapsedFolderIndex];
            folder.SetExpanded(state, false);
            tree = ChangeTreeNode.Build(changes, state, false);
            Check(state.ManualStates.Count == 1, "Only the manually changed folder must occupy persistent state.");
            Check(!tree[0].Children[CollapsedFolderIndex].IsExpanded, "A large refresh must preserve the manual collapse.");
            Check(tree[0].Children[CollapsedFolderIndex + 1].IsExpanded, "Untouched folders must keep their initial expanded default.");
        }

        private static void KeepStateSafeDuringBackgroundReads()
        {
            const int ChoiceCount = 2000;
            var state = new TreeExpansionState();
            Parallel.For(0, ChoiceCount, i =>
            {
                var path = $"src/folder{i}";
                state.SetExpanded(path, false);
                Check(!state.GetIsExpanded(path, true), "A background tree rebuild must safely read manual choices during updates.");
            });
            Check(state.ManualStates.Count == ChoiceCount, "Concurrent reads and writes must not lose manual choices.");
        }

        private static ChangeCollectionView CreateChangeView(TreeExpansionState state)
        {
            return new ChangeCollectionView
            {
                ExpansionState = state,
                Changes = [new Change { Path = "src/nested/file.cs" }],
            };
        }

        private static ChangeCollectionAsTree GetChangeTree(ChangeCollectionView view)
        {
            return (ChangeCollectionAsTree)view.Content;
        }

        private static BranchTreeNode.Builder BuildBranches(TreeExpansionState state)
        {
            var builder = new BranchTreeNode.Builder(BranchSortMode.Name, BranchSortMode.Name);
            builder.SetExpansionState(state);
            builder.Run([
                new Branch { Name = "feature/current", IsLocal = true, IsCurrent = true },
                new Branch { Name = "other/topic", IsLocal = true },
            ], [], false);
            return builder;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
