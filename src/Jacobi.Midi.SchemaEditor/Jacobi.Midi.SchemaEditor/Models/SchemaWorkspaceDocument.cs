using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Services;

namespace Jacobi.Midi.SchemaEditor.Models;

internal sealed partial class SchemaWorkspaceDocument : ObservableObject
{
    public SchemaWorkspaceDocument(EditDocument schema, SchemaFormat format, string? fileName = null)
    {
        Schema = schema;
        Format = format;
        FileName = fileName;
    }

    public SchemaFormat Format { get; }

    /// <summary>
    /// True for the schema of an opened assembly: it is shown but cannot be changed.
    /// </summary>
    public bool IsReadOnly { get; init; }

    public EditDocument Schema { get; }
    public long Revision { get; private set; }

    [ObservableProperty]
    public partial string? FileName { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    /// <summary>
    /// True to keep the document in the schema tree when another document is selected (session only).
    /// </summary>
    [ObservableProperty]
    public partial bool IsPinned { get; set; }

    public string DisplayName =>$"{Schema.Name}{(IsDirty ? " *" : String.Empty)}";

    public EditField AddField(EditGroup parent, string name, string dataType, string sampleValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataType);
        EnsureCanAddMembers(parent);
        var field = new EditField(parent)
        {
            Name = name,
            DataType = new SchemaName(dataType),
            Processors = [],
            Properties = [],
        };
        SetProperty(field, "sample", sampleValue);
        parent.Members.Add(field);
        MarkChanged();
        return field;
    }

    public EditGroup AddGroup(EditGroup parent, string name)
        => AddMember(parent, (EditGroup)CreateNode(SchemaNodeKind.Group, parent, name));

    public EditRepeat AddRepeat(EditGroup parent, string name, int count)
    {
        var repeat = (EditRepeat)CreateNode(SchemaNodeKind.Repeat, parent, name);
        repeat.Count = count;
        return AddMember(parent, repeat);
    }

    public EditChoice AddChoice(EditGroup parent, string name, int selectedIndex)
    {
        var choice = (EditChoice)CreateNode(SchemaNodeKind.Choice, parent, name);
        choice.SelectedIndex = selectedIndex;
        return AddMember(parent, choice);
    }

    /// <summary>
    /// Adds a top-level node definition of <paramref name="kind"/> with default values.
    /// </summary>
    public EditNode AddNodeDef(SchemaNodeKind kind, string name)
        => AddItem(Schema.NodeDefs, CreateNode(kind, null, name));

    public EditDocumentRef AddInclude(string schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        return AddItem(Schema.Includes, new EditDocumentRef { Schema = schema });
    }

    public EditDataTypeDef AddDataTypeDef(string name, string basedOn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(basedOn);
        return AddItem(Schema.DataTypeDefs, new EditDataTypeDef { Name = name, BasedOn = new SchemaName(basedOn) });
    }

    public EditProcessorDef AddProcessorDef(string name, string processor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(processor);
        return AddItem(Schema.ProcessorDefs, new EditProcessorDef { Name = name, Processor = processor });
    }

    /// <summary>
    /// Adds a processor to the document itself.
    /// </summary>
    public EditProcessorRef AddProcessor(string processor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processor);
        return AddItem(Schema.Processors, new EditProcessorRef { Processor = new SchemaName(processor) });
    }

    /// <summary>
    /// Adds a property to the document itself.
    /// </summary>
    public EditProperty AddProperty(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return AddItem(Schema.Properties, new EditProperty { Name = name, Value = value });
    }

    /// <summary>
    /// Removes <paramref name="node"/> from its parent group, or from the node defs when it is a top-level definition.
    /// </summary>
    public void RemoveNode(EditNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        RemoveItem(node);
    }

    /// <summary>
    /// Removes a node or a document collection item (include, def, document processor or property).
    /// </summary>
    public void RemoveItem(object item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var removed = item switch
        {
            EditDocument => throw new ArgumentException("The document itself cannot be removed.", nameof(item)),
            EditNode { Parent: { } parent } node => parent.Members.Remove(node),
            EditNode node => Schema.NodeDefs.Remove(node),
            EditDocumentRef include => Schema.Includes.Remove(include),
            EditDataTypeDef definition => Schema.DataTypeDefs.Remove(definition),
            EditProcessorDef definition => Schema.ProcessorDefs.Remove(definition),
            EditProcessorRef processor => Schema.Processors.Remove(processor),
            EditProperty property => Schema.Properties.Remove(property),
            _ => throw new NotSupportedException($"Cannot remove a {item.GetType().Name}.")
        };
        if (!removed)
        {
            throw new ArgumentException("The item is not part of this document.", nameof(item));
        }
        MarkChanged();
    }

    /// <summary>
    /// True when <paramref name="node"/> can move <paramref name="offset"/> places among its siblings (see <see cref="MoveNode"/>).
    /// </summary>
    public bool CanMoveNode(EditNode node, int offset)
        => FindSiblings(node) is { } siblings && siblings.IndexOf(node) + offset is var index && index >= 0 && index < siblings.Count;

    /// <summary>
    /// Moves <paramref name="node"/> <paramref name="offset"/> places among its siblings: the members of its parent group,
    /// or the node defs for a top-level node def. A negative offset moves it up.
    /// </summary>
    public void MoveNode(EditNode node, int offset)
    {
        if (!CanMoveNode(node, offset))
        {
            throw new InvalidOperationException($"'{node.Name}' cannot be moved {(offset < 0 ? "up" : "down")}.");
        }
        var siblings = GetSiblings(node);
        var index = siblings.IndexOf(node);
        siblings.Move(index, index + offset);
        MarkChanged();
    }

    /// <summary>
    /// True when <paramref name="node"/> can be moved into the group right before it (see <see cref="IndentNode"/>).
    /// </summary>
    public bool CanIndentNode(EditNode node)
        => FindSiblings(node) is { } siblings && siblings.IndexOf(node) is > 0 and var index
            && siblings[index - 1] is EditGroup and not EditChoice { MemberNodeDef: not null };

    /// <summary>
    /// Moves <paramref name="node"/> into the group right before it (a group, repeat or closed choice), as its last member.
    /// </summary>
    public void IndentNode(EditNode node)
    {
        if (!CanIndentNode(node))
        {
            throw new InvalidOperationException($"'{node.Name}' has no group before it to move into.");
        }
        var siblings = GetSiblings(node);
        var group = (EditGroup)siblings[siblings.IndexOf(node) - 1];
        siblings.Remove(node);
        node.Parent = group;
        group.Members.Add(node);
        MarkChanged();
    }

    /// <summary>
    /// True when <paramref name="node"/> can be moved out of its parent group (see <see cref="OutdentNode"/>).
    /// Not for a top-level member or a member of a top-level node def: it would leave the document or the node def.
    /// </summary>
    public bool CanOutdentNode(EditNode node)
        => node.Parent is { Parent: { } outer } parent && parent.Members.Contains(node) && outer.Members.Contains(parent);

    /// <summary>
    /// Moves <paramref name="node"/> out of its parent group into the group that holds the parent, right after the parent.
    /// </summary>
    public void OutdentNode(EditNode node)
    {
        if (!CanOutdentNode(node))
        {
            throw new InvalidOperationException($"'{node.Name}' cannot be moved out of its parent.");
        }
        var parent = node.Parent!;
        var outer = parent.Parent!;
        parent.Members.Remove(node);
        node.Parent = outer;
        outer.Members.Insert(outer.Members.IndexOf(parent) + 1, node);
        MarkChanged();
    }

    /// <summary>
    /// The values of <paramref name="node"/> that are lost when its kind changes to <paramref name="kind"/> (see <see cref="ChangeNodeKind"/>):
    /// the count of a repeat, the selected index, member node def and pipeline of a choice and their value processors,
    /// when they differ from the defaults of a new node. Empty when nothing is lost.
    /// </summary>
    public static IReadOnlyList<string> GetLostByKindChange(EditGroup node, SchemaNodeKind kind)
    {
        var lost = new List<string>();
        if (node.Kind == kind)
        {
            return lost;
        }
        switch (node)
        {
            case EditRepeat repeat:
                if (repeat.Count is not 1)
                {
                    lost.Add("count");
                }
                if (repeat.ValueProcessors.Count > 0)
                {
                    lost.Add("value processors");
                }
                break;
            case EditChoice choice:
                if (choice.SelectedIndex is not 0)
                {
                    lost.Add("selected index");
                }
                if (choice.MemberNodeDef is not null)
                {
                    lost.Add("member node def");
                }
                if (choice.Pipeline is not null)
                {
                    lost.Add("pipeline");
                }
                if (choice.ValueProcessors.Count > 0)
                {
                    lost.Add("value processors");
                }
                break;
        }
        return lost;
    }

    /// <summary>
    /// Replaces <paramref name="node"/> (a group, repeat or choice) by a node of <paramref name="kind"/> at the same place.
    /// The new node keeps the name, node def, byte size, properties, processors and members; the values only the old kind has
    /// are dropped (see <see cref="GetLostByKindChange"/>) and the new kind gets the defaults of a new node.
    /// </summary>
    /// <returns>The new node.</returns>
    public EditGroup ChangeNodeKind(EditGroup node, SchemaNodeKind kind)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is EditDocument || kind is not (SchemaNodeKind.Group or SchemaNodeKind.Repeat or SchemaNodeKind.Choice))
        {
            throw new NotSupportedException($"Cannot change '{node.Name}' into a {kind}.");
        }
        if (node.Kind == kind)
        {
            return node;
        }
        var siblings = GetSiblings(node);
        var changed = (EditGroup)CreateNode(kind, node.Parent, node.Name);
        changed.NodeDef = node.NodeDef;
        changed.ByteSize = node.ByteSize;
        changed.Properties = node.Properties;
        changed.Processors = node.Processors;
        changed.Members = node.Members;
        foreach (var member in changed.Members)
        {
            member.Parent = changed;
        }
        siblings[siblings.IndexOf(node)] = changed;
        MarkChanged();
        return changed;
    }

    /// <summary>
    /// The collection that holds <paramref name="node"/>: the members of its parent group, or the node defs for a top-level node def.
    /// Null for the document itself or a node that is not part of this document.
    /// </summary>
    private ObservableCollection<EditNode>? FindSiblings(EditNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Parent is { } parent)
        {
            return parent.Members.Contains(node) ? parent.Members : null;
        }
        return Schema.NodeDefs.Contains(node) ? Schema.NodeDefs : null;
    }

    private ObservableCollection<EditNode> GetSiblings(EditNode node)
        => FindSiblings(node) ?? throw new ArgumentException($"'{node.Name}' is not part of this document.", nameof(node));

    /// <summary>
    /// Creates a node of <paramref name="kind"/> with default values: a field is a sys.uint8,
    /// a repeat repeats once and a choice selects its first member.
    /// </summary>
    private static EditNode CreateNode(SchemaNodeKind kind, EditGroup? parent, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return kind switch
        {
            SchemaNodeKind.Field => new EditField(parent) { Name = name, DataType = new SchemaName("sys.uint8"), Processors = [], Properties = [] },
            SchemaNodeKind.Group => new EditGroup(parent) { Name = name, Members = [], Processors = [], Properties = [] },
            SchemaNodeKind.Repeat => new EditRepeat(parent) { Name = name, Count = 1, Members = [], Processors = [], Properties = [], ValueProcessors = [] },
            SchemaNodeKind.Choice => new EditChoice(parent) { Name = name, SelectedIndex = 0, Members = [], Processors = [], Properties = [], ValueProcessors = [] },
            _ => throw new NotSupportedException($"Cannot create a node of kind {kind}.")
        };
    }

    private T AddMember<T>(EditGroup parent, T node) where T : EditNode
    {
        EnsureCanAddMembers(parent);
        return AddItem(parent.Members, node);
    }

    private T AddItem<T, TItem>(IList<TItem> collection, T item) where T : TItem
    {
        collection.Add(item);
        MarkChanged();
        return item;
    }

    /// <summary>
    /// Registers an edit of the draft: increments the revision and marks the document dirty.
    /// </summary>
    public void MarkChanged()
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException($"'{Schema.Name}' is read-only.");
        }
        Revision++;
        IsDirty = true;
        OnPropertyChanged(nameof(DisplayName));
    }

    private static void EnsureCanAddMembers(EditGroup parent)
    {
        if (parent is EditChoice { MemberNodeDef: not null })
        {
            throw new InvalidOperationException("Open choices select their body at runtime and cannot contain explicit members.");
        }
    }

    private static void SetProperty(EditNode node, string name, string value)
    {
        var property = node.Properties.FirstOrDefault(candidate => String.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (String.IsNullOrWhiteSpace(value))
        {
            if (property is not null)
            {
                node.Properties.Remove(property);
            }
            return;
        }
        if (property is null)
        {
            node.Properties.Add(new EditProperty { Name = name, Value = value });
        }
        else
        {
            property.Value = value;
        }
    }

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(DisplayName));
}
