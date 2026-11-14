using System;
using Avalonia.Data.Converters;

namespace SourceGit.Converters
{
    public static class ReviewConverters
    {
        public static readonly FuncValueConverter<Models.ReviewCommentState, string> ToStateLabel = new(value => App.Text("Reviews.State." + value));

        public static readonly FuncValueConverter<Models.ReviewSourceKind, string> ToSourceLabel = new(value => App.Text("Reviews.Source." + value));

        public static readonly FuncValueConverter<Models.ReviewSnapshot, string> ToSnapshotLabel = new(value => value == null ? string.Empty :
            $"{value.Subject}  {value.TargetRevision[..Math.Min(8, value.TargetRevision.Length)]}  ({value.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm})");
    }
}
