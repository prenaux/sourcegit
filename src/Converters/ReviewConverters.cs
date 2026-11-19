using Avalonia.Data.Converters;

namespace SourceGit.Converters
{
    public static class ReviewConverters
    {
        public static readonly FuncValueConverter<Models.ReviewCommentState, string> ToStateLabel = new(value => App.Text("Reviews.State." + value));

        public static readonly FuncValueConverter<ViewModels.ReviewFileFilter, string> ToReviewFilterLabel = new(value => App.Text("Reviews.Filter." + value));
    }
}
