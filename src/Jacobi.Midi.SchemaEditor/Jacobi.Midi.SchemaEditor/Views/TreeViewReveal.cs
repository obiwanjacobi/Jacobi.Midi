using Avalonia.Controls;

namespace Jacobi.Midi.SchemaEditor.Views;

/// <summary>
/// Makes an item deep in a <see cref="TreeView"/> visible: expands the items above it (realizing their child containers
/// level by level) and scrolls it into view. Used when an item is selected from code, e.g. when a reference is followed.
/// </summary>
internal static class TreeViewReveal
{
    /// <summary>
    /// Expands the tree items along <paramref name="path"/> (from a root down) and brings <paramref name="item"/>,
    /// a child of the last item of the path (or a root for an empty path), into view.
    /// Stops at the first item of the path that has no container (e.g. it is not shown in the tree).
    /// </summary>
    /// <param name="item">The item to bring into view; null to only expand the path.</param>
    public static void Reveal(TreeView tree, IReadOnlyList<object> path, object? item = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(path);
        // a root item may just have been added
        tree.UpdateLayout();
        ItemsControl level = tree;
        foreach (var ancestor in path)
        {
            if (level.ContainerFromItem(ancestor) is not TreeViewItem container)
            {
                return;
            }
            container.IsExpanded = true;
            // realize the child containers so the next level can be found
            container.UpdateLayout();
            level = container;
        }
        if (item is not null)
        {
            level.ContainerFromItem(item)?.BringIntoView();
        }
    }
}
