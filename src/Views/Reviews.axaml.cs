using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    // The REVIEWS page: the list of the saved reviews of the repository.
    public partial class Reviews : UserControl
    {
        public Reviews()
        {
            InitializeComponent();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (DataContext is ViewModels.Reviews model)
                model.RefreshItems();
        }

        private async void OnReviewListDoubleTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is ViewModels.Reviews model)
                await model.OpenSelectedAsync();
            e.Handled = true;
        }

        private async void OnOpenReview(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Reviews model)
                await model.OpenSelectedAsync();
            e.Handled = true;
        }

        private async void OnCopyReview(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Reviews model)
                await model.CopySelectedAsync();
            e.Handled = true;
        }

        private async void OnDeleteReview(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Reviews { SelectedItem: { } selected } model &&
                await App.AskConfirmAsync(App.Text("Reviews.Delete.Confirm", selected.Title)))
                model.DeleteSelected();
            e.Handled = true;
        }
    }
}
