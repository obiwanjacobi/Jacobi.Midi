using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.SchemaEditor.Assemblies;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.NodeEditor;

/// <summary>
/// View model of one document collection tab (includes, data type defs or processor defs) of the node editor:
/// lists the items of the collection, adds and removes items, and edits the selected item with an <see cref="ItemEditor"/>.
/// Dispose it to release the editor of the selected item.
/// </summary>
internal sealed partial class CollectionTabViewModel : ObservableObject, IDisposable
{
    private readonly SchemaWorkspaceDocument _document;
    private readonly Func<object> _add;
    private readonly Action<string> _report;
    private readonly ProcessorCatalog? _catalog;

    /// <param name="document">The document the collection belongs to.</param>
    /// <param name="items">The (observable) collection of the document.</param>
    /// <param name="what">What an item is called in messages, e.g. "include".</param>
    /// <param name="add">Adds a new item to the document and returns it.</param>
    /// <param name="report">Called with a status message after an add or remove, or when it failed.</param>
    /// <param name="catalog">The processors to check the processor properties of the items against; null to not check them.</param>
    public CollectionTabViewModel(SchemaWorkspaceDocument document, IList items, string what, Func<object> add, Action<string> report,
        ProcessorCatalog? catalog = null)
    {
        _document = document;
        Items = items;
        What = what;
        _add = add;
        _report = report;
        _catalog = catalog;
    }

    public IList Items { get; }

    /// <summary>
    /// What an item is called in messages, e.g. "include".
    /// </summary>
    public string What { get; }

    /// <summary>
    /// The item selected in the list; null when none is selected.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial object? SelectedEntry { get; set; }

    /// <summary>
    /// The editor of the <see cref="SelectedEntry"/>; null when none is selected.
    /// </summary>
    [ObservableProperty]
    public partial ItemEditor? Editor { get; private set; }

    partial void OnSelectedEntryChanged(object? value)
    {
        Editor?.Dispose();
        Editor = ItemEditor.Create(_document, value, _catalog);
    }

    /// <summary>
    /// Adds a new item and selects it.
    /// </summary>
    [RelayCommand]
    private void Add()
    {
        try
        {
            SelectedEntry = _add();
            _report($"Added a {What} to {_document.Schema.Name}.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            _report($"The {What} was not added: {exception.Message}");
        }
    }

    /// <summary>
    /// Removes the selected item.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        if (SelectedEntry is { } entry)
        {
            SelectedEntry = null;
            _document.RemoveItem(entry);
            _report($"Deleted the {What}.");
        }
    }

    private bool CanRemove() => SelectedEntry is not null;

    public void Dispose() => Editor?.Dispose();
}

/// <summary>
/// The collection tabs of the node editor that are shown when the document root is selected.
/// The node defs tab is not part of it: it selects a node def so the regular node editor edits it.
/// </summary>
internal sealed class DocumentTabsViewModel(CollectionTabViewModel includes, CollectionTabViewModel dataTypeDefs,
    CollectionTabViewModel processorDefs) : IDisposable
{
    public CollectionTabViewModel Includes { get; } = includes;
    public CollectionTabViewModel DataTypeDefs { get; } = dataTypeDefs;
    public CollectionTabViewModel ProcessorDefs { get; } = processorDefs;

    public void Dispose()
    {
        Includes.Dispose();
        DataTypeDefs.Dispose();
        ProcessorDefs.Dispose();
    }
}
