using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Assemblies;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.NodeEditor;

/// <summary>
/// Base of the editors for the document items that are not nodes (includes, data type defs, processor defs, processors and properties).
/// Editors change the model directly (live) and mark the document changed.
/// Setters throw on invalid input so the bound text box shows a validation error and the model keeps its value.
/// </summary>
internal abstract class ItemEditor(SchemaWorkspaceDocument document) : ObservableObject, IDisposable
{
    /// <summary>
    /// The processors and properties of the edited item; null when the item has none. Disposed with the editor.
    /// </summary>
    public ItemDetailsViewModel? Details { get; private set; }

    public void Dispose() => Details?.Dispose();

    /// <summary>
    /// Creates the editor for <paramref name="item"/>, or returns null when the item has no item editor (nodes have their own).
    /// </summary>
    /// <param name="catalog">The processors to check the processor properties against; null to not check them.</param>
    public static ItemEditor? Create(SchemaWorkspaceDocument document, object? item, ProcessorCatalog? catalog = null)
    {
        ItemEditor? editor = item switch
        {
            EditDocumentRef include => new DocumentRefEditor(document, include),
            EditDataTypeDef definition => new DataTypeDefEditor(document, definition),
            EditProcessorDef definition => new ProcessorDefEditor(document, definition),
            EditProcessorRef processor => new ProcessorRefEditor(document, processor),
            EditProperty property => new PropertyEditor(document, property),
            _ => null
        };
        editor?.Details = ItemDetailsViewModel.Create(document, item, catalog);
        return editor;
    }

    /// <summary>
    /// Assigns <paramref name="value"/> through <paramref name="assign"/> when it differs from <paramref name="current"/>
    /// and marks the document changed.
    /// </summary>
    protected void Edit<T>(T current, T value, Action<T> assign, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return;
        }
        assign(value);
        OnPropertyChanged(propertyName);
        document.MarkChanged();
    }

    protected static string Required(string? value, string what)
        => String.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{what} is required.") : value.Trim();

    protected static string? Optional(string? value)
        => String.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected static decimal? ParseDecimal(string? value)
        => String.IsNullOrWhiteSpace(value) ? null
            : Decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number
            : throw new FormatException($"'{value}' is not a number.");

    protected static string? Format(decimal? value)
        => value?.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Edits an include: the name of the included schema and its (optional) path.
/// </summary>
internal sealed class DocumentRefEditor(SchemaWorkspaceDocument document, EditDocumentRef include) : ItemEditor(document)
{
    public string Schema
    {
        get => include.Schema;
        set => Edit(include.Schema, Required(value, "The schema name"), v => include.Schema = v);
    }

    public string? Path
    {
        get => include.Path;
        set => Edit(include.Path, Optional(value), v => include.Path = v);
    }
}

/// <summary>
/// Edits a data type def: its name, base type, numeric facets and the value/label options.
/// Its processors and properties are edited in the Processors and Properties tabs.
/// </summary>
internal sealed partial class DataTypeDefEditor : ItemEditor
{
    private readonly SchemaWorkspaceDocument _document;
    private readonly EditDataTypeDef _definition;

    public DataTypeDefEditor(SchemaWorkspaceDocument document, EditDataTypeDef definition)
        : base(document)
    {
        _document = document;
        _definition = definition;
        Options = new(definition.Options?.Select(option => new OptionRow(this, option.Key, option.Value)) ?? []);
    }

    public string Name
    {
        get => _definition.Name;
        set => Edit(_definition.Name, Required(value, "The name"), v => _definition.Name = v);
    }

    public string BasedOn
    {
        get => _definition.BasedOn.ToString();
        set => Edit(_definition.BasedOn, new SchemaName(Required(value, "The base type")), v => _definition.BasedOn = v);
    }

    /// <summary>
    /// The data types to base the def on: the registered types and the other data type defs of the document.
    /// Taken from the document each time the editor is created (when a def is selected or added).
    /// </summary>
    public IReadOnlyList<string> BasedOnOptions => DataTypeOptions.Get(_document.Schema, _definition);

    public string? Min
    {
        get => Format(_definition.Min);
        set => Edit(_definition.Min, ParseDecimal(value), v => _definition.Min = v);
    }

    public string? Max
    {
        get => Format(_definition.Max);
        set => Edit(_definition.Max, ParseDecimal(value), v => _definition.Max = v);
    }

    public string? Scale
    {
        get => Format(_definition.Scale);
        set => Edit(_definition.Scale, ParseDecimal(value), v => _definition.Scale = v);
    }

    public string? Shift
    {
        get => Format(_definition.Shift);
        set => Edit(_definition.Shift, ParseDecimal(value), v => _definition.Shift = v);
    }

    /// <summary>
    /// The options as editable rows. Every edit writes the complete options map back to the def.
    /// </summary>
    public ObservableCollection<OptionRow> Options { get; }

    [RelayCommand]
    private void AddOption()
    {
        var value = Options.Count == 0 ? 0 : Options.Max(option => option.Number) + 1;
        Options.Add(new OptionRow(this, value, $"Option{value}"));
        WriteOptions();
    }

    [RelayCommand]
    private void RemoveOption(OptionRow? row)
    {
        if (row is not null && Options.Remove(row))
        {
            WriteOptions();
        }
    }

    /// <summary>
    /// Writes the rows to the def. A value used by more than one row keeps the label of the last row.
    /// </summary>
    internal void WriteOptions()
    {
        _definition.Options = Options.Count == 0 ? null
            : Options.GroupBy(option => option.Number).ToDictionary(group => group.Key, group => group.Last().Label);
        _document.MarkChanged();
    }
}

/// <summary>
/// One value/label option of a <see cref="DataTypeDefEditor"/>.
/// The value accepts decimal or hexadecimal ('0x..') numbers.
/// </summary>
internal sealed class OptionRow(DataTypeDefEditor owner, long number, string label) : ObservableObject
{
    public long Number { get; private set; } = number;

    public string Value
    {
        get => Number.ToString(CultureInfo.InvariantCulture);
        set
        {
            var number = ParseNumber(value);
            if (number == Number)
            {
                return;
            }
            Number = number;
            OnPropertyChanged();
            owner.WriteOptions();
        }
    }

    public string Label
    {
        get => label;
        set
        {
            var text = value ?? String.Empty;
            if (text == label)
            {
                return;
            }
            label = text;
            OnPropertyChanged();
            owner.WriteOptions();
        }
    }

    private static long ParseNumber(string? value)
    {
        var text = value?.Trim() ?? String.Empty;
        var parsed = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Int64.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number)
            : Int64.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
        return parsed ? number : throw new FormatException($"'{value}' is not a whole number.");
    }
}

/// <summary>
/// Edits a processor def: the name it is referenced by.
/// Its processor and properties are edited in the Processors tab.
/// </summary>
internal sealed class ProcessorDefEditor(SchemaWorkspaceDocument document, EditProcessorDef definition) : ItemEditor(document)
{
    public string Name
    {
        get => definition.Name;
        set => Edit(definition.Name, Required(value, "The name"), v => definition.Name = v);
    }
}

/// <summary>
/// Edits a processor of the document: the processor key or def reference and its publish namespace.
/// Its properties are edited in the Properties tab.
/// </summary>
internal sealed class ProcessorRefEditor(SchemaWorkspaceDocument document, EditProcessorRef processor) : ItemEditor(document)
{
    public string Processor
    {
        get => processor.Processor.ToString();
        set => Edit(processor.Processor, new SchemaName(Required(value, "The processor")), v => processor.Processor = v);
    }

    public string? PublishNamespace
    {
        get => processor.PublishNamespace;
        set => Edit(processor.PublishNamespace, Optional(value), v => processor.PublishNamespace = v);
    }
}

/// <summary>
/// Edits a property of the document.
/// </summary>
internal sealed class PropertyEditor(SchemaWorkspaceDocument document, EditProperty property) : ItemEditor(document)
{
    public string Name
    {
        get => property.Name;
        set => Edit(property.Name, Required(value, "The name"), v => property.Name = v);
    }

    public string? Value
    {
        get => property.Value;
        set => Edit(property.Value, value, v => property.Value = v);
    }
}
