using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.NodeEditor;

/// <summary>
/// View model of the Processors and Properties tabs of the node editor.
/// Shows the effective processors and properties of one item (a node, data type def, processor def or
/// document processor; see <see cref="EffectiveNodeResolver"/>) and edits the item's own entries in place.
/// Inherited entries are read-only (properties can be overridden).
/// Rebuilds its rows when the structure of the item's own entries changes; dispose it to stop listening.
/// </summary>
internal sealed partial class ItemDetailsViewModel : ObservableObject, IDisposable
{
    private readonly SchemaWorkspaceDocument _document;
    private readonly INotifyPropertyChanged _item;
    private readonly IList<EditProperty> _properties;
    private readonly IList<EditProcessorRef>? _processors;
    private readonly Func<EffectiveItem> _resolve;
    private readonly List<INotifyPropertyChanged> _observedItems = [];
    private readonly List<INotifyCollectionChanged> _observedCollections = [];

    private readonly EditProcessorDef? _definition;
    private readonly bool _isDocument;

    private ItemDetailsViewModel(SchemaWorkspaceDocument document, INotifyPropertyChanged item,
        IList<EditProperty> properties, IList<EditProcessorRef>? processors, Func<EffectiveItem> resolve,
        EditProcessorDef? definition = null, bool isDocument = false)
    {
        _isDocument = isDocument;
        _document = document;
        _item = item;
        _properties = properties;
        _processors = processors;
        _resolve = resolve;
        _definition = definition;
        _item.PropertyChanged += OnModelPropertyChanged;
        Rebuild();
    }

    /// <summary>
    /// Creates the details of <paramref name="item"/>, or returns null when the item has no processors or properties.
    /// </summary>
    public static ItemDetailsViewModel? Create(SchemaWorkspaceDocument document, object? item)
    {
        ArgumentNullException.ThrowIfNull(document);
        var schema = document.Schema;
        return item switch
        {
            EditNode node => new(document, node, node.Properties, EffectiveNodeResolver.GetProcessors(node),
                () => EffectiveNodeResolver.Resolve(schema, node), isDocument: node is EditDocument),
            EditDataTypeDef definition => new(document, definition, definition.Properties, definition.Processors,
                () => EffectiveNodeResolver.ResolveOwn(schema, definition.Properties, definition.Processors)),
            EditProcessorDef definition => new(document, definition, definition.Properties, null,
                () => EffectiveNodeResolver.ResolveOwn(schema, definition.Properties, null), definition),
            EditProcessorRef processor => new(document, processor, processor.Properties, null,
                () => EffectiveNodeResolver.ResolveProcessorRef(schema, processor)),
            _ => null
        };
    }

    /// <summary>
    /// True when the item has processors (nodes and data type defs).
    /// </summary>
    public bool HasProcessors => _processors is not null;

    /// <summary>
    /// False for a processor def: it references exactly one processor and its properties are part of that processor
    /// (<see cref="Definition"/>); both are edited in the General tab.
    /// </summary>
    public bool HasPropertiesTab => _definition is null;

    /// <summary>
    /// True when the Processors tab is shown. The processors of the document are edited in its General tab.
    /// </summary>
    public bool ShowProcessorsTab => HasProcessors && !_isDocument;

    /// <summary>
    /// True when the Properties tab is shown. The properties of the document are edited in its General tab.
    /// </summary>
    public bool ShowPropertiesTab => HasPropertiesTab && !_isDocument;

    /// <summary>
    /// The one processor of a processor def with its properties; null for other items.
    /// </summary>
    [ObservableProperty]
    public partial ProcessorDefRow? Definition { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<PropertyRow> Properties { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ProcessorRow> Processors { get; private set; } = [];

    /// <summary>
    /// The definitions that could not be resolved within the document, so the effective values may be incomplete.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnresolved))]
    public partial IReadOnlyList<string> Unresolved { get; private set; } = [];

    public bool HasUnresolved => Unresolved.Count > 0;

    [RelayCommand]
    private void AddProperty()
        => AddPropertyTo(_properties);

    [RelayCommand]
    private void AddProcessor()
    {
        if (_processors is null)
        {
            return;
        }
        _processors.Add(new EditProcessorRef { Processor = new SchemaName("namespace.id") });
        _document.MarkChanged();
    }

    [RelayCommand]
    private void AddProcessorProperty(ProcessorRow? row)
    {
        if (row is { IsOwn: true })
        {
            AddPropertyTo(row.Ref.Properties);
        }
    }

    [RelayCommand]
    private void RemoveProperty(PropertyRow? row)
    {
        if (row is { Own: { } own, Owner: { } owner } && owner.Remove(own))
        {
            _document.MarkChanged();
        }
    }

    /// <summary>
    /// Adds an own property with the name and value of an inherited one, so it can be changed.
    /// </summary>
    [RelayCommand]
    private void OverrideProperty(PropertyRow? row)
    {
        if (row is { IsOwn: false, Owner: { } owner })
        {
            owner.Add(new EditProperty { Name = row.Name, Value = row.Value });
            _document.MarkChanged();
        }
    }

    [RelayCommand]
    private void RemoveProcessor(ProcessorRow? row)
    {
        if (row is { IsOwn: true, Owner: { } owner } && owner.Remove(row.Ref))
        {
            _document.MarkChanged();
        }
    }

    public void Dispose()
    {
        _item.PropertyChanged -= OnModelPropertyChanged;
        Unobserve();
    }

    private void AddPropertyTo(IList<EditProperty> target)
    {
        var name = "property";
        for (var suffix = 2; target.Any(property => property.Name == name); suffix++)
        {
            name = $"property{suffix}";
        }
        target.Add(new EditProperty { Name = name });
        _document.MarkChanged();
    }

    private void Rebuild()
    {
        var effective = _resolve();
        Properties = effective.Properties.Select(property => new PropertyRow(property, _properties, OnRowEdited, this)).ToList();
        Processors = effective.Processors.Select(processor => new ProcessorRow(processor, _processors, OnRowEdited, this)).ToList();
        Definition = _definition is null ? null : new ProcessorDefRow(_definition, Properties, _properties, OnRowEdited, this);
        Unresolved = effective.Unresolved;
        Observe();
    }

    private void OnRowEdited() => _document.MarkChanged();

    /// <summary>
    /// Listens to the item's own entries: any change other than a property value changes the merge result.
    /// </summary>
    private void Observe()
    {
        Unobserve();
        var processors = _processors ?? [];
        ObserveCollection(_properties);
        ObserveCollection(_processors);
        foreach (var processor in processors)
        {
            ObserveItem(processor);
            ObserveCollection(processor.Properties);
        }
        foreach (var property in _properties.Concat(processors.SelectMany(processor => processor.Properties)))
        {
            ObserveItem(property);
        }
    }

    private void ObserveCollection(object? collection)
    {
        if (collection is INotifyCollectionChanged observable)
        {
            observable.CollectionChanged += OnModelCollectionChanged;
            _observedCollections.Add(observable);
        }
    }

    private void ObserveItem(INotifyPropertyChanged item)
    {
        item.PropertyChanged += OnModelPropertyChanged;
        _observedItems.Add(item);
    }

    private void Unobserve()
    {
        foreach (var collection in _observedCollections)
        {
            collection.CollectionChanged -= OnModelCollectionChanged;
        }
        foreach (var item in _observedItems)
        {
            item.PropertyChanged -= OnModelPropertyChanged;
        }
        _observedCollections.Clear();
        _observedItems.Clear();
    }

    private void OnModelCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // a value edit does not change which entries are effective; the row already shows it
        if (sender is EditProperty && e.PropertyName == nameof(EditProperty.Value))
        {
            return;
        }
        Rebuild();
    }
}

/// <summary>
/// One effective property in the node editor. An own property edits the authored <see cref="EditProperty"/>;
/// an inherited one is read-only and shows where it comes from.
/// </summary>
internal sealed class PropertyRow : ObservableObject
{
    private readonly ResolvedProperty _resolved;
    private readonly Action _edited;

    /// <param name="resolved">The effective property.</param>
    /// <param name="owner">The collection own properties live in (and overrides are added to); null when none can be added.</param>
    /// <param name="edited">Called after an own property was edited.</param>
    /// <param name="details">The details this row belongs to; its commands remove and override properties.</param>
    public PropertyRow(ResolvedProperty resolved, IList<EditProperty>? owner, Action edited, ItemDetailsViewModel details)
    {
        _resolved = resolved;
        _edited = edited;
        Owner = owner;
        Details = details;
    }

    public ItemDetailsViewModel Details { get; }

    public EditProperty? Own => _resolved.Own;
    public IList<EditProperty>? Owner { get; }
    public bool IsOwn => Own is not null;
    public bool IsInherited => !IsOwn;
    public bool CanOverride => IsInherited && Owner is not null;
    public string Source => _resolved.Source;

    public string Name
    {
        get => Own?.Name ?? _resolved.Name;
        set
        {
            if (Own is null || Own.Name == value || String.IsNullOrWhiteSpace(value))
            {
                return;
            }
            Own.Name = value.Trim();
            OnPropertyChanged();
            _edited();
        }
    }

    public string? Value
    {
        get => IsOwn ? Own!.Value : _resolved.Value;
        set
        {
            if (Own is null || Own.Value == value)
            {
                return;
            }
            Own.Value = value;
            OnPropertyChanged();
            _edited();
        }
    }
}

/// <summary>
/// The processor of a processor def in the node editor with the def's properties, which are part of the processor.
/// </summary>
internal sealed class ProcessorDefRow(EditProcessorDef definition, IReadOnlyList<PropertyRow> properties,
    IList<EditProperty> owner, Action edited, ItemDetailsViewModel details) : ObservableObject
{
    public ItemDetailsViewModel Details { get; } = details;

    public IReadOnlyList<PropertyRow> Properties { get; } = properties;
    public IList<EditProperty> Owner { get; } = owner;

    /// <summary>
    /// The processor key ('namespace.id'). Empty input is ignored.
    /// </summary>
    public string Processor
    {
        get => definition.Processor;
        set
        {
            if (String.IsNullOrWhiteSpace(value) || definition.Processor == value.Trim())
            {
                return;
            }
            definition.Processor = value.Trim();
            OnPropertyChanged();
            edited();
        }
    }
}

/// <summary>
/// One effective processor in the node editor with its effective properties.
/// An own processor edits the authored <see cref="EditProcessorRef"/>; an inherited one is read-only.
/// </summary>
internal sealed class ProcessorRow : ObservableObject
{
    private readonly ResolvedProcessor _resolved;
    private readonly Action _edited;

    /// <param name="resolved">The effective processor.</param>
    /// <param name="owner">The collection own processors live in.</param>
    /// <param name="edited">Called after an own processor (or one of its properties) was edited.</param>
    /// <param name="details">The details this row belongs to; its commands remove processors and add their properties.</param>
    public ProcessorRow(ResolvedProcessor resolved, IList<EditProcessorRef>? owner, Action edited, ItemDetailsViewModel details)
    {
        _resolved = resolved;
        _edited = edited;
        Owner = owner;
        Details = details;
        Properties = resolved.Properties.Select(property => new PropertyRow(property, IsOwn ? Ref.Properties : null, edited, details)).ToList();
    }

    public ItemDetailsViewModel Details { get; }

    public EditProcessorRef Ref => _resolved.Processor;
    public IList<EditProcessorRef>? Owner { get; }
    public bool IsOwn => _resolved.IsOwn;
    public bool IsInherited => !IsOwn;
    public string Source => _resolved.Source;
    public IReadOnlyList<PropertyRow> Properties { get; }

    /// <summary>
    /// The processor key ('namespace.id') or definition reference ('ref:name'). Invalid input is ignored.
    /// </summary>
    public string Processor
    {
        get => Ref.Processor.ToString();
        set
        {
            if (!IsOwn || String.IsNullOrWhiteSpace(value) || Ref.Processor.Equals(value.Trim()))
            {
                return;
            }
            Ref.Processor = new SchemaName(value.Trim());
            OnPropertyChanged();
            _edited();
        }
    }

    public string? PublishNamespace
    {
        get => Ref.PublishNamespace;
        set
        {
            var publishNamespace = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (!IsOwn || Ref.PublishNamespace == publishNamespace)
            {
                return;
            }
            Ref.PublishNamespace = publishNamespace;
            OnPropertyChanged();
            _edited();
        }
    }
}
