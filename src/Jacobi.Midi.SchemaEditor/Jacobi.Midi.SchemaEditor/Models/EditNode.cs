using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.SchemaEditor.Models;

internal abstract partial class EditNode : ObservableObject
{
    public SchemaNodeKind Kind { get; protected init; }

    [ObservableProperty]
    public partial string Name { get; set; }
    [ObservableProperty]
    public partial SchemaName? NodeDef { get; set; }
    [ObservableProperty]
    public required partial ObservableCollection<EditProperty> Properties { get; set; }

    public EditGroup? Parent { get; set; }

    /// <summary>
    /// The address map tag that marks the node in the schema tree: its role (<c>midi.role</c>, e.g. 'address') or the kind
    /// of message it is (<c>midi.message</c>, e.g. 'message: request'); null when it has neither.
    /// </summary>
    public string? AddressTag
        => FindProperty(AddressMapTags.Role) is { } role ? role
        : FindProperty(AddressMapTags.Message) is { } message ? $"message: {message}"
        : null;

    private string? FindProperty(string name)
        => Properties?.FirstOrDefault(property => String.Equals(property.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))?.Value?.Trim() is { Length: > 0 } value
            ? value : null;

    // the tag follows the properties: added, removed, renamed or changed
    partial void OnPropertiesChanged(ObservableCollection<EditProperty>? oldValue, ObservableCollection<EditProperty> newValue)
    {
        if (oldValue is not null)
        {
            oldValue.CollectionChanged -= OnPropertiesCollectionChanged;
            foreach (var property in oldValue)
            {
                property.PropertyChanged -= OnPropertyEdited;
            }
        }
        if (newValue is not null)
        {
            newValue.CollectionChanged += OnPropertiesCollectionChanged;
            foreach (var property in newValue)
            {
                property.PropertyChanged += OnPropertyEdited;
            }
        }
        OnPropertyChanged(nameof(AddressTag));
    }

    private void OnPropertiesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var property in e.OldItems?.OfType<EditProperty>() ?? [])
        {
            property.PropertyChanged -= OnPropertyEdited;
        }
        // a reset gives no old items: subscribe the current items once more (unsubscribing first keeps it once)
        var added = e.Action == NotifyCollectionChangedAction.Reset ? Properties : e.NewItems?.OfType<EditProperty>() ?? [];
        foreach (var property in added)
        {
            property.PropertyChanged -= OnPropertyEdited;
            property.PropertyChanged += OnPropertyEdited;
        }
        OnPropertyChanged(nameof(AddressTag));
    }

    private void OnPropertyEdited(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(AddressTag));
}

internal partial class EditField : EditNode
{
    public EditField(EditGroup? parent)
    {
        Parent = parent;
        Kind = SchemaNodeKind.Field;
    }

    [ObservableProperty]
    public partial SchemaName? DataType { get; set; }
    [ObservableProperty]
    public partial SchemaValueOrRef<string> Value { get; set; }
    [ObservableProperty]
    public partial SchemaValueOrRef<int> ByteLength { get; set; }
    [ObservableProperty]
    public partial int? ByteOffset { get; set; }
    [ObservableProperty]
    public required partial ObservableCollection<EditProcessorRef> Processors { get; set; }
}

internal partial class EditGroup : EditNode
{
    public EditGroup(EditGroup? parent)
    {
        Parent = parent;
        Kind = SchemaNodeKind.Group;
    }

    [ObservableProperty]
    public partial SchemaValueOrRef<int> ByteSize { get; set; }
    [ObservableProperty]
    public required partial ObservableCollection<EditNode> Members { get; set; }
    [ObservableProperty]
    public required partial ObservableCollection<EditProcessorRef> Processors { get; set; }
}

internal partial class EditRepeat : EditGroup
{
    public EditRepeat(EditGroup? parent)
        : base(parent)
    {
        Kind = SchemaNodeKind.Repeat;
    }

    [ObservableProperty]
    public partial SchemaValueOrRef<int> Count { get; set; }
    [ObservableProperty]
    public required partial ObservableCollection<EditProcessorRef> ValueProcessors { get; set; }
}

internal partial class EditChoice : EditGroup
{
    public EditChoice(EditGroup? parent)
        : base(parent)
    {
        Kind = SchemaNodeKind.Choice;
    }

    [ObservableProperty]
    public partial SchemaValueOrRef<int> SelectedIndex { get; set; }
    [ObservableProperty]
    public partial SchemaName? MemberNodeDef { get; set; }
    [ObservableProperty]
    public partial SchemaPipelineMode? Pipeline { get; set; }
    [ObservableProperty]
    public required partial ObservableCollection<EditProcessorRef> ValueProcessors { get; set; }
}

internal partial class EditDocument : EditGroup
{
    public EditDocument() : base(null)
        => Kind = SchemaNodeKind.Schema;

    [ObservableProperty]
    public required partial ObservableCollection<EditNode> NodeDefs { get; set; }
    [ObservableProperty]
    public required partial ObservableCollection<EditDataTypeDef> DataTypeDefs { get; set; }
    [ObservableProperty]
    public required partial ObservableCollection<EditProcessorDef> ProcessorDefs { get; set; }
    [ObservableProperty]
    public required partial ObservableCollection<EditDocumentRef> Includes { get; set; }
}

internal partial class EditDataTypeDef : ObservableObject
{
    [ObservableProperty]
    public required partial string Name { get; set; }
    [ObservableProperty]
    public required partial SchemaName BasedOn { get; set; }
    /// <summary>A literal of the value type.</summary>
    [ObservableProperty]
    public partial string? Min { get; set; }
    /// <summary>A literal of the value type.</summary>
    [ObservableProperty]
    public partial string? Max { get; set; }
    [ObservableProperty]
    public partial decimal? Scale { get; set; }
    [ObservableProperty]
    public partial decimal? Shift { get; set; }
    /// <summary>The physical values (literals of the data type) mapped to their logical values (literals of the value type).</summary>
    [ObservableProperty]
    public partial Dictionary<string, string>? Options { get; set; }
    /// <summary>The data type of the logical value; null for the implied value type.</summary>
    [ObservableProperty]
    public partial SchemaName? ValueType { get; set; }
    /// <summary>The logical value read for a physical value that is not one of the options.</summary>
    [ObservableProperty]
    public partial string? OptionsDefault { get; set; }
    [ObservableProperty]
    public partial ObservableCollection<EditProcessorRef> Processors { get; set; } = [];
    [ObservableProperty]
    public partial ObservableCollection<EditProperty> Properties { get; set; } = [];
}

internal partial class EditProcessorRef : ObservableObject
{
    [ObservableProperty]
    public required partial SchemaName Processor { get; set; }
    [ObservableProperty]
    public partial string? PublishNamespace { get; set; }
    [ObservableProperty]
    public partial ObservableCollection<EditProperty> Properties { get; set; } = [];
}

internal partial class EditProcessorDef : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; }
    [ObservableProperty]
    public partial string Processor { get; set; }
    [ObservableProperty]
    public partial ObservableCollection<EditProperty> Properties { get; set; } = [];
}

internal partial class EditProperty : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; }
    [ObservableProperty]
    public partial string? Value { get; set; }
}

internal partial class EditDocumentRef : ObservableObject
{
    [ObservableProperty]
    public partial string Schema { get; set; }
    [ObservableProperty]
    public partial string? Path { get; set; }

    /// <summary>
    /// False when the included document is not loaded in the workspace.
    /// Maintained by the workspace; not part of the schema.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoaded))]
    public partial bool IsLoaded { get; set; } = true;

    public bool IsNotLoaded => !IsLoaded;
}
