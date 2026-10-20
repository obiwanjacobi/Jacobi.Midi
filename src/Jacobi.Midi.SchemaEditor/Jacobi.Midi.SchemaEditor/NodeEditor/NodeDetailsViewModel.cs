using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.NodeEditor;

/// <summary>
/// View model of the Processors and Properties tabs of the node editor.
/// Shows the effective processors and properties of one node (see <see cref="EffectiveNodeResolver"/>)
/// and edits the node's own entries in place. Inherited entries are read-only (properties can be overridden).
/// Rebuilds its rows when the structure of the node's own entries changes; dispose it to stop listening.
/// </summary>
internal sealed partial class NodeDetailsViewModel : ObservableObject, IDisposable
{
    private readonly SchemaWorkspaceDocument _document;
    private readonly List<INotifyPropertyChanged> _observedItems = [];
    private readonly List<INotifyCollectionChanged> _observedCollections = [];

    public NodeDetailsViewModel(SchemaWorkspaceDocument document, EditNode node)
    {
        _document = document;
        Node = node;
        Node.PropertyChanged += OnModelPropertyChanged;
        Rebuild();
    }

    public EditNode Node { get; }

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
        => AddPropertyTo(Node.Properties);

    [RelayCommand]
    private void AddProcessor()
        => AddProcessorTo(EffectiveNodeResolver.GetProcessors(Node));

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
        Node.PropertyChanged -= OnModelPropertyChanged;
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

    private void AddProcessorTo(IList<EditProcessorRef>? target)
    {
        if (target is null)
        {
            return;
        }
        target.Add(new EditProcessorRef { Processor = new SchemaName("namespace.id") });
        _document.MarkChanged();
    }

    private void Rebuild()
    {
        var effective = EffectiveNodeResolver.Resolve(_document.Schema, Node);
        Properties = effective.Properties.Select(property => new PropertyRow(property, Node.Properties, OnRowEdited)).ToList();
        Processors = effective.Processors.Select(processor => new ProcessorRow(processor, EffectiveNodeResolver.GetProcessors(Node), OnRowEdited)).ToList();
        Unresolved = effective.Unresolved;
        Observe();
    }

    private void OnRowEdited() => _document.MarkChanged();

    /// <summary>
    /// Listens to the node's own entries: any change other than a property value changes the merge result.
    /// </summary>
    private void Observe()
    {
        Unobserve();
        var processors = EffectiveNodeResolver.GetProcessors(Node) ?? [];
        ObserveCollection(Node.Properties);
        ObserveCollection(EffectiveNodeResolver.GetProcessors(Node));
        foreach (var processor in processors)
        {
            ObserveItem(processor);
            ObserveCollection(processor.Properties);
        }
        foreach (var property in Node.Properties.Concat(processors.SelectMany(processor => processor.Properties)))
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
    public PropertyRow(ResolvedProperty resolved, IList<EditProperty>? owner, Action edited)
    {
        _resolved = resolved;
        _edited = edited;
        Owner = owner;
    }

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
    public ProcessorRow(ResolvedProcessor resolved, IList<EditProcessorRef>? owner, Action edited)
    {
        _resolved = resolved;
        _edited = edited;
        Owner = owner;
        Properties = resolved.Properties.Select(property => new PropertyRow(property, IsOwn ? Ref.Properties : null, edited)).ToList();
    }

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
