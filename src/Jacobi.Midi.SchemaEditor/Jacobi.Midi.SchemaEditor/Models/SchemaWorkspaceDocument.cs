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

    public EditDocument Schema { get; }
    public long Revision { get; private set; }

    [ObservableProperty]
    public partial string? FileName { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    public string DisplayName => $"{Schema.Name}{(IsDirty ? " *" : String.Empty)}";

    public void ApplyNodeEdits(EditNode node, string name, string dataType, string description, string sampleValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        SchemaName? fieldType = String.IsNullOrWhiteSpace(dataType) ? (SchemaName?)null : new SchemaName(dataType);
        if (node is EditField field)
        {
            if (fieldType is null && field.NodeDef is null)
            {
                throw new ArgumentException("A field data type or node definition is required.", nameof(dataType));
            }
            field.DataType = fieldType;
        }
        node.Name = name;
        SetProperty(node, "description", description);
        SetProperty(node, "sample", sampleValue);
        MarkChanged();
    }

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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureCanAddMembers(parent);
        var group = new EditGroup(parent)
        {
            Name = name,
            Members = [],
            Processors = [],
            Properties = [],
        };
        parent.Members.Add(group);
        MarkChanged();
        return group;
    }

    public EditRepeat AddRepeat(EditGroup parent, string name, int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureCanAddMembers(parent);
        var repeat = new EditRepeat(parent)
        {
            Name = name,
            Count = count,
            Members = [],
            Processors = [],
            Properties = [],
            ValueProcessors = [],
        };
        parent.Members.Add(repeat);
        MarkChanged();
        return repeat;
    }

    public EditChoice AddChoice(EditGroup parent, string name, int selectedIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureCanAddMembers(parent);
        var choice = new EditChoice(parent)
        {
            Name = name,
            SelectedIndex = selectedIndex,
            Members = [],
            Processors = [],
            Properties = [],
            ValueProcessors = [],
        };
        parent.Members.Add(choice);
        MarkChanged();
        return choice;
    }

    /// <summary>
    /// Removes <paramref name="node"/> from its parent group, or from the node defs when it is a top-level definition.
    /// </summary>
    public void RemoveNode(EditNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var removed = node switch
        {
            EditDocument => throw new ArgumentException("The document itself cannot be removed.", nameof(node)),
            { Parent: { } parent } => parent.Members.Remove(node),
            _ => Schema.NodeDefs.Remove(node)
        };
        if (!removed)
        {
            throw new ArgumentException($"Node '{node.Name}' is not part of this document.", nameof(node));
        }
        MarkChanged();
    }

    private void MarkChanged()
    {
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
