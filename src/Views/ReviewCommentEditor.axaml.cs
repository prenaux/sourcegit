using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class ReviewCommentEditor : UserControl
    {
        public ReviewCommentEditor()
        {
            InitializeComponent();
        }

        public void FocusEditor()
        {
            var area = BodyEditor.TextArea;
            area.Focus();
            area.Caret.Offset = area.Document.TextLength;
            area.Caret.BringCaretToView();
        }

        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ReviewCommentDraft draft)
                draft.DropEmptyComment();
        }
    }
}
