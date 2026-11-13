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
/// Invalid input (e.g. an emptied required field) is not assigned: the model keeps its value and the problem is shown
/// as a warning of the field (<see cref="Warnings"/>).
/// </summary>
internal abstract class ItemEditor(SchemaWorkspaceDocument document) : ObservableObject, IDisposable
{
    private readonly Dictionary<string, string> _warnings = [];

    /// <summary>
    /// The processors and properties of the edited item; null when the item has none. Disposed with the editor.
    /// </summary>
    public ItemDetailsViewModel? Details { get; private set; }

    /// <summary>
    /// Why the last input of a field was not assigned, by property name (e.g. <c>Warnings[Schema]</c>); null when it was.
    /// A new instance after every change, so bindings to it are refreshed.
    /// </summary>
    public ItemEditorWarnings Warnings => new(this);

    internal string? GetWarning(string propertyName) => _warnings.GetValueOrDefault(propertyName);

    public void Dispose() => Details?.Dispose();

    /// <summary>
    /// Creates the editor for <paramref name="item"/>, or returns null when the item has no item editor (nodes have their own).
    /// </summary>
    /// <param name="catalog">The processors to check the processor properties against; null to not check them.</param>
    /// <param name="schemas">The loaded schemas the definitions of other schemas are resolved in; null to resolve within the document only.</param>
    public static ItemEditor? Create(SchemaWorkspaceDocument document, object? item, ProcessorCatalog? catalog = null, SchemaSetWorkspace? schemas = null)
    {
        ItemEditor? editor = item switch
        {
            EditDocumentRef include => new DocumentRefEditor(document, include, schemas),
            EditDataTypeDef definition => new DataTypeDefEditor(document, definition, schemas),
            EditProcessorDef definition => new ProcessorDefEditor(document, definition),
            EditProcessorRef processor => new ProcessorRefEditor(document, processor),
            EditProperty property => new PropertyEditor(document, property),
            _ => null
        };
        editor?.Details = ItemDetailsViewModel.Create(document, item, catalog, schemas);
        return editor;
    }

    /// <summary>
    /// Assigns the value made by <paramref name="parse"/> through <paramref name="assign"/> when it differs from <paramref name="current"/>
    /// and marks the document changed. When <paramref name="parse"/> rejects the input (<see cref="ArgumentException"/> or
    /// <see cref="FormatException"/>) nothing is assigned and its message becomes the warning of the property.
    /// </summary>
    /// <returns>True when the input was accepted (assigned or unchanged).</returns>
    protected bool Edit<T>(T current, Func<T> parse, Action<T> assign, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
    {
        T value;
        try
        {
            value = parse();
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            SetWarning(propertyName, exception.Message);
            return false;
        }
        SetWarning(propertyName, null);
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return true;
        }
        assign(value);
        OnPropertyChanged(propertyName);
        document.MarkChanged();
        return true;
    }

    private void SetWarning(string propertyName, string? warning)
    {
        if (warning is null ? !_warnings.Remove(propertyName) : _warnings.GetValueOrDefault(propertyName) == warning)
        {
            return;
        }
        if (warning is not null)
        {
            _warnings[propertyName] = warning;
        }
        OnPropertyChanged(nameof(Warnings));
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
/// The warnings of the fields of an <see cref="ItemEditor"/>, by property name, for bindings such as <c>Warnings[Schema]</c>.
/// Each is looked up when it is read.
/// </summary>
internal sealed class ItemEditorWarnings(ItemEditor editor)
{
    public string? this[string propertyName] => editor.GetWarning(propertyName);
}

/// <summary>
/// Edits an include: the name of the included schema and its (optional) path.
/// </summary>
/// <param name="schemas">The loaded schemas offered as <see cref="SchemaOptions"/>; null to offer none.</param>
internal sealed class DocumentRefEditor(SchemaWorkspaceDocument document, EditDocumentRef include, SchemaSetWorkspace? schemas = null)
    : ItemEditor(document)
{
    private readonly EditDocument _schema = document.Schema;

    public string Schema
    {
        get => include.Schema;
        set => Edit(include.Schema, () => Required(value, "The schema name"), v => include.Schema = v);
    }

    /// <summary>
    /// The schemas to include: the loaded schemas (open documents and embedded schemas of the opened assemblies)
    /// except the document itself and the schemas it already includes with another include.
    /// Taken from the loaded schemas each time the editor is created (when an include is selected or added).
    /// </summary>
    public IReadOnlyList<string> SchemaOptions => schemas is null ? []
        : [.. schemas.GetIncludeNames(_schema)
            .Where(name => !_schema.Includes.Any(other => !ReferenceEquals(other, include)
                && String.Equals(other.Schema, name, StringComparison.OrdinalIgnoreCase)))];

    public string? Path
    {
        get => include.Path;
        set => Edit(include.Path, () => Optional(value), v => include.Path = v);
    }
}

/// <summary>
/// Edits a data type def: its name, base type, value type, facets and the options (physical value to logical value).
/// Its processors and properties are edited in the Processors and Properties tabs.
/// </summary>
internal sealed partial class DataTypeDefEditor : ItemEditor
{
    private readonly SchemaWorkspaceDocument _document;
    private readonly EditDataTypeDef _definition;
    private readonly SchemaSetWorkspace? _schemas;

    /// <param name="schemas">The loaded schemas: the data type defs of the schemas the document includes are known; null for the document only.</param>
    public DataTypeDefEditor(SchemaWorkspaceDocument document, EditDataTypeDef definition, SchemaSetWorkspace? schemas = null)
        : base(document)
    {
        _document = document;
        _definition = definition;
        _schemas = schemas;
        Options = new(definition.Options?.Select(option => new OptionRow(this, option.Key, option.Value)) ?? []);
    }

    /// <summary>The edited data type def; e.g. to follow its <see cref="BasedOn"/> to the data type def it refers to.</summary>
    public EditDataTypeDef Definition => _definition;

    public string Name
    {
        get => _definition.Name;
        set => Edit(_definition.Name, () => Required(value, "The name"), v => _definition.Name = v);
    }

    public string BasedOn
    {
        get => _definition.BasedOn.ToString();
        set
        {
            Edit(_definition.BasedOn, () => new SchemaName(Required(value, "The base type")), v => _definition.BasedOn = v);
            OnPropertyChanged(nameof(CanGoToBasedOn));
            OnPropertyChanged(nameof(BasedOnTargetProblem));
        }
    }

    /// <summary>
    /// True when the base type can be followed (→) to its data type def; otherwise <see cref="BasedOnTargetProblem"/>
    /// tells why not (e.g. a registered data type such as 'sys.uint8' has no def).
    /// </summary>
    public bool CanGoToBasedOn => BasedOnTargetProblem is null;

    /// <summary>
    /// Why the base type cannot be followed to a data type def (<see cref="SchemaSetWorkspace.FindDataTypeDef"/>); null when it can.
    /// </summary>
    public string? BasedOnTargetProblem
        => _schemas is null ? "The loaded schemas are not known here."
            : _schemas.FindDataTypeDef(_definition, _definition.BasedOn, out var problem) is null ? problem : null;

    /// <summary>
    /// The data types to base the def on: the registered types and the other data type defs of the document and the schemas it includes.
    /// Taken from the document each time the editor is created (when a def is selected or added).
    /// </summary>
    public IReadOnlyList<DataTypeOption> BasedOnOptions => DataTypeOptions.Get(SchemaScope.Of(_document.Schema, _schemas), _definition);

    /// <summary>The data type of the logical value; empty for the implied value type.</summary>
    public string? ValueType
    {
        get => _definition.ValueType?.ToString();
        set => Edit(_definition.ValueType, () => Optional(value) is { } name ? new SchemaName(name) : (SchemaName?)null, v => _definition.ValueType = v);
    }

    /// <summary>A literal of the value type.</summary>
    public string? Min
    {
        get => _definition.Min;
        set => Edit(_definition.Min, () => Optional(value), v => _definition.Min = v);
    }

    /// <summary>A literal of the value type.</summary>
    public string? Max
    {
        get => _definition.Max;
        set => Edit(_definition.Max, () => Optional(value), v => _definition.Max = v);
    }

    public string? Scale
    {
        get => Format(_definition.Scale);
        set => Edit(_definition.Scale, () => ParseDecimal(value), v => _definition.Scale = v);
    }

    public string? Shift
    {
        get => Format(_definition.Shift);
        set => Edit(_definition.Shift, () => ParseDecimal(value), v => _definition.Shift = v);
    }

    /// <summary>
    /// The options as editable rows. Every edit writes the complete options map back to the def.
    /// </summary>
    public ObservableCollection<OptionRow> Options { get; }

    /// <summary>
    /// The logical value (a literal of the value type) read for a physical value that is not one of the options; empty for none.
    /// The spaces are part of the value.
    /// </summary>
    public string? OptionsDefault
    {
        get => _definition.OptionsDefault;
        set => Edit(_definition.OptionsDefault, () => String.IsNullOrEmpty(value) ? null : value, v => _definition.OptionsDefault = v);
    }

    [RelayCommand]
    private void AddOption()
    {
        // the next number after the largest (whole number) physical value
        var numbers = Options.Select(option => OptionRow.TryParseNumber(option.Value, out var number) ? number : (long?)null).OfType<long>().ToList();
        var value = numbers.Count == 0 ? 0 : numbers.Max() + 1;
        Options.Add(new OptionRow(this, value.ToString(CultureInfo.InvariantCulture), $"Option{value}"));
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
    /// A warning when <paramref name="value"/> is not a physical value of the base type (a literal of <see cref="BasedOn"/>); otherwise null.
    /// </summary>
    internal string? ValidatePhysical(string value)
        => DataTypeValueValidator.ValidatePhysical(SchemaScope.Of(_document.Schema, _schemas), _definition.BasedOn.ToString(), value);

    /// <summary>
    /// Writes the rows to the def. A value used by more than one row keeps the label of the last row.
    /// </summary>
    internal void WriteOptions()
    {
        _definition.Options = Options.Count == 0 ? null
            : Options.GroupBy(option => option.Value).ToDictionary(group => group.Key, group => group.Last().Label);
        _document.MarkChanged();
    }
}

/// <summary>
/// One option of a <see cref="DataTypeDefEditor"/>: the physical value (a literal of the data type) and its label,
/// the logical value (a literal of the value type).
/// </summary>
internal sealed class OptionRow(DataTypeDefEditor owner, string physical, string label) : ObservableObject
{
    public string Value
    {
        get => physical;
        set
        {
            var text = value?.Trim() is { Length: > 0 } trimmed ? trimmed : throw new ArgumentException("The value is required.");
            if (text == physical)
            {
                return;
            }
            if (owner.ValidatePhysical(text) is { } warning)
            {
                throw new FormatException(warning);
            }
            physical = text;
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

    /// <summary>Parses a decimal or hexadecimal ('0x..') whole number.</summary>
    internal static bool TryParseNumber(string? value, out long number)
    {
        var text = value?.Trim() ?? String.Empty;
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Int64.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out number)
            : Int64.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
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
        set => Edit(definition.Name, () => Required(value, "The name"), v => definition.Name = v);
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
        set => Edit(processor.Processor, () => new SchemaName(Required(value, "The processor")), v => processor.Processor = v);
    }

    public string? PublishNamespace
    {
        get => processor.PublishNamespace;
        set => Edit(processor.PublishNamespace, () => Optional(value), v => processor.PublishNamespace = v);
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
        set => Edit(property.Name, () => Required(value, "The name"), v => property.Name = v);
    }

    public string? Value
    {
        get => property.Value;
        set => Edit(property.Value, () => value, v => property.Value = v);
    }
}
