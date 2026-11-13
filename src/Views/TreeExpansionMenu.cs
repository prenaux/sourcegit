using System;

using Avalonia.Controls;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public static class TreeExpansionMenu
    {
        public static bool IsRequestedOnEmptySpace(Control owner, ContextRequestedEventArgs e)
        {
            if (!e.TryGetPosition(owner, out _))
                return false;

            var source = e.Source as Control;
            return source is not ListBoxItem && source?.FindAncestorOfType<ListBoxItem>() == null;
        }

        public static ContextMenu AppendExpandAllChildren(ContextMenu menu, Action expand)
        {
            menu ??= new ContextMenu();
            if (menu.Items.Count > 0)
                menu.Items.Add(new MenuItem { Header = "-" });

            var item = new MenuItem { Header = App.Text("Tree.ExpandAllChildren") };
            item.Click += (_, e) =>
            {
                expand();
                e.Handled = true;
            };
            menu.Items.Add(item);
            return menu;
        }
    }
}
