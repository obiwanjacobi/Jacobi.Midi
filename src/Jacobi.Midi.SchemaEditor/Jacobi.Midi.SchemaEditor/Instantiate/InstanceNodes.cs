using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;

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
}

/// <summary>
/// A field for which the user provides a value (as text).
/// A field with a constant value in the schema starts with that value and cannot be changed.
/// </summary>
internal sealed partial class InstanceField : InstanceNode
{
    public InstanceField(SchemaField schema, SchemaPath path, string? constant)
        : base(schema, path)
    {
        Value = constant ?? String.Empty;
        IsConstant = constant is not null;
    }

    public SchemaField Field => (SchemaField)Schema;

    public string DataType => Field.DataType?.ToString() ?? Field.NodeDef?.ToString() ?? String.Empty;

    /// <summary>The documentation of the field (its 'description' property), if any.</summary>
    public string Description => Field.Properties.FirstOrDefault(p => String.Equals(p.Name, "description", StringComparison.OrdinalIgnoreCase))?.Value ?? String.Empty;

    /// <summary>The sample of the field (its 'sample' property), shown as a watermark.</summary>
    public string Sample => Field.Properties.FirstOrDefault(p => String.Equals(p.Name, "sample", StringComparison.OrdinalIgnoreCase))?.Value ?? String.Empty;

    /// <summary>True when the schema gives the field a constant value.</summary>
    public bool IsConstant { get; }

    public bool IsEditable => !IsConstant;

    [ObservableProperty]
    public partial string Value { get; set; }
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

    /// <summary>The zero-based position of the item in its repeat.</summary>
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
/// </summary>
internal sealed partial class InstanceRepeat : InstanceNode
{
    private readonly Func<InstanceRepeat, int, InstanceRepeatItem> _createItem;

    public InstanceRepeat(SchemaRepeat schema, SchemaPath path, int? constantCount, Func<InstanceRepeat, int, InstanceRepeatItem> createItem)
        : base(schema, path)
    {
        _createItem = createItem;
        ConstantCount = constantCount;
    }

    public SchemaRepeat Repeat => (SchemaRepeat)Schema;

    /// <summary>The count of the schema when it is a constant.</summary>
    public int? ConstantCount { get; }

    /// <summary>True when the user decides how many items there are.</summary>
    public bool IsOpen => ConstantCount is null;

    /// <summary>Describes how the count is determined: its constant, its reference or 'until the end'.</summary>
    public string CountText => Repeat.Count switch
    {
        int constant => $"count {constant}",
        SchemaNodeRef reference => $"count {reference}",
        SchemaPubRef reference => $"count {reference}",
        _ => "until the end of the data"
    };

    public ObservableCollection<InstanceRepeatItem> Items { get; } = [];

    /// <summary>Adds a copy of the repeat group at the end and returns it.</summary>
    public InstanceRepeatItem AddNewItem()
    {
        var item = _createItem(this, Items.Count);
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
        Selected = value?.Create(this);
        if (Selected is not null)
        {
            Selected.Parent = this;
        }
    }

    internal override void RefreshPaths()
    {
        base.RefreshPaths();
        Selected?.RefreshPaths();
    }
}
