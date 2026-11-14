using Avalonia.Data.Converters;

namespace SourceGit.Converters
{
    public static class ReviewConverters
    {
        public static readonly FuncValueConverter<Models.ReviewCommentState, string> ToStateLabel = new(value => App.Text("Reviews.State." + value));

        public static readonly FuncValueConverter<Models.ReviewSourceKind, string> ToSourceLabel = new(value => App.Text("Reviews.Source." + value));

        public static readonly FuncValueConverter<Models.ReviewSnapshot, string> ToSnapshotLabel = new(value => value == null ? string.Empty : $"{value.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {value.Id[..8]}");
    }
}
