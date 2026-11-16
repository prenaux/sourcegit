using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SourceGit.Views
{
    public partial class ReviewCommentEditor : UserControl
    {
        private ViewModels.ReviewDiffContext _context;

        public void FocusEditor()
        {
            var area = BodyEditor.TextArea;
            area.Focus();
            area.Caret.Offset = area.Document.TextLength;
            area.Caret.BringCaretToView();
        }

        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ReviewDiffContext context)
                context.DropEmpty();
        }

        public ReviewCommentEditor()
        {
            InitializeComponent();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            ConnectContext();
            if (_context?.IsEditing == true)
                OnEditorOpened(_context, EventArgs.Empty);
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            if (_context != null)
                _context.EditorOpened -= OnEditorOpened;
            _context = null;
            base.OnUnloaded(e);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (IsLoaded)
                ConnectContext();
        }

        private void ConnectContext()
        {
            if (_context != null)
                _context.EditorOpened -= OnEditorOpened;
            _context = DataContext as ViewModels.ReviewDiffContext;
            if (_context != null)
                _context.EditorOpened += OnEditorOpened;
        }

        private void OnEditorOpened(object sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_context?.IsEditing == true && IsEffectivelyVisible)
                    FocusEditor();
            }, DispatcherPriority.Loaded);
        }
    }
}
