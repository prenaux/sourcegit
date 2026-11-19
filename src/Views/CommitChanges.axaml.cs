using System;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class CommitChanges : UserControl
    {
        public CommitChanges()
        {
            InitializeComponent();
        }

        private void OnChangeContextRequested(object sender, ContextRequestedEventArgs e)
        {
            e.Handled = true;

            if (sender is not ChangeCollectionView { SelectedChanges: { Count: > 0 } changes } view)
                return;

            var detailView = this.FindAncestorOfType<CommitDetail>();
            if (detailView == null)
                return;

            var paths = view.GetSelectedPaths();
            var container = view.FindDescendantOfType<ChangeCollectionContainer>();
            ContextMenu menu;
            if (container is { SelectedItems.Count: 1, SelectedItem: ViewModels.ChangeTreeNode { IsFolder: true } node })
                menu = detailView.CreateChangeContextMenuByFolder(node, changes);
            else if (paths.Count > 1 || changes.Count > 1)
                menu = detailView.CreateMultipleChangesContextMenu(changes, paths);
            else
                menu = detailView.CreateChangeContextMenu(changes[0]);

            ReviewMenu.PrependFileItems(menu, (DataContext as ViewModels.CommitDetail)?.Review, changes);
            view.CreateTreeContextMenu(menu)?.Open(view);
        }

        private async void OnChangeCollectionViewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not ViewModels.CommitDetail vm)
                return;

            if (sender is not ChangeCollectionView { SelectedChanges: { Count: > 0 } } view)
                return;

            var cmdKey = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            if (e.Key == Key.C && e.KeyModifiers.HasFlag(cmdKey))
            {
                var paths = view.GetSelectedPaths();
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    await ChangeCollectionView.CopyFullPathsAsync(paths, vm.GetAbsPath);
                else
                    await ChangeCollectionView.CopyPathsAsync(paths);

                e.Handled = true;
            }
            else if (e.Key == Key.F && e.KeyModifiers == cmdKey)
            {
                CommitChangeSearchBox.Focus();
                e.Handled = true;
            }
        }
    }
}
