using Jacobi.Midi.Console.Core.Parameters;

namespace Jacobi.Midi.Console.Core.Dashboards;

/// <summary>
/// The rules of the pages of <see cref="SelectorTabsControl"/>: a page belongs to the option its label names (matched
/// without case); bound to another field the selector tabs get a page per option of that field; a page whose option
/// is not among the options, or an option with more than one page, is a problem.
/// </summary>
public static class SelectorPages
{
    private static readonly StringComparer OptionComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>True when the page belongs to the option: its label is the option's name, matched without case.</summary>
    public static bool IsPageOf(DashboardControl page, string option)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(option);
        return page.Label is { } label && OptionComparer.Equals(label, option);
    }

    /// <summary>The (zero based) number of the option a page belongs to; null for a page of no option.</summary>
    public static int? OptionOf(DashboardControl page, IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);
        for (var index = 0; index < options.Count; index++)
        {
            if (IsPageOf(page, options[index]))
                return index;
        }
        return null;
    }

    /// <summary>The (zero based) number of the page of an option; null when no page belongs to it.</summary>
    public static int? PageOf(SelectorTabsControl tabs, string? option)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        if (option is null)
            return null;
        for (var index = 0; index < tabs.Children.Count; index++)
        {
            if (IsPageOf(tabs.Children[index], option))
                return index;
        }
        return null;
    }

    /// <summary>
    /// New, empty pages for the options, in their order: grids named after the options, in the size the selector tabs
    /// give their pages (<see cref="GridLayout.PageSize"/>).
    /// </summary>
    public static IReadOnlyList<DashboardControl> For(SelectorTabsControl tabs, IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(options);
        var size = GridLayout.PageSize(tabs.Size ?? GridLayout.DefaultSize(tabs.Kind));
        return [.. options.Select(option => new LayoutControl { Kind = LayoutControlKind.Grid, Label = option, Size = size })];
    }

    /// <summary>
    /// What the user chooses the pages from: every option in its order, with whether it has a page, and after them the
    /// pages of no option (they can only be removed). The page of the only page cannot be removed (see
    /// <see cref="CanRemove"/>).
    /// </summary>
    public static IReadOnlyList<PageChoice> Choices(SelectorTabsControl tabs, IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(options);
        var isLast = tabs.Children.Count == 1;
        return
        [
            .. options.Select(option => PageOf(tabs, option) is not null
                ? new PageChoice(option, HasPage: true, IsOfNoOption: false) { CanChange = !isLast }
                : new PageChoice(option, HasPage: false, IsOfNoOption: false)),
            .. tabs.Children.Where(page => OptionOf(page, options) is null)
                .Select((page, index) => new PageChoice(page.Label ?? $"Page {index + 1}", HasPage: true, IsOfNoOption: true) { CanChange = !isLast }),
        ];
    }

    /// <summary>
    /// True when the page can be taken away from its selector tabs: they keep at least one page (without pages they
    /// show nothing; to remove them, the selector tabs are deleted).
    /// </summary>
    public static bool CanRemove(SelectorTabsControl tabs, DashboardControl page)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(page);
        return tabs.Children.Contains(page) && tabs.Children.Count > 1;
    }

    /// <summary>
    /// The pages with a new, empty page for an option, among the others in the order of the options (before the first
    /// page of a later option); null when the option has a page already or is not an option.
    /// </summary>
    public static IReadOnlyList<DashboardControl>? WithPage(SelectorTabsControl tabs, IReadOnlyList<string> options, string option)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(option);
        var number = options.ToList().FindIndex(other => OptionComparer.Equals(other, option));
        if (number < 0 || PageOf(tabs, option) is not null)
            return null;
        var pages = tabs.Children.ToList();
        var at = pages.FindIndex(page => OptionOf(page, options) > number);
        pages.Insert(at < 0 ? pages.Count : at, For(tabs, [options[number]])[0]);
        return pages;
    }

    /// <summary>
    /// The pages without those of a name (an option, or the label of a page of no option); null when there are none, or
    /// when no page would be left (see <see cref="CanRemove"/>).
    /// </summary>
    public static IReadOnlyList<DashboardControl>? WithoutPage(SelectorTabsControl tabs, string name)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(name);
        var pages = tabs.Children.Where(page => !IsPageOf(page, name)).ToList();
        return pages.Count == tabs.Children.Count || pages.Count == 0 ? null : pages;
    }

    /// <summary>
    /// The pages the selector tabs get when their <c>value</c> slot is bound from one field to another: a page per
    /// option of the new field. Null when the pages stay: the slot is unbound, or still bound to the same field (a
    /// binding written again, or another item of the same repeat).
    /// </summary>
    public static IReadOnlyList<DashboardControl>? Rebound(SelectorTabsControl tabs, BoundField? before, BoundField? after)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        if (after is null || after.Info.Options is not { } options || (before is not null && before.IsSameField(after)))
            return null;
        return For(tabs, options);
    }

    /// <summary>What does not fit between the pages and the options: a page of no option, an option with more pages.</summary>
    public static IReadOnlyList<string> Problems(SelectorTabsControl tabs, IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        foreach (var page in tabs.Children)
        {
            if (OptionOf(page, options) is null)
                problems.Add($"the page '{page.Label}' is of no option; it is never shown (options: {String.Join(", ", options)}).");
        }
        foreach (var option in options.Where(option => tabs.Children.Count(page => IsPageOf(page, option)) > 1))
            problems.Add($"the option '{option}' has more than one page; the first is shown.");
        return problems;
    }
}

/// <summary>
/// A choice of the pages of selector tabs: an option (or the label of a page of no option), whether it has a page, and
/// whether it is a page of no option.
/// </summary>
public sealed record PageChoice(string Name, bool HasPage, bool IsOfNoOption)
{
    /// <summary>False for the only page: selector tabs keep at least one (see <see cref="SelectorPages.CanRemove"/>).</summary>
    public bool CanChange { get; init; } = true;
}

/// <summary>
/// The field a binding refers to: the device, its schema path from the map root without repeat indices (as
/// <see cref="ParameterReference.Path"/>) and what the field is.
/// </summary>
public sealed record BoundField(string Device, string Path, ParameterInfo Info)
{
    /// <summary>True when both are the same field of the same device (whatever repeat item).</summary>
    public bool IsSameField(BoundField other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return String.Equals(Device, other.Device, StringComparison.OrdinalIgnoreCase) && Path == other.Path;
    }
}
