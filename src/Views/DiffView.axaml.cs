using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class DiffView : UserControl
    {
        public DiffView()
        {
            InitializeComponent();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (DataContext is ViewModels.DiffContext vm)
                vm.CheckSettings();
        }

        public ThemedTextDiffPresenter FindActivePresenter()
        {
            return this.FindDescendantOfType<TextDiffView>()?.FindActivePresenter();
        }

        private void OnGotoFirstChange(object _, RoutedEventArgs e)
        {
            this.FindDescendantOfType<ThemedTextDiffPresenter>()?.GotoChange(ViewModels.BlockNavigationDirection.First);
            e.Handled = true;
        }

        private void OnGotoPrevChange(object _, RoutedEventArgs e)
        {
            FindActivePresenter()?.MoveCaretToBoundary(ViewModels.BlockNavigationDirection.Prev, KeyModifiers.None);
            e.Handled = true;
        }

        private void OnGotoNextChange(object _, RoutedEventArgs e)
        {
            FindActivePresenter()?.MoveCaretToBoundary(ViewModels.BlockNavigationDirection.Next, KeyModifiers.None);
            e.Handled = true;
        }

        private void OnGotoLastChange(object _, RoutedEventArgs e)
        {
            this.FindDescendantOfType<ThemedTextDiffPresenter>()?.GotoChange(ViewModels.BlockNavigationDirection.Last);
            e.Handled = true;
        }

        private void OnAddReviewComment(object _, RoutedEventArgs e)
        {
            FindActivePresenter()?.AddReviewComment();
            e.Handled = true;
        }

        private void OnGotoPrevComment(object _, RoutedEventArgs e)
        {
            FindActivePresenter()?.GotoComment(ViewModels.BlockNavigationDirection.Prev);
            e.Handled = true;
        }

        private void OnGotoNextComment(object _, RoutedEventArgs e)
        {
            FindActivePresenter()?.GotoComment(ViewModels.BlockNavigationDirection.Next);
            e.Handled = true;
        }

        private void OnOpenReviewMenu(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.DiffContext { Review: { } review } && sender is Button button)
                ReviewMenu.CreateActionsMenu(review, this).Open(button);
            e.Handled = true;
        }
    }
}
