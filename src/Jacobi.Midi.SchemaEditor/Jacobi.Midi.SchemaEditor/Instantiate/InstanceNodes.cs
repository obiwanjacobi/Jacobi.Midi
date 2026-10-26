using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.SchemaEditor.Instantiate;

/// <summary>
/// A node of an instantiated schema: the values the user provides for one compiled schema node.
/// The tree of instance nodes is the result of the instantiate dialog.
/// A <see cref="SchemaPath"/> is shared by all items of a repeat; the <see cref="Instance"/> tells the iterations apart.
/// </summary>
internal abstract partial class InstanceNode : ObservableObject
{
    protected InstanceNode(SchemaNode schema, SchemaPath path)
    {
        Schema = schema;
        Path = path;
    }

    /// <summary>The compiled schema node this instance is of.</summary>
    public SchemaNode Schema { get; }

    public string Name => Schema.Name;

    /// <summary>The schema path of the node. For the body of an open choice it is relative to the body root.</summary>
    public SchemaPath Path { get; }

    public InstanceNode? Parent { get; internal set; }

    /// <summary>
    /// The repeat item indices that lead to this node, outermost to innermost (empty outside repeats).
    /// Like the engine, the body of an open choice starts anew.
    /// </summary>
    public virtual InstancePath Instance => Parent?.Instance ?? InstancePath.Empty;

    /// <summary>
    /// The full path with the item index of each repeat, e.g. <c>Message.Parts[2].Values[0]</c>,
    /// including the nodes above the body of an open choice.
    /// </summary>
    public virtual string DisplayPath => Parent is null ? Name : $"{Parent.DisplayPath}.{Name}";

    /// <summary>The instance indices as text, e.g. <c>[2][0]</c>; '-' outside repeats.</summary>
    public string InstanceText => Instance.ToLogString();

    /// <summary>
    /// Raises the changed notifications of <see cref="Instance"/> and <see cref="DisplayPath"/> for this node and the nodes below it.
    /// </summary>
    internal virtual void RefreshPaths()
    {
        OnPropertyChanged(nameof(Instance));
        OnPropertyChanged(nameof(InstanceText));
        OnPropertyChanged(nameof(DisplayPath));
    }

    /// <summary>The values below this node in schema order: the path and the text of each field.</summary>
    public IEnumerable<InstanceField> Fields() => this switch
    {
        InstanceField field => [field],
        InstanceGroup group => group.Members.SelectMany(member => member.Fields()),
        InstanceRepeat repeat => repeat.Items.SelectMany(item => item.Fields()),
        InstanceChoice { Selected: { } selected } => selected.Fields(),
        _ => []
    };

    /// <summary>The nodes directly below this node: the members of a group, the items of a repeat, the selected alternative of a choice.</summary>
    public IEnumerable<InstanceNode> Children() => this switch
    {
        InstanceGroup group => group.Members,
        InstanceRepeat repeat => repeat.Items,
        InstanceChoice { Selected: { } selected } => [selected],
        _ => []
    };

    /// <summary>
    /// The largest number of fields below a node that is expanded when it is created (see <see cref="ApplyDefaultExpansion"/>).
    /// </summary>
    public const int ExpandFieldLimit = 50;

    /// <summary>
    /// True when the members (or items) of a group (or repeat) are shown. Only the members of an expanded node get controls,
    /// so a large instance tree (e.g. the 100 patches of a device address map) is shown quickly.
    /// </summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>The number of fields below the node, shown while it is collapsed.</summary>
    public string FieldCountText => Fields().Count() is var count && count == 1 ? "1 field" : $"{count:N0} fields";

    partial void OnIsExpandedChanged(bool value)
    {
        // the count is shown while collapsed: items may have been added or removed since it was shown
        OnPropertyChanged(nameof(FieldCountText));
        OnExpandedChanged();
    }

    /// <summary>Called when <see cref="IsExpanded"/> changed: a container raises the change of the members it shows.</summary>
    protected virtual void OnExpandedChanged()
    {
    }

    /// <summary>
    /// Expands this node and the nodes below it that have at most <see cref="ExpandFieldLimit"/> fields; collapses the others.
    /// </summary>
    internal void ApplyDefaultExpansion()
    {
        IsExpanded = Fields().Take(ExpandFieldLimit + 1).Count() <= ExpandFieldLimit;
        foreach (var child in Children())
        {
            child.ApplyDefaultExpansion();
        }
    }

    /// <summary>Expands this node and the nodes above it, so it is shown.</summary>
    public void Reveal()
    {
        for (InstanceNode? node = this; node is not null; node = node.Parent)
        {
            node.IsExpanded = true;
        }
    }

    // set while the inclusion is changed by a node above or below, so the change is not passed on again
    private bool _isUpdatingInclusion;

    /// <summary>
    /// True when the node can be included in what is sent, or left out: a group, repeat or repeat item of the body of an
    /// address map (see <see cref="InstanceBody.IsAddressMap"/>).
    /// </summary>
    public bool CanInclude { get; internal set; }

    /// <summary>
    /// True when the node is included in what is sent, false when it is left out and null when only part of the nodes
    /// below it is included. Including or leaving out a node does the same with the nodes below it.
    /// In an address map the included part stays contiguous (see <see cref="InstanceBody.KeepContiguous"/>).
    /// Only used when <see cref="CanInclude"/>.
    /// </summary>
    [ObservableProperty]
    public partial bool? IsIncluded { get; set; } = false;

    /// <summary>
    /// Raised on the top node of the tree (the instance) when the inclusion of a node in it changed,
    /// or another body of an open choice was selected.
    /// </summary>
    public event EventHandler? InclusionChanged;

    partial void OnIsIncludedChanged(bool? value)
    {
        if (_isUpdatingInclusion)
        {
            return;
        }
        if (value is { } included)
        {
            IncludeBelow(included);
            AddressMapBody()?.KeepContiguous(this, included);
        }
        Parent?.RefreshInclusion();
        RaiseInclusionChanged();
    }

    // the address map body this node is in (or is); null outside an address map
    private InstanceBody? AddressMapBody()
    {
        for (var node = this; node is not null; node = node.Parent)
        {
            if (node is InstanceBody { IsAddressMap: true } body)
            {
                return body;
            }
        }
        return null;
    }

    /// <summary>The nodes at or below this one that can be included and have no such nodes below them, in tree order.</summary>
    internal IEnumerable<InstanceNode> InclusionLeaves()
    {
        var children = Children().Where(child => child.CanInclude).ToList();
        if (children.Count == 0)
        {
            return CanInclude ? [this] : [];
        }
        return children.SelectMany(child => child.InclusionLeaves());
    }

    /// <summary>
    /// Sets <see cref="IsIncluded"/> of a node without passing it on to the nodes below it; the nodes above it follow.
    /// For a node without nodes below it that can be included (see <see cref="InclusionLeaves"/>).
    /// </summary>
    internal void SetIncluded(bool included)
    {
        if (IsIncluded == included)
        {
            return;
        }
        _isUpdatingInclusion = true;
        IsIncluded = included;
        _isUpdatingInclusion = false;
        Parent?.RefreshInclusion();
    }

    /// <summary>The nodes that are included as a whole (<see cref="IsIncluded"/> true), not the nodes below them, in tree order.</summary>
    public IEnumerable<InstanceNode> IncludedNodes() => IsIncluded switch
    {
        true when CanInclude => [this],
        null when CanInclude => Children().SelectMany(child => child.IncludedNodes()),
        _ => []
    };

    /// <summary>Includes only <paramref name="node"/> (a node at or below this one) and leaves out the other nodes.</summary>
    public void IncludeOnly(InstanceNode node)
    {
        IsIncluded = false;
        node.IsIncluded = true;
    }

    /// <summary>Raises <see cref="InclusionChanged"/> on the top node of the tree.</summary>
    protected void RaiseInclusionChanged()
    {
        var top = this;
        while (top.Parent is { } parent)
        {
            top = parent;
        }
        top.InclusionChanged?.Invoke(top, EventArgs.Empty);
    }

    private void IncludeBelow(bool included)
    {
        foreach (var child in Children().Where(child => child.CanInclude))
        {
            child._isUpdatingInclusion = true;
            child.IsIncluded = included;
            child._isUpdatingInclusion = false;
            child.IncludeBelow(included);
        }
    }

    // the inclusion follows the nodes below: all, none or part of them
    private void RefreshInclusion()
    {
        if (!CanInclude)
        {
            return;
        }
        var states = Children().Where(child => child.CanInclude).Select(child => child.IsIncluded).Distinct().ToList();
        _isUpdatingInclusion = true;
        IsIncluded = states switch
        {
            [] => IsIncluded,
            [var all] => all,
            _ => null
        };
        _isUpdatingInclusion = false;
        Parent?.RefreshInclusion();
    }
}

/// <summary>
/// A field for which the user provides a value (as text).
/// A field with a constant value in the schema starts with that value and cannot be changed.
/// </summary>
internal sealed partial class InstanceField : InstanceNode
{
    private readonly Func<string, string?>? _validate;

    /// <param name="validate">Returns a warning for a value that does not fit the data type of the field, else null.</param>
    /// <param name="options">The option values of the data type of the field (as text); null or empty when it has none.</param>
    public InstanceField(SchemaField schema, SchemaPath path, string? constant, Func<string, string?>? validate = null,
        IReadOnlyList<string>? options = null)
        : base(schema, path)
    {
        _validate = validate;
        Value = constant ?? String.Empty;
        IsConstant = constant is not null;
        Options = options ?? [];
        Role = AddressMapTags.RoleOf(schema);
    }

    public SchemaField Field => (SchemaField)Schema;

    public string DataType => Field.DataType?.ToString() ?? Field.NodeDef?.ToString() ?? String.Empty;

    /// <summary>The documentation of the field (its 'description' property), if any.</summary>
    public string Description => Field.Properties.FirstOrDefault(p => String.Equals(p.Name, "description", StringComparison.OrdinalIgnoreCase))?.Value ?? String.Empty;

    /// <summary>The sample of the field (its 'sample' property), shown as a watermark.</summary>
    public string Sample => Field.Properties.FirstOrDefault(p => String.Equals(p.Name, "sample", StringComparison.OrdinalIgnoreCase))?.Value ?? String.Empty;

    /// <summary>True when the schema gives the field a constant value.</summary>
    public bool IsConstant { get; }

    /// <summary>The role of the field in an envelope message (<c>midi.role</c>, e.g. 'address'); null when it has none.</summary>
    public string? Role { get; }

    /// <summary>
    /// How the value of a role field is supplied while the message carries an address map (see
    /// <see cref="AddressMapAdapter.DescribeRole"/>), e.g. 'address: calculated from the map';
    /// null when the value is given here.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable), nameof(IsReadOnly), nameof(ShowsOptions), nameof(ValueWarning), nameof(RoleNote), nameof(HasRoleNote))]
    public partial string? RoleSupply { get; set; }

    /// <summary>True when the value is given here: not a constant and not supplied by an address map.</summary>
    public bool IsEditable => !IsConstant && RoleSupply is null;

    public bool IsReadOnly => !IsEditable;

    /// <summary>The marker of a role field: how its value is supplied, or its role when the value is given here; null without a role.</summary>
    public string? RoleNote => RoleSupply ?? (Role is null ? null : $"{Role}: the value given here");

    public bool HasRoleNote => RoleNote is not null;

    /// <summary>The option values of the data type of the field (as text), in the order of the data type; empty when it has none.</summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>True when the value is chosen from the <see cref="Options"/> (other text can still be typed, e.g. a reference).</summary>
    public bool ShowsOptions => IsEditable && Options.Count > 0;

    [ObservableProperty]
    public partial string Value { get; set; }

    /// <summary>
    /// A warning when the <see cref="Value"/> does not fit the data type of the field; otherwise null.
    /// Informational only: it does not prevent confirming the dialog.
    /// </summary>
    public string? ValueWarning => IsEditable ? _validate?.Invoke(Value) : null;

    partial void OnValueChanged(string value) => OnPropertyChanged(nameof(ValueWarning));
}

/// <summary>
/// A group (also the root, a repeat item or the body of an open choice) with the instances of its members.
/// </summary>
internal partial class InstanceGroup : InstanceNode
{
    public InstanceGroup(SchemaGroup schema, SchemaPath path)
        : base(schema, path)
    {
    }

    public ObservableCollection<InstanceNode> Members { get; } = [];

    /// <summary>The members shown in the dialog: the <see cref="Members"/> while expanded, otherwise none (no controls are created).</summary>
    public IEnumerable<InstanceNode>? ShownMembers => IsExpanded ? Members : null;

    protected override void OnExpandedChanged() => OnPropertyChanged(nameof(ShownMembers));

    internal void AddMember(InstanceNode member)
    {
        member.Parent = this;
        Members.Add(member);
    }

    internal override void RefreshPaths()
    {
        base.RefreshPaths();
        foreach (var member in Members)
        {
            member.RefreshPaths();
        }
    }
}

/// <summary>
/// The body of an open choice: a schema root that conforms to the member node def of the choice.
/// The engine reads and writes it as a separate plan, so its instance path starts anew.
/// </summary>
internal sealed class InstanceBody(SchemaGroup schema, SchemaPath path) : InstanceGroup(schema, path)
{
    public override InstancePath Instance => InstancePath.Empty;

    /// <summary>The name of the selected root (the option of the open choice), e.g. <c>Gp8.AddressMap</c>.</summary>
    public required SchemaName Root { get; init; }

    /// <summary>
    /// True when the body is an address map: its repeats have the item count of the address space, and the groups that are
    /// included (<see cref="InstanceNode.IsIncluded"/>) are sent, at the addresses the map gives them.
    /// </summary>
    public bool IsAddressMap { get; init; }

    /// <summary>Why the body could not be used as an address map (it is used as a plain group); null when there is no problem.</summary>
    public string? Note { get; init; }

    public bool HasNote => Note is not null;

    /// <summary>
    /// Keeps the included part of the map one contiguous run (the data of a message is contiguous), in tree order (the
    /// order of the addresses): leaving out <paramref name="changed"/> also leaves out everything after it; including it also
    /// includes everything between it and the part that was already included.
    /// </summary>
    internal void KeepContiguous(InstanceNode changed, bool included)
    {
        var leaves = InclusionLeaves().ToList();
        var own = changed.InclusionLeaves().ToHashSet();
        var first = leaves.FindIndex(own.Contains);
        var last = leaves.FindLastIndex(own.Contains);
        if (first < 0)
        {
            return;
        }

        if (!included)
        {
            foreach (var leaf in leaves.Skip(last + 1))
            {
                leaf.SetIncluded(false);
            }
            return;
        }

        var start = leaves.FindIndex(leaf => leaf.IsIncluded == true);
        var end = leaves.FindLastIndex(leaf => leaf.IsIncluded == true);
        foreach (var leaf in leaves.Skip(Math.Min(start, first)).Take(Math.Max(end, last) - Math.Min(start, first) + 1))
        {
            leaf.SetIncluded(true);
        }
    }
}

/// <summary>
/// One copy (item) of a repeat, holding the instances of the members of the repeat.
/// </summary>
internal sealed partial class InstanceRepeatItem : InstanceGroup
{
    public InstanceRepeatItem(SchemaGroup schema, SchemaPath path, InstanceRepeat repeat, int index)
        : base(schema, path)
    {
        Repeat = repeat;
        Index = index;
        Parent = repeat;
    }

    public InstanceRepeat Repeat { get; }

    /// <summary>
    /// The zero-based position of the item in its repeat; for the one item of an address map repeat, the item of the
    /// address space it is (<see cref="InstanceRepeat.ItemIndex"/>).
    /// </summary>
    public int Index { get; internal set; }

    public override InstancePath Instance => Repeat.Instance.Append(Index);

    public override string DisplayPath => $"{Repeat.DisplayPath}[{Index}]";

    /// <summary>Removes this item from its repeat. Only allowed for an open repeat.</summary>
    [RelayCommand]
    private void Remove() => Repeat.RemoveItem(this);
}

/// <summary>
/// A repeat. A repeat with a constant count has that many items; any other (open) repeat starts with one item
/// and the user can add and remove copies of the group.
/// A repeat of an address map (<see cref="IsAddressed"/>) has one item: the item of the address space that is selected
/// with <see cref="ItemIndex"/> (or that a matched message addressed).
/// </summary>
internal sealed partial class InstanceRepeat : InstanceNode
{
    private readonly Func<InstanceRepeat, int, InstanceRepeatItem> _createItem;

    /// <param name="addressItemCount">For a repeat of an address map: the number of items in the address space.</param>
    public InstanceRepeat(SchemaRepeat schema, SchemaPath path, int? constantCount, Func<InstanceRepeat, int, InstanceRepeatItem> createItem,
        int? addressItemCount = null)
        : base(schema, path)
    {
        _createItem = createItem;
        ConstantCount = constantCount;
        AddressItemCount = addressItemCount;
    }

    public SchemaRepeat Repeat => (SchemaRepeat)Schema;

    /// <summary>The count of the schema when it is a constant.</summary>
    public int? ConstantCount { get; }

    /// <summary>For a repeat of an address map: the number of items in the address space (<c>midi.repeat</c>); otherwise null.</summary>
    public int? AddressItemCount { get; }

    /// <summary>True for a repeat of an address map: it has one item, the item of the address space at <see cref="ItemIndex"/>.</summary>
    public bool IsAddressed => AddressItemCount is not null;

    /// <summary>True when the user decides how many items there are.</summary>
    public bool IsOpen => ConstantCount is null && !IsAddressed;

    /// <summary>The indices of the items of the address space the one item can be (empty when not <see cref="IsAddressed"/>).</summary>
    public IReadOnlyList<int> ItemIndexes => AddressItemCount is { } count ? Enumerable.Range(0, count).ToList() : [];

    /// <summary>
    /// For a repeat of an address map: the item of the address space the one item is. Changing it keeps the values, so the
    /// same data can be sent as another item (at another address).
    /// </summary>
    [ObservableProperty]
    public partial int ItemIndex { get; set; }

    partial void OnItemIndexChanged(int value)
    {
        if (!IsAddressed || Items is not [var item])
        {
            return;
        }
        item.Index = value;
        item.RefreshPaths();
        // the address of what is sent changed
        RaiseInclusionChanged();
    }

    /// <summary>
    /// Describes how the count is determined: its constant, its reference, the number of items of an address map
    /// or 'until the end'.
    /// </summary>
    public string CountText => Repeat.Count switch
    {
        int constant => $"count {constant}",
        SchemaNodeRef reference => $"count {reference}",
        SchemaPubRef reference => $"count {reference}",
        _ when AddressItemCount is { } items => $"{items} items in the address map, item",
        _ => "until the end of the data"
    };

    public ObservableCollection<InstanceRepeatItem> Items { get; } = [];

    /// <summary>The items shown in the dialog: the <see cref="Items"/> while expanded, otherwise none (no controls are created).</summary>
    public IEnumerable<InstanceRepeatItem>? ShownItems => IsExpanded ? Items : null;

    protected override void OnExpandedChanged() => OnPropertyChanged(nameof(ShownItems));

    /// <summary>Adds a copy of the repeat group at the end and returns it; it is expanded when it is small.</summary>
    public InstanceRepeatItem AddNewItem()
    {
        var item = _createItem(this, Items.Count);
        item.ApplyDefaultExpansion();
        Items.Add(item);
        return item;
    }

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void AddItem() => AddNewItem();

    internal void RemoveItem(InstanceRepeatItem item)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException($"The repeat '{DisplayPath}' has a constant count; items cannot be removed.");
        }
        if (!Items.Remove(item))
        {
            return;
        }
        for (var index = 0; index < Items.Count; index++)
        {
            Items[index].Index = index;
            Items[index].RefreshPaths();
        }
    }

    /// <summary>
    /// Ensures the repeat has an item at <paramref name="index"/> (adding copies of the group when needed) and returns it.
    /// Used when the values are read: the data decides how many items there are. A repeat of an address map has one item:
    /// it becomes the item at <paramref name="index"/>.
    /// </summary>
    internal InstanceRepeatItem GetOrAddItem(int index)
    {
        if (IsAddressed && Items is [var item])
        {
            ItemIndex = index;
            return item;
        }
        while (Items.Count <= index)
        {
            AddNewItem();
        }
        return Items[index];
    }

    /// <summary>
    /// Removes the items from <paramref name="count"/> on (of an open repeat only), e.g. after reading fewer items than it held.
    /// </summary>
    internal void Truncate(int count)
    {
        while (IsOpen && Items.Count > count)
        {
            Items.RemoveAt(Items.Count - 1);
        }
    }

    internal override void RefreshPaths()
    {
        base.RefreshPaths();
        foreach (var item in Items)
        {
            item.RefreshPaths();
        }
    }
}

/// <summary>
/// A selectable alternative of a choice.
/// </summary>
/// <param name="Name">The name shown to the user: the member name of a choice, or the full root name of an open choice.</param>
/// <param name="Create">Creates the instance of the alternative for the given choice.</param>
internal sealed record ChoiceOption(string Name, Func<InstanceChoice, InstanceNode> Create)
{
    public override string ToString() => Name;
}

/// <summary>
/// A choice. The user selects the alternative: a member of the choice or, for an open choice, the body that is looked up
/// by the member node def of the choice (the roots that conform to it). A constant selected index preselects (and fixes) the alternative.
/// </summary>
internal sealed partial class InstanceChoice : InstanceNode
{
    public InstanceChoice(SchemaChoice schema, SchemaPath path, IReadOnlyList<ChoiceOption> options, int? constantIndex)
        : base(schema, path)
    {
        Options = options;
        CanSelect = constantIndex is null;
        if (constantIndex is { } index && index >= 0 && index < options.Count)
        {
            SelectedOption = options[index];
        }
    }

    public SchemaChoice Choice => (SchemaChoice)Schema;

    /// <summary>True for an open choice: the body is a root looked up by the member node def.</summary>
    public bool IsOpen => Choice.MemberNodeDef is not null;

    /// <summary>The contract the bodies of an open choice conform to.</summary>
    public string MemberNodeDef => Choice.MemberNodeDef?.ToString() ?? String.Empty;

    public IReadOnlyList<ChoiceOption> Options { get; }

    /// <summary>False when the selected index of the schema is a constant.</summary>
    public bool CanSelect { get; }

    public bool HasOptions => Options.Count > 0;

    public bool HasNoOptions => Options.Count == 0;

    public string NoOptionsText => IsOpen
        ? $"No group conforms to '{MemberNodeDef}'."
        : "The choice has no alternatives.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelected))]
    public partial ChoiceOption? SelectedOption { get; set; }

    /// <summary>The instance of the selected alternative; null until one is selected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelected))]
    public partial InstanceNode? Selected { get; private set; }

    public bool HasSelected => Selected is not null;

    partial void OnSelectedOptionChanged(ChoiceOption? value)
    {
        var selected = value?.Create(this);
        if (selected is not null)
        {
            selected.Parent = this;
            // the selected alternative is shown; the large parts below it start collapsed
            selected.ApplyDefaultExpansion();
            selected.IsExpanded = true;
        }
        Selected = selected;
        // another body may include other nodes (or none)
        RaiseInclusionChanged();
    }

    internal override void RefreshPaths()
    {
        base.RefreshPaths();
        Selected?.RefreshPaths();
    }
}
