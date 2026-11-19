using System;
using System.Collections.Generic;

using Avalonia.Controls;

namespace SourceGit.Views
{
    public static class ReviewMenu
    {
        // The menu of the Review button in the diff toolbar. It lists every review action with its shortcut.
        public static ContextMenu CreateActionsMenu(ViewModels.ReviewSession review, DiffView view)
        {
            var menu = new ContextMenu();

            var comment = CreateItem(review.CommentActionLabel, "Icons.Message", () => view.FindActivePresenter()?.AddReviewComment());
            comment.Tag = "Ctrl+M";
            menu.Items.Add(comment);

            var prev = CreateItem(App.Text("Reviews.PrevComment"), "Icons.Up",
                () => view.FindActivePresenter()?.GotoComment(ViewModels.BlockNavigationDirection.Prev));
            prev.Tag = "Ctrl+Alt+Up";
            menu.Items.Add(prev);

            var next = CreateItem(App.Text("Reviews.NextComment"), "Icons.Down",
                () => view.FindActivePresenter()?.GotoComment(ViewModels.BlockNavigationDirection.Next));
            next.Tag = "Ctrl+Alt+Down";
            menu.Items.Add(next);

            var selectedChange = review.SelectedChange;
            if (selectedChange != null)
            {
                menu.Items.Add(new MenuItem { Header = "-" });
                foreach (var item in CreateFileItems(review, [selectedChange]))
                    menu.Items.Add(item);
            }

            menu.Items.Add(new MenuItem { Header = "-" });
            menu.Items.Add(CreateItem(App.Text("Reviews.CopyReview"), "Icons.Copy", () => _ = review.CopyReviewAsync()));
            if (review.IsLocal)
                menu.Items.Add(CreateItem(App.Text("Reviews.NewReview"), "Icons.Plus", review.StartNewLocalReview));
            return menu;
        }

        // Puts the review actions for the selected files before the standard file actions.
        public static void PrependFileItems(ContextMenu menu, ViewModels.ReviewSession review, List<Models.Change> changes)
        {
            if (menu == null || review is not { HasReview: true } || changes == null || changes.Count == 0)
                return;

            var index = 0;
            foreach (var item in CreateFileItems(review, changes))
                menu.Items.Insert(index++, item);
            menu.Items.Insert(index, new MenuItem { Header = "-" });
        }

        private static List<MenuItem> CreateFileItems(ViewModels.ReviewSession review, List<Models.Change> changes)
        {
            var editFileComment = CreateItem(App.Text("Reviews.EditFileComment"), "Icons.Edit", review.OpenFileComment);
            editFileComment.IsEnabled = changes.Count == 1 && changes[0].Path.Equals(review.SelectedChange?.Path, StringComparison.Ordinal);

            var toFixCount = review.CountToFix(changes);
            var markOk = CreateItem(toFixCount == 0 ? App.Text("Reviews.MarkFileOk") : App.Text("Reviews.MarkFileOkBlocked", toFixCount),
                "Icons.Check", () => review.MarkFilesAsOk(changes));
            markOk.IsEnabled = toFixCount == 0;

            var setComments = new MenuItem { Header = App.Text("Reviews.SetComments"), Icon = App.CreateMenuIcon("Icons.Fix") };
            foreach (var state in review.StateChoices)
            {
                var entry = new MenuItem { Header = App.Text("Reviews.State." + state) };
                entry.Click += (_, e) =>
                {
                    review.SetCommentsState(changes, state);
                    e.Handled = true;
                };
                setComments.Items.Add(entry);
            }

            return [editFileComment, markOk, setComments];
        }

        private static MenuItem CreateItem(string header, string icon, Action execute)
        {
            var item = new MenuItem { Header = header, Icon = App.CreateMenuIcon(icon) };
            item.Click += (_, e) =>
            {
                execute();
                e.Handled = true;
            };
            return item;
        }
    }
}
