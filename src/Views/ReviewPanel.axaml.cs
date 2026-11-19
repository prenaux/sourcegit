using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    // The tabs that edit a review: Comment, File comment and General review.
    // A host page can add its own first tab with `CommitContent`.
    public partial class ReviewPanel : UserControl
    {
        public static readonly StyledProperty<object> CommitContentProperty =
            AvaloniaProperty.Register<ReviewPanel, object>(nameof(CommitContent));

        public object CommitContent
        {
            get => GetValue(CommitContentProperty);
            set => SetValue(CommitContentProperty, value);
        }

        public static readonly StyledProperty<bool> ShowCommentsProperty =
            AvaloniaProperty.Register<ReviewPanel, bool>(nameof(ShowComments), true);

        public bool ShowComments
        {
            get => GetValue(ShowCommentsProperty);
            set => SetValue(ShowCommentsProperty, value);
        }

        public ReviewPanel()
        {
            InitializeComponent();
            AddHandler(KeyDownEvent, OnPanelKeyDown, RoutingStrategies.Tunnel);
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            ConnectSession();
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);
            DisconnectSession();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            ConnectSession();
        }

        private void ConnectSession()
        {
            DisconnectSession();
            _session = DataContext as ViewModels.ReviewSession;
            if (_session != null)
                _session.FocusEditorRequested += OnFocusEditorRequested;
        }

        private void DisconnectSession()
        {
            if (_session != null)
                _session.FocusEditorRequested -= OnFocusEditorRequested;
            _session = null;
        }

        private void OnFocusEditorRequested(object sender, EventArgs e)
        {
            // The tab content exists only after the layout pass that follows the tab change.
            Dispatcher.UIThread.Post(() =>
            {
                if (_session == null || !IsEffectivelyVisible)
                    return;

                if (_session.ActiveTab == ViewModels.ReviewTab.Comment)
                    CommentEditor.FocusEditor();
                else if (_session.ActiveTab == ViewModels.ReviewTab.FileComment)
                    FileCommentEditor.FocusEditor();
            }, DispatcherPriority.Loaded);
        }

        private void OnPanelKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || _session == null || e.Source is not Visual source)
                return;

            if (source.FindAncestorOfType<ReviewCommentEditor>(true) == null && !GeneralReviewEditor.IsVisualAncestorOf(source))
                return;

            _session.FocusDiff();
            e.Handled = true;
        }

        // The list reports no change for a click on the selected row. The diff must still go to that comment.
        private void OnCommentListTapped(object sender, TappedEventArgs e)
        {
            if (_session?.SelectedComment is { } selected)
                _session.ShowComment(selected.Data);
        }

        private void OnCommentListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || _session?.SelectedComment is not { } selected)
                return;

            _session.EditComment(selected.Data);
            e.Handled = true;
        }

        private ViewModels.ReviewSession _session;
    }
}
