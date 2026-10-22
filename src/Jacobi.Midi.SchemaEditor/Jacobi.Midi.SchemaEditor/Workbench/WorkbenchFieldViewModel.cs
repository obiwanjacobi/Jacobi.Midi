using Jacobi.BinarySerializer.Processor;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.SchemaEditor.Instantiate;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.Services;

namespace Jacobi.Midi.SchemaEditor.Workbench;

/// <summary>
/// The access of the workbench to the schema documents of the editor, provided by the host view model.
/// </summary>
internal sealed class WorkbenchSchemaHost
{
    /// <summary>The open, editable schema document with the name; null when it is not open.</summary>
    public required Func<string, SchemaWorkspaceDocument?> FindDocument { get; init; }

    /// <summary>Creates an empty schema document with the name and opens it in the editor.</summary>
    public required Func<string, SchemaWorkspaceDocument> CreateDocument { get; init; }

    /// <summary>
    /// Compiles a document with the documents it includes (the open documents and the embedded schemas);
    /// another open document with the same name is left out. Throws <see cref="InvalidOperationException"/> when it does not compile.
    /// </summary>
    public required Func<EditDocument, SchemaInstantiator> Compile { get; init; }

    /// <summary>The processors the schemas use.</summary>
    public required IProcessorProvider Processors { get; init; }
}

/// <summary>The value of the field in one capture, as shown in the field panel.</summary>
/// <param name="Column">The capture: its change note or label.</param>
/// <param name="Value">The logical value; '—' when it was not read.</param>
/// <param name="Problem">Why the message of the capture could not be read (completely); empty when it was.</param>
internal sealed record FieldValue(string Column, string Value, string Problem)
{
    public bool HasProblem => Problem.Length > 0;
}

/// <summary>
/// The field panel of the workbench: defines a field for the selected bytes of an unknown span, or changes or removes the selected field.
/// While the name, data type or processor is edited, the field is tried on a copy of the target schema and every capture is read
/// with it, so the logical value of the field is shown in each capture before it is defined (or changed).
/// The target schema is the open schema document named after the session; it is created by the first field that is defined.
/// </summary>
internal sealed partial class WorkbenchFieldViewModel : ObservableObject
{
    // the name of the field in the preview while defining (the name the user typed may not be valid yet)
    private const string PreviewName = "WorkbenchPreview";

    private readonly WorkbenchSchemaHost _host;
    private readonly Action _schemaChanged;
    private readonly Action<string> _setStatus;
    private FieldContext? _context;
    private bool _updating;

    /// <param name="host">The access to the schema documents.</param>
    /// <param name="schemaChanged">Called after the target schema was changed (a field defined, changed or removed).</param>
    /// <param name="setStatus">Shows the outcome of an action.</param>
    public WorkbenchFieldViewModel(WorkbenchSchemaHost host, Action schemaChanged, Action<string> setStatus)
    {
        _host = host;
        _schemaChanged = schemaChanged;
        _setStatus = setStatus;
    }

    /// <summary>What the panel shows for the selection.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefining), nameof(IsEditing), nameof(HasField), nameof(Heading), nameof(CopyText))]
    [NotifyCanExecuteChangedFor(nameof(DefineCommand), nameof(ApplyCommand), nameof(RemoveCommand), nameof(CopyToOtherSlotsCommand))]
    public partial FieldMode Mode { get; private set; }

    public bool IsDefining => Mode == FieldMode.Define;
    public bool IsEditing => Mode == FieldMode.Edit;
    public bool HasField => Mode != FieldMode.None;

    public string Heading => Mode switch
    {
        FieldMode.Define => "Define a field",
        FieldMode.Edit => $"Field {_context?.Field?.Name}",
        _ => "Field"
    };

    /// <summary>Why no field can be defined for the selection, or what to do; empty when there is nothing to say.</summary>
    [ObservableProperty]
    public partial string Hint { get; private set; } = "Select bytes to define a field.";

    [ObservableProperty]
    public partial string Name { get; set; } = String.Empty;

    [ObservableProperty]
    public partial string DataType { get; set; } = "sys.uint8";

    /// <summary>An optional processor, e.g. 'ref:Midi.sysexbyte' (7-bit bytes) or 'ref:Midi.string' (ASCII).</summary>
    [ObservableProperty]
    public partial string Processor { get; set; } = String.Empty;

    /// <summary>The data types to choose from (including the data type defs of the target schema).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> DataTypeOptions { get; private set; } = Models.DataTypeOptions.Get(null);

    /// <summary>The processors that are often needed for SysEx data.</summary>
    public IReadOnlyList<string> ProcessorOptions { get; } = ["", "ref:Midi.sysexbyte", "ref:Midi.string"];

    /// <summary>The value of the field in each compared capture (with the name, data type and processor as entered).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<FieldValue> Values { get; private set; } = [];

    /// <summary>A problem with the field or the schema (e.g. it does not compile); empty when there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; private set; } = String.Empty;

    public bool HasError => Error.Length > 0;

    partial void OnNameChanged(string value)
    {
        Preview();
        // the name must be free in the other slots
        UpdateCopyTargets();
    }
    partial void OnDataTypeChanged(string value) => Preview();
    partial void OnProcessorChanged(string value) => Preview();

    /// <summary>
    /// Updates the panel for the current selection. Called by the workbench when the selection, the captures or the schema change.
    /// </summary>
    /// <param name="title">The session title: the name of the target schema.</param>
    /// <param name="selection">The selected bytes; null when nothing is selected.</param>
    /// <param name="messages">The messages of the baseline, one per slot.</param>
    /// <param name="layouts">The layout of the root of each slot; null for a slot without a root yet.</param>
    /// <param name="captures">The compared captures (baseline first) with their column names.</param>
    public void Update(string title, ByteRange? selection, IReadOnlyList<byte[]> messages, IReadOnlyList<MessageLayout?> layouts,
        IReadOnlyList<(Capture Capture, string Column)> captures)
    {
        var previous = _context;
        var layout = selection is { } selected ? layouts.ElementAtOrDefault(selected.Slot) : null;
        _context = selection is { } range && range.Slot < messages.Count
            ? new FieldContext(title, range, messages[range.Slot], layout, captures,
                layout?.Find(range.Start) is { IsPlaceholder: false } span ? span.Field : null, messages, layouts)
            : null;
        DataTypeOptions = Models.DataTypeOptions.Get(_host.FindDocument(title)?.Schema);

        Mode = GetMode(_context, out var hint);
        Hint = hint;
        OnPropertyChanged(nameof(Heading));

        _updating = true;
        try
        {
            if (_context?.Field is { } field && !ReferenceEquals(field, previous?.Field))
            {
                // a field was selected: edit its values
                Name = field.Name;
                DataType = field.DataType?.ToString() ?? String.Empty;
                Processor = field.Processors.FirstOrDefault()?.Processor.ToString() ?? String.Empty;
                FixedValue = WorkbenchSchema.GetValue(field) ?? String.Empty;
                IsFixed = FixedValue.Length > 0;
            }
            else if (Mode == FieldMode.Define && (previous is null || previous.Field is not null || previous.Selection != _context!.Selection))
            {
                // new bytes to define: suggest a data type
                Name = String.Empty;
                (DataType, Processor) = Suggest(_context!.Baseline.AsSpan(_context.Selection.Start, _context.Selection.Length));
                IsFixed = false;
                FixedValue = String.Empty;
            }
        }
        finally
        {
            _updating = false;
        }
        Preview();
        UpdateCopyTargets();
    }

    // ---- fixed value ----

    /// <summary>True when the field holds a fixed value (e.g. a manufacturer id) instead of a parameter.</summary>
    [ObservableProperty]
    public partial bool IsFixed { get; set; }

    /// <summary>The fixed value (as the logical value, e.g. '65' for 41H); filled with the value of the baseline when checked.</summary>
    [ObservableProperty]
    public partial string FixedValue { get; set; } = String.Empty;

    partial void OnIsFixedChanged(bool value)
    {
        if (value && !_updating && FixedValue.Length == 0
            && Values.FirstOrDefault() is { Value: var baselineValue } && baselineValue != ByteInterpretations.NoValue)
        {
            _updating = true;
            try
            {
                FixedValue = baselineValue;
            }
            finally
            {
                _updating = false;
            }
        }
        Preview();
        UpdateCopyTargets();
    }

    partial void OnFixedValueChanged(string value) => Preview();

    private string? GetFixedValue() => IsFixed && !String.IsNullOrWhiteSpace(FixedValue) ? FixedValue : null;

    // ---- the same field in other slots ----

    /// <summary>
    /// True to copy the field only to the slots that have the same bytes at its position (e.g. a manufacturer id);
    /// false to copy it to every slot with an unknown span there (e.g. an address that differs per message).
    /// </summary>
    [ObservableProperty]
    public partial bool CopyRequiresSameBytes { get; set; } = true;

    /// <summary>
    /// The other slots the field can be defined in too (at the same position, inside an unknown span):
    /// the selected field, or while defining the field for the selected bytes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CopyText))]
    [NotifyCanExecuteChangedFor(nameof(CopyToOtherSlotsCommand))]
    public partial IReadOnlyList<int> CopyTargets { get; private set; } = [];

    public string CopyText => (CopyTargets.Count, Mode) switch
    {
        (0, _) => "No other slot has unknown bytes here.",
        (1, FieldMode.Define) => "Define here and in 1 other slot",
        (var count, FieldMode.Define) => $"Define here and in {count} other slots",
        (1, _) => "Define it in 1 other slot",
        (var count, _) => $"Define it in {count} other slots"
    };

    partial void OnCopyRequiresSameBytesChanged(bool value) => UpdateCopyTargets();

    // the bytes of the field in its slot: the selected field, or the selection while defining
    private static ByteRange? GetCopyRange(FieldContext context, FieldMode mode) => mode switch
    {
        FieldMode.Define => context.Selection,
        FieldMode.Edit => context.Layout?.Spans.FirstOrDefault(span => ReferenceEquals(span.Field, context.Field))?.ToRange(context.Selection.Slot),
        _ => null
    };

    private void UpdateCopyTargets()
    {
        if (_context is not { } context || GetCopyRange(context, Mode) is not { } fieldRange)
        {
            CopyTargets = [];
            return;
        }
        var name = context.Field?.Name ?? Name.Trim();
        var bytes = context.Baseline.AsSpan(fieldRange.Start, fieldRange.Length).ToArray();
        CopyTargets = Enumerable.Range(0, context.Messages.Count)
            .Where(slot => slot != fieldRange.Slot)
            .Where(slot =>
            {
                var message = context.Messages[slot];
                var range = fieldRange with { Slot = slot };
                return range.End < message.Length
                    && (!CopyRequiresSameBytes || message.AsSpan(range.Start, range.Length).SequenceEqual(bytes))
                    && WorkbenchSchema.CanDefine(context.Layouts.ElementAtOrDefault(slot), message, range, name);
            })
            .ToList();
    }

    private bool CanCopy() => Mode != FieldMode.None && CopyTargets.Count > 0;

    /// <summary>
    /// Defines the field (name, data type, processor, fixed value) at the same position in the other slots;
    /// while defining, the field is defined for the selected bytes first.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void CopyToOtherSlots()
    {
        if (_context is not { } context || GetCopyRange(context, Mode) is not { } fieldRange)
        {
            return;
        }
        var targets = CopyTargets.ToList();
        EditField field;
        SchemaWorkspaceDocument? document;
        if (Mode == FieldMode.Define)
        {
            if (TryDefine(context, out document, out var defined) is not true)
            {
                return;
            }
            field = defined!;
        }
        else
        {
            field = context.Field!;
            document = _host.FindDocument(context.Title);
        }
        if (document is null)
        {
            return;
        }

        var dataType = field.DataType?.ToString() ?? String.Empty;
        var processor = field.Processors.FirstOrDefault()?.Processor.ToString();
        var value = WorkbenchSchema.GetValue(field);
        var copied = 0;
        try
        {
            foreach (var slot in targets)
            {
                var message = context.Messages[slot];
                var root = WorkbenchSchema.FindRoot(document.Schema, slot) ?? WorkbenchSchema.CreateRoot(document, slot, message);
                WorkbenchSchema.DefineField(document, WorkbenchSchema.GetLayout(root, message), fieldRange with { Slot = slot },
                    field.Name, dataType, processor, value);
                copied++;
            }
            _setStatus($"Defined {field.Name} in {copied} other slot(s).");
        }
        catch (InvalidOperationException exception)
        {
            Error = $"Defined {field.Name} in {copied} other slot(s), then: {exception.Message}";
        }
        _schemaChanged();
    }

    private static FieldMode GetMode(FieldContext? context, out string hint)
    {
        if (context is null)
        {
            hint = "Select bytes to define a field.";
            return FieldMode.None;
        }
        if (context.Field is not null)
        {
            hint = context.Selection.Length == context.Layout!.Find(context.Selection.Start)!.Length
                ? String.Empty
                : "The selection starts in this field; the whole field is changed.";
            return FieldMode.Edit;
        }

        var range = context.Selection;
        if (context.Layout is null)
        {
            var framed = WorkbenchSchema.IsFramed(context.Baseline);
            if (framed && (range.Start == 0 || range.End == context.Baseline.Length - 1))
            {
                hint = "F0 and F7 are the SysEx framing; select the bytes between them.";
                return FieldMode.None;
            }
            hint = $"The first field of this slot adds the root {WorkbenchSchema.GetRootName(range.Slot)} to the schema.";
            return FieldMode.Define;
        }
        if (context.Layout.Find(range.Start) is { IsPlaceholder: true } span && range.End <= span.End)
        {
            hint = String.Empty;
            return FieldMode.Define;
        }
        hint = context.Layout.Find(range.Start) is null
            ? context.Layout.Problem ?? "These bytes are not part of the message root (framing)."
            : "The selection covers more than one unknown span: select bytes inside one Wip_ span.";
        return FieldMode.None;
    }

    // a data type (and processor) that fits the bytes
    private static (string DataType, string Processor) Suggest(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && !bytes.ContainsAnyExceptInRange((byte)0x20, (byte)0x7E))
        {
            return ("sys.string", "ref:Midi.string");
        }
        return bytes.Length switch
        {
            1 => ("sys.uint8", String.Empty),
            2 => ("sys.uint16", "ref:Midi.sysexbyte"),
            _ => ("sys.bytes", String.Empty)
        };
    }

    /// <summary>Takes the data type (and processor) of a row of the interpret strip.</summary>
    [RelayCommand]
    private void UseInterpretation(Interpretation? interpretation)
    {
        if (interpretation is null || !HasField || _context is null)
        {
            return;
        }
        var length = _context.Selection.Length;
        (string, string)? choice = interpretation.Name switch
        {
            "uint8" => ("sys.uint8", ""),
            "int8" => ("sys.int8", ""),
            "7-bit, MSB first" when length == 2 => ("sys.uint16", "ref:Midi.sysexbyte"),
            "7-bit, MSB first" => ("sys.uint32", "ref:Midi.sysexbyte"),
            "ascii" => ("sys.string", "ref:Midi.string"),
            "hex" => ("sys.bytes", ""),
            _ => null
        };
        if (choice is null)
        {
            _setStatus($"There is no data type for '{interpretation.Name}' yet; enter it in the field panel.");
            return;
        }
        var (dataType, processor) = choice.Value;
        _updating = true;
        try
        {
            DataType = dataType;
            Processor = processor;
        }
        finally
        {
            _updating = false;
        }
        Preview();
    }

    // reads the captures with the field tried on a copy of the target schema
    private void Preview()
    {
        if (_updating)
        {
            return;
        }
        Error = String.Empty;
        if (_context is not { } context || Mode == FieldMode.None)
        {
            Values = [];
            return;
        }

        try
        {
            var target = _host.FindDocument(context.Title);
            var copy = new SchemaWorkspaceDocument(target is null ? WorkbenchSchema.CreateEmpty(context.Title) : WorkbenchSchema.Copy(target.Schema), SchemaFormat.Xml);
            var root = WorkbenchSchema.FindRoot(copy.Schema, context.Selection.Slot)
                ?? WorkbenchSchema.CreateRoot(copy, context.Selection.Slot, context.Baseline);
            WorkbenchSchema.EnsureInclude(copy, Processor);

            EditField field;
            if (context.Field is { } edited)
            {
                field = WorkbenchSchema.FindField(copy.Schema, WorkbenchSchema.GetPath(edited))
                    ?? throw new InvalidOperationException($"'{edited.Name}' is not found in the schema.");
                WorkbenchSchema.UpdateField(copy, field, field.Name, DataType, Processor, GetFixedValue());
            }
            else
            {
                field = WorkbenchSchema.DefineField(copy, WorkbenchSchema.GetLayout(root, context.Baseline), context.Selection,
                    PreviewName, DataType, Processor, GetFixedValue());
            }

            var path = WorkbenchSchema.GetPath(field);
            var reads = WorkbenchReader.Read(_host.Compile(copy.Schema), root, context.Selection.Slot,
                context.Captures.Select(item => item.Capture).ToList(), _host.Processors);
            // the reader checks a fixed value; its messages name the preview field by the name the user typed
            var shownName = Name.Trim().Length > 0 ? Name.Trim() : "(new field)";
            Values = reads.Select((read, index) => new FieldValue(context.Captures[index].Column,
                read.Values.TryGetValue(path, out var value) && value.Length > 0 ? value : ByteInterpretations.NoValue,
                (read.Error ?? read.Remark ?? String.Empty).Replace(PreviewName, shownName, StringComparison.Ordinal))).ToList();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            Values = [];
            Error = exception.Message;
        }
    }

    private bool CanDefine() => Mode == FieldMode.Define;

    /// <summary>Defines the field in the target schema (created when it does not exist yet).</summary>
    [RelayCommand(CanExecute = nameof(CanDefine))]
    private void Define()
    {
        if (_context is { } context && TryDefine(context, out _, out _))
        {
            _schemaChanged();
        }
    }

    // defines the field for the selected bytes; shows the problem and returns false when it cannot be defined
    private bool TryDefine(FieldContext context, out SchemaWorkspaceDocument? document, out EditField? field)
    {
        document = null;
        field = null;
        try
        {
            document = _host.FindDocument(context.Title) ?? _host.CreateDocument(context.Title);
            var root = WorkbenchSchema.FindRoot(document.Schema, context.Selection.Slot)
                ?? WorkbenchSchema.CreateRoot(document, context.Selection.Slot, context.Baseline);
            var layout = WorkbenchSchema.GetLayout(root, context.Baseline);
            // checked before the include is added, so a rejected field does not change the document
            if (layout.Find(context.Selection.Start) is not { IsPlaceholder: true })
            {
                throw new InvalidOperationException("Select bytes inside one unknown span (a Wip_ placeholder) to define a field.");
            }
            field = WorkbenchSchema.DefineField(document, layout, context.Selection, Name, DataType, Processor, GetFixedValue());
            WorkbenchSchema.EnsureInclude(document, Processor);
            _setStatus($"Defined {WorkbenchSchema.GetPath(field)} ({context.Selection}) in {document.Schema.Name}.");
            return true;
        }
        catch (InvalidOperationException exception)
        {
            Error = exception.Message;
            return false;
        }
    }

    private bool CanEdit() => Mode == FieldMode.Edit;

    /// <summary>Applies the name, data type and processor to the selected field.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Apply()
    {
        if (_context?.Field is not { } field || _host.FindDocument(_context.Title) is not { } document)
        {
            return;
        }
        try
        {
            WorkbenchSchema.UpdateField(document, field, Name, DataType, Processor, GetFixedValue());
            WorkbenchSchema.EnsureInclude(document, Processor);
            _setStatus($"Changed {WorkbenchSchema.GetPath(field)}.");
            _schemaChanged();
        }
        catch (InvalidOperationException exception)
        {
            Error = exception.Message;
        }
    }

    /// <summary>Turns the selected field back into an unknown span.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Remove()
    {
        if (_context is not { Field: { } field, Layout: { } layout } || _host.FindDocument(_context.Title) is not { } document)
        {
            return;
        }
        try
        {
            WorkbenchSchema.RemoveField(document, layout, field);
            _setStatus($"Removed {field.Name}: its bytes are unknown again.");
            _schemaChanged();
        }
        catch (InvalidOperationException exception)
        {
            Error = exception.Message;
        }
    }

    private sealed record FieldContext(string Title, ByteRange Selection, byte[] Baseline, MessageLayout? Layout,
        IReadOnlyList<(Capture Capture, string Column)> Captures, EditField? Field,
        IReadOnlyList<byte[]> Messages, IReadOnlyList<MessageLayout?> Layouts);
}

/// <summary>What the field panel of the workbench shows for the selection.</summary>
internal enum FieldMode
{
    /// <summary>No field can be defined for the selection (nothing selected, framing, several spans).</summary>
    None,

    /// <summary>The selection is inside an unknown span: a field can be defined.</summary>
    Define,

    /// <summary>The selection starts in a field: it can be changed or removed.</summary>
    Edit
}
