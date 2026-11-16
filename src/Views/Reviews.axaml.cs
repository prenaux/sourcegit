using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class Reviews : UserControl
    {
        private readonly DispatcherTimer _sourceTimer = new() { Interval = TimeSpan.FromSeconds(2) };
        private ViewModels.Reviews _model;
        private bool _isActive;

        public Reviews()
        {
            InitializeComponent();
            AddHandler(KeyDownEvent, OnReviewKeyDown, RoutingStrategies.Tunnel);
            _sourceTimer.Tick += async (_, _) =>
            {
                if (DataContext is ViewModels.Reviews model)
                    await model.CheckSourceAsync();
            };
        }

        protected override async void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            _isActive = true;
            if (DataContext is ViewModels.Reviews model)
            {
                await model.InitializeAsync();
                await model.CheckSourceAsync();
            }
            if (_isActive)
                _sourceTimer.Start();
        }

        protected override async void OnUnloaded(RoutedEventArgs e)
        {
            _isActive = false;
            _sourceTimer.Stop();
            if (DataContext is ViewModels.Reviews model)
                await model.FlushAsync();
            base.OnUnloaded(e);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            if (_model != null)
                _model.FocusEditorRequested -= OnFocusEditorRequested;
            if (_model != null)
                _model.CommentTargetRequested -= OnCommentTargetRequested;
            _model = DataContext as ViewModels.Reviews;
            if (_model != null)
            {
                _model.FocusEditorRequested += OnFocusEditorRequested;
                _model.CommentTargetRequested += OnCommentTargetRequested;
            }
            base.OnDataContextChanged(e);
        }

        private ThemedTextDiffPresenter FindDiffPresenter() => this.FindDescendantOfType<TextDiffView>()?.FindActivePresenter();

        private void OnCommentTargetRequested(object sender, EventArgs e) => FindDiffPresenter()?.AddReviewComment();

        private void OnFocusEditorRequested(object sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is not ViewModels.Reviews model)
                    return;
                var editor = model.ActiveTabIndex switch
                {
                    0 => CommentEditor,
                    1 => FileCommentEditor,
                    _ => null,
                };
                editor?.FocusEditor();
            }, DispatcherPriority.Loaded);
        }

        private void OnReviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not ViewModels.Reviews model || model.IsBusy)
                return;
            if (e.Key == Key.Escape && (model.Context?.IsEditing == true || model.FileContext?.IsEditing == true || model.HasPendingReattach))
            {
                model.Context?.CloseEditor();
                model.FileContext?.CloseEditor();
                model.CancelReattach();
                FindDiffPresenter()?.TextArea.Focus();
                e.Handled = true;
                return;
            }
            var primary = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (!primary || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                return;
            if (e.Key == Key.M && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                FindDiffPresenter()?.AddReviewComment();
                e.Handled = true;
                return;
            }
            if (e.Source is Avalonia.Visual visual && (visual.FindAncestorOfType<ReviewCommentEditor>() != null ||
                visual.FindAncestorOfType<CommitMessageTextEditor>() != null))
                return;
            var direction = e.Key switch
            {
                Key.Up => ViewModels.BlockNavigationDirection.Prev,
                Key.Down => ViewModels.BlockNavigationDirection.Next,
                _ => (ViewModels.BlockNavigationDirection?)null,
            };
            if (direction != null)
            {
                FindDiffPresenter()?.MoveReviewBoundary(direction.Value, e.KeyModifiers);
                e.Handled = true;
            }
        }

        private void OnCommentsKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || DataContext is not ViewModels.Reviews { SelectedComment: { } selected } model)
                return;
            model.Context?.EditComment(selected.Data);
            e.Handled = true;
        }

        private void OnClearFileFilter(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Reviews model)
                model.FileFilter = string.Empty;
            e.Handled = true;
        }

        private void OnFilesContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (DataContext is not ViewModels.Reviews model || sender is not ChangeCollectionView view)
                return;
            if (model.SelectedFileItems.Count == 0)
            {
                var clicked = (e.Source as Control)?.FindAncestorOfType<ListBoxItem>()?.DataContext switch
                {
                    ViewModels.ChangeTreeNode node => node.Change,
                    Models.Change change => change,
                    _ => null,
                };
                if (clicked != null)
                    model.SelectedChanges = [clicked];
            }
            var menu = CreateFileContextMenu(model, view);
            menu?.Open(view);
            e.Handled = true;
        }

        public ContextMenu CreateFileContextMenu(ViewModels.Reviews model, ChangeCollectionView view)
        {
            var files = model.SelectedFileItems;
            if (files.Count == 0)
                return null;

            var menu = new ContextMenu();
            var editFileComment = new MenuItem
            {
                Header = App.Text("Reviews.EditFileComment"),
                Icon = App.CreateMenuIcon("Icons.Edit"),
                IsEnabled = model.CanEditFileComment,
            };
            editFileComment.Click += (_, e) =>
            {
                model.OpenFileComment();
                e.Handled = true;
            };

            var markOk = new MenuItem
            {
                Header = model.CanMarkFilesAsOk ? App.Text("Reviews.MarkFileOk") :
                    App.Text("Reviews.MarkFileOkBlocked", model.SelectedToFixCount),
                Icon = App.CreateMenuIcon("Icons.Check"),
                IsEnabled = model.CanMarkFilesAsOk,
            };
            markOk.Click += (_, e) =>
            {
                model.MarkFilesAsOk();
                e.Handled = true;
            };

            var setComments = new MenuItem
            {
                Header = App.Text("Reviews.SetComments"),
                Icon = App.CreateMenuIcon("Icons.Fix"),
            };
            foreach (var state in model.StateChoices)
            {
                var entry = new MenuItem { Header = App.Text("Reviews.State." + state) };
                entry.Click += (_, e) =>
                {
                    model.SetSelectedCommentsState(state);
                    e.Handled = true;
                };
                setComments.Items.Add(entry);
            }

            menu.Items.Add(editFileComment);
            menu.Items.Add(markOk);
            menu.Items.Add(setComments);
            menu.Items.Add(new MenuItem { Header = "-" });

            var savePatch = new MenuItem
            {
                Header = App.Text("FileCM.SaveAsPatch"),
                Icon = App.CreateMenuIcon("Icons.Save"),
                IsEnabled = model.CanExportPatch,
            };
            savePatch.Click += async (_, e) =>
            {
                await SaveSelectedPatchAsync(model);
                e.Handled = true;
            };
            menu.Items.Add(savePatch);

            var copyPatch = new MenuItem
            {
                Header = App.Text("FileCM.CopyAsPatch"),
                Icon = App.CreateMenuIcon("Icons.Copy"),
                IsEnabled = model.CanExportPatch,
            };
            copyPatch.Click += async (_, e) =>
            {
                await model.CopyPatchAsync();
                e.Handled = true;
            };
            menu.Items.Add(copyPatch);

            if (files.Count == 1)
            {
                var fullPath = model.GetFullPath(files[0].Path);
                menu.Items.Add(new MenuItem { Header = "-" });
                TryAddOpenFileToContextMenu(menu, fullPath);
                TryAddWorkingFileToolsToContextMenu(menu, model, files[0].Path, fullPath);
                TryAddCustomActionsToContextMenu(menu, model, files[0].Path);
            }

            menu.Items.Add(new MenuItem { Header = "-" });
            AddCopyPathsToContextMenu(menu, model, files.ConvertAll(x => x.Path));
            return view.CreateTreeContextMenu(menu);
        }

        private void TryAddOpenFileToContextMenu(ContextMenu menu, string fullpath)
        {
            var openWith = new MenuItem();
            openWith.Header = App.Text("Open");
            openWith.Icon = App.CreateMenuIcon("Icons.OpenWith");
            openWith.IsEnabled = File.Exists(fullpath);
            if (openWith.IsEnabled)
            {
                var defaultEditor = new MenuItem();
                defaultEditor.Header = App.Text("Open.SystemDefaultEditor");
                defaultEditor.Tag = OperatingSystem.IsMacOS() ? "Cmd+O" : "Ctrl+O";
                defaultEditor.Click += (_, ev) =>
                {
                    Native.OS.OpenWithDefaultEditor(fullpath);
                    ev.Handled = true;
                };
                openWith.Items.Add(defaultEditor);

                var tools = Native.OS.ExternalTools;
                if (tools.Count > 0)
                {
                    openWith.Items.Add(new MenuItem { Header = "-" });
                    foreach (var tool in tools)
                    {
                        var item = new MenuItem();
                        item.Header = tool.Name;
                        item.Icon = new Avalonia.Controls.Image { Width = 16, Height = 16, Source = tool.IconImage };
                        item.Click += (_, e) =>
                        {
                            tool.Launch(fullpath.Quoted());
                            e.Handled = true;
                        };
                        openWith.Items.Add(item);
                    }
                }
            }
            menu.Items.Add(openWith);
        }

        private void TryAddWorkingFileToolsToContextMenu(ContextMenu menu, ViewModels.Reviews model, string path, string fullpath)
        {
            var explore = new MenuItem();
            explore.Header = App.Text("RevealFile");
            explore.Icon = App.CreateMenuIcon("Icons.Explore");
            explore.IsEnabled = File.Exists(fullpath) || Directory.Exists(fullpath);
            explore.Click += (_, e) =>
            {
                Native.OS.OpenInFileManager(fullpath);
                e.Handled = true;
            };
            menu.Items.Add(explore);

            var history = new MenuItem();
            history.Header = App.Text("FileHistory");
            history.Icon = App.CreateMenuIcon("Icons.Histories");
            history.Click += (_, e) =>
            {
                App.ShowWindow(new ViewModels.FileHistories(model.RepositoryPath, path));
                e.Handled = true;
            };

            var blame = new MenuItem();
            blame.Header = App.Text("Blame") + " (HEAD-only)";
            blame.Icon = App.CreateMenuIcon("Icons.Blame");
            blame.Click += async (_, e) =>
            {
                var commit = await new Commands.QuerySingleCommit(model.RepositoryPath, "HEAD").GetResultAsync();
                App.ShowWindow(new ViewModels.Blame(model.RepositoryPath, path, commit));
                e.Handled = true;
            };

            menu.Items.Add(history);
            menu.Items.Add(blame);
        }

        private void TryAddCustomActionsToContextMenu(ContextMenu menu, ViewModels.Reviews model, string path)
        {
            var repo = model.Owner;
            if (repo == null)
                return;
            var actions = repo.GetCustomActions(Models.CustomActionScope.File);
            if (actions.Count == 0)
                return;
            var target = new Models.CustomActionTargetFile(path, null);
            menu.Items.Add(new MenuItem { Header = "-" });
            foreach (var (dup, label) in actions)
            {
                var item = new MenuItem();
                item.Icon = App.CreateMenuIcon("Icons.Action");
                item.Header = label;
                item.Click += async (_, e) =>
                {
                    await repo.ExecCustomActionAsync(dup, target);
                    e.Handled = true;
                };
                menu.Items.Add(item);
            }
        }

        private static void AddCopyPathsToContextMenu(ContextMenu menu, ViewModels.Reviews model, System.Collections.Generic.List<string> paths)
        {
            var copyPath = new MenuItem { Header = App.Text("CopyPath"), Icon = App.CreateMenuIcon("Icons.Copy") };
            copyPath.Click += async (_, e) =>
            {
                await ChangeCollectionView.CopyPathsAsync(paths);
                e.Handled = true;
            };

            var copyFullPath = new MenuItem { Header = App.Text("CopyFullPath"), Icon = App.CreateMenuIcon("Icons.Copy") };
            copyFullPath.Click += async (_, e) =>
            {
                await ChangeCollectionView.CopyFullPathsAsync(paths, model.GetFullPath);
                e.Handled = true;
            };

            menu.Items.Add(copyPath);
            menu.Items.Add(copyFullPath);
        }

        private async Task SaveSelectedPatchAsync(ViewModels.Reviews model)
        {
            var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storageProvider == null)
                return;
            var patch = model.GetSelectedPatchText();
            if (patch.Length == 0)
                return;
            var options = new FilePickerSaveOptions
            {
                Title = App.Text("FileCM.SaveAsPatch"),
                DefaultExtension = ".patch",
                FileTypeChoices = [new FilePickerFileType("Patch File") { Patterns = ["*.patch"] }],
            };
            try
            {
                var storageFile = await storageProvider.SaveFilePickerAsync(options);
                if (storageFile != null)
                    await File.WriteAllTextAsync(storageFile.Path.LocalPath, patch);
            }
            catch (Exception exception)
            {
                App.RaiseException(model.RepositoryPath, $"Failed to save as patch: {exception.Message}");
            }
        }

        private void OnGotoFirstChange(object sender, RoutedEventArgs e) => FindDiffPresenter()?.MoveReviewBoundary(ViewModels.BlockNavigationDirection.First, KeyModifiers.None);
        private void OnGotoPrevChange(object sender, RoutedEventArgs e) => FindDiffPresenter()?.MoveReviewBoundary(ViewModels.BlockNavigationDirection.Prev, KeyModifiers.None);
        private void OnGotoNextChange(object sender, RoutedEventArgs e) => FindDiffPresenter()?.MoveReviewBoundary(ViewModels.BlockNavigationDirection.Next, KeyModifiers.None);
        private void OnGotoLastChange(object sender, RoutedEventArgs e) => FindDiffPresenter()?.MoveReviewBoundary(ViewModels.BlockNavigationDirection.Last, KeyModifiers.None);
    }
}
