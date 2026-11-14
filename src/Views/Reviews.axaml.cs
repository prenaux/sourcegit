using System;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class Reviews : UserControl
    {
        private readonly DispatcherTimer _sourceTimer = new() { Interval = TimeSpan.FromSeconds(2) };
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

        private ThemedTextDiffPresenter FindDiffPresenter() => this.FindDescendantOfType<TextDiffView>()?.FindActivePresenter();

        private void OnReviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not ViewModels.Reviews model || model.IsBusy)
                return;
            if (e.Key == Key.Escape && (model.Context?.IsEditing == true || model.HasPendingReattach))
            {
                model.Context?.CloseEditor();
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

        public ContextMenu CreateFileContextMenu(ViewModels.ReviewFileItem file)
        {
            if (DataContext is not ViewModels.Reviews model)
                return null;
            var path = file.Path;
            var fullPath = model.GetFullPath(path);
            var copyPath = new MenuItem { Header = App.Text("CopyPath"), Icon = App.CreateMenuIcon("Icons.Copy") };
            copyPath.Click += async (_, e) =>
            {
                await App.CopyTextAsync(path);
                e.Handled = true;
            };
            var copyFullPath = new MenuItem { Header = App.Text("CopyFullPath"), Icon = App.CreateMenuIcon("Icons.Copy") };
            copyFullPath.Click += async (_, e) =>
            {
                await App.CopyTextAsync(fullPath);
                e.Handled = true;
            };
            var menu = new ContextMenu();
            menu.Items.Add(copyPath);
            menu.Items.Add(copyFullPath);
            return menu;
        }

        private void OnFileContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (DataContext is not ViewModels.Reviews model)
                return;
            var container = e.Source as ListBoxItem ?? (e.Source as Control)?.FindAncestorOfType<ListBoxItem>();
            if (container?.DataContext is ViewModels.ReviewFileItem clicked)
                model.SelectedFile = clicked;
            if (model.SelectedFile == null)
                return;
            CreateFileContextMenu(model.SelectedFile)?.Open(FileList);
            e.Handled = true;
        }

        private void OnCommentsKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || DataContext is not ViewModels.Reviews { SelectedComment: { } selected } model)
                return;
            model.Context?.EditComment(selected.Data);
            Dispatcher.UIThread.Post(() => this.GetVisualDescendants().OfType<ReviewCommentEditor>()
                .FirstOrDefault(x => x.IsEffectivelyVisible)?.FocusEditor());
            e.Handled = true;
        }

        private void OnGotoFirstChange(object sender, RoutedEventArgs e) => FindDiffPresenter()?.MoveReviewBoundary(ViewModels.BlockNavigationDirection.First, KeyModifiers.None);
        private void OnGotoPrevChange(object sender, RoutedEventArgs e) => FindDiffPresenter()?.MoveReviewBoundary(ViewModels.BlockNavigationDirection.Prev, KeyModifiers.None);
        private void OnGotoNextChange(object sender, RoutedEventArgs e) => FindDiffPresenter()?.MoveReviewBoundary(ViewModels.BlockNavigationDirection.Next, KeyModifiers.None);
        private void OnGotoLastChange(object sender, RoutedEventArgs e) => FindDiffPresenter()?.MoveReviewBoundary(ViewModels.BlockNavigationDirection.Last, KeyModifiers.None);
    }
}
