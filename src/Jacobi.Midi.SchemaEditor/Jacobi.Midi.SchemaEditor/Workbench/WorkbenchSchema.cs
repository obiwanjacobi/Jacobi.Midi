using System.Globalization;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.Services;

namespace Jacobi.Midi.SchemaEditor.Workbench;

/// <summary>
/// A field of a message root and the bytes of the message it describes (message offsets, including F0).
/// A placeholder field (<see cref="WorkbenchSchema.PlaceholderPrefix"/>) describes an unknown span.
/// </summary>
internal sealed record FieldSpan(EditField Field, int Start, int Length)
{
    public int End => Start + Length - 1;

    /// <summary>True for a placeholder: bytes that are not described yet.</summary>
    public bool IsPlaceholder => WorkbenchSchema.IsPlaceholder(Field);

    public ByteRange ToRange(int slot) => new(slot, Start, Length);
}

/// <summary>
/// The layout of a message root: the bytes of the message that each of its fields describes, as far as their sizes are known.
/// </summary>
/// <param name="Root">The root group that describes the message.</param>
/// <param name="Spans">The fields in message order with their bytes.</param>
/// <param name="Problem">Why the layout stops before the end of the root (a node of unknown size); null when it is complete.</param>
internal sealed record MessageLayout(EditGroup Root, IReadOnlyList<FieldSpan> Spans, string? Problem)
{
    /// <summary>The number of bytes described by fields that are not placeholders.</summary>
    public int DescribedCount => Spans.Where(span => !span.IsPlaceholder).Sum(span => span.Length);

    /// <summary>The span (field or placeholder) that contains the byte; null when none does.</summary>
    public FieldSpan? Find(int offset) => Spans.FirstOrDefault(span => offset >= span.Start && offset <= span.End);
}

/// <summary>
/// Builds the target schema of the workbench: one root group per message slot (<c>Message1</c>, <c>Message2</c>, …)
/// whose bytes are described by fields and, where they are not known yet, by placeholder fields (<c>Wip_&lt;offset&gt;</c>).
/// Every root describes its whole message at all times, so the serializer can read it while the work is in progress.
/// Fields made by the workbench have a fixed byte length; the byte positions of the fields are computed from them.
/// </summary>
internal static class WorkbenchSchema
{
    /// <summary>The name prefix of a placeholder field: work in progress.</summary>
    public const string PlaceholderPrefix = "Wip_";

    private const string SysExProcessor = "midi.sysex";
    private const string PlaceholderDataType = "sys.bytes";

    // the sizes of the fixed size data types (fields without byte length or processors)
    private static readonly Dictionary<string, int> DataTypeSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sys.bool"] = 1,
        ["sys.uint8"] = 1,
        ["sys.int8"] = 1,
        ["sys.uint16"] = 2,
        ["sys.int16"] = 2,
        ["sys.uint32"] = 4,
        ["sys.int32"] = 4,
        ["sys.uint64"] = 8,
        ["sys.int64"] = 8,
    };

    public static bool IsPlaceholder(EditNode node) => node is EditField && node.Name.StartsWith(PlaceholderPrefix, StringComparison.Ordinal);

    /// <summary>The name of the root group of a message slot: <c>Message1</c> for the first slot.</summary>
    public static string GetRootName(int slot) => $"Message{slot + 1}";

    /// <summary>The root group of a message slot; null when the document has none.</summary>
    public static EditGroup? FindRoot(EditDocument document, int slot)
        => document.Members.OfType<EditGroup>().FirstOrDefault(group => group.Kind == SchemaNodeKind.Group
            && String.Equals(group.Name, GetRootName(slot), StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the message starts with F0 and ends with F7.</summary>
    public static bool IsFramed(byte[] message) => message.Length >= 2 && message[0] == 0xF0 && message[^1] == 0xF7;

    /// <summary>
    /// Adds the root group of a message slot to the document: with the SysEx framing processor when the message is framed (F0 … F7),
    /// and one placeholder for all bytes in between.
    /// </summary>
    public static EditGroup CreateRoot(SchemaWorkspaceDocument document, int slot, byte[] message)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(message);
        var root = document.AddGroup(document.Schema, GetRootName(slot));
        var framed = IsFramed(message);
        if (framed)
        {
            root.Processors.Add(new EditProcessorRef { Processor = new SchemaName(SysExProcessor) });
        }
        var start = framed ? 1 : 0;
        var length = message.Length - (framed ? 2 : 0);
        if (length > 0)
        {
            root.Members.Add(CreatePlaceholder(root, start, length));
        }
        document.MarkChanged();
        return root;
    }

    /// <summary>The layout of <paramref name="root"/> on <paramref name="message"/>.</summary>
    public static MessageLayout GetLayout(EditGroup root, byte[] message)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(message);
        // the framing processor reads the F0
        var offset = root.Processors.Any(processor => String.Equals(processor.Processor.ToString(), SysExProcessor, StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
        var spans = new List<FieldSpan>();
        var problem = AddSpans(root, ref offset, spans);
        return new MessageLayout(root, spans, problem);
    }

    private static string? AddSpans(EditGroup group, ref int offset, List<FieldSpan> spans)
    {
        foreach (var member in group.Members)
        {
            switch (member)
            {
                case EditField field when GetSize(field) is { } size:
                    spans.Add(new FieldSpan(field, offset, size));
                    offset += size;
                    break;
                case EditField field:
                    return $"The size of '{field.Name}' is not known (give it a byte length): the bytes after it are not shown.";
                case EditRepeat or EditChoice:
                    return $"'{member.Name}' is a {member.Kind.ToString().ToLowerInvariant()}: the bytes after it are not shown.";
                case EditGroup nested when nested.NodeDef is null && nested.Processors.Count == 0:
                    if (AddSpans(nested, ref offset, spans) is { } problem)
                    {
                        return problem;
                    }
                    break;
                default:
                    return $"The size of '{member.Name}' is not known: the bytes after it are not shown.";
            }
        }
        return null;
    }

    // the number of bytes of a field: its byte length, or the size of its data type when nothing changes it
    private static int? GetSize(EditField field)
    {
        if (field.NodeDef is not null || field.ByteOffset is not null)
        {
            return null;
        }
        if (field.ByteLength is int length)
        {
            return length;
        }
        return field.Processors.Count == 0 && field.DataType is { } dataType && DataTypeSizes.TryGetValue(dataType.ToString(), out var size)
            ? size
            : null;
    }

    /// <summary>
    /// Describes <paramref name="range"/> (message offsets) with a new field: the placeholder that contains the range is split
    /// into the field and placeholders for the bytes before and after it.
    /// </summary>
    /// <param name="processor">An optional processor of the field, e.g. 'ref:Midi.sysexbyte'.</param>
    /// <param name="value">The fixed value of the field (e.g. a manufacturer id); null or empty for a field that holds a parameter.</param>
    /// <exception cref="InvalidOperationException">The range is not inside one placeholder, or the name is not valid.</exception>
    public static EditField DefineField(SchemaWorkspaceDocument document, MessageLayout layout, ByteRange range,
        string name, string dataType, string? processor, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layout);
        name = name.Trim();
        dataType = dataType.Trim();
        if (layout.Find(range.Start) is not { IsPlaceholder: true } span || range.End > span.End)
        {
            throw new InvalidOperationException("Select bytes inside one unknown span (a Wip_ placeholder) to define a field.");
        }
        var parent = span.Field.Parent ?? throw new InvalidOperationException($"'{span.Field.Name}' has no parent group.");
        ValidateName(parent, name, except: null);
        if (dataType.Length == 0)
        {
            throw new InvalidOperationException("Enter the data type of the field.");
        }

        var field = new EditField(parent)
        {
            Name = name,
            DataType = new SchemaName(dataType),
            ByteLength = range.Length,
            Processors = [],
            Properties = [],
        };
        if (!String.IsNullOrWhiteSpace(processor))
        {
            field.Processors.Add(new EditProcessorRef { Processor = new SchemaName(processor.Trim()) });
        }
        SetValue(field, value);

        var replacement = new List<EditNode>();
        if (range.Start > span.Start)
        {
            replacement.Add(CreatePlaceholder(parent, span.Start, range.Start - span.Start));
        }
        replacement.Add(field);
        if (range.End < span.End)
        {
            replacement.Add(CreatePlaceholder(parent, range.End + 1, span.End - range.End));
        }
        Replace(parent, [span.Field], replacement);
        document.MarkChanged();
        return field;
    }

    /// <summary>Changes the name, data type, processor and fixed value of a field that was defined before.</summary>
    /// <param name="value">The fixed value; null or empty for none.</param>
    /// <exception cref="InvalidOperationException">The name is not valid or the data type is empty.</exception>
    public static void UpdateField(SchemaWorkspaceDocument document, EditField field, string name, string dataType, string? processor, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(field);
        name = name.Trim();
        dataType = dataType.Trim();
        ValidateName(field.Parent, name, except: field);
        if (dataType.Length == 0)
        {
            throw new InvalidOperationException("Enter the data type of the field.");
        }
        field.Name = name;
        field.DataType = new SchemaName(dataType);
        field.Processors.Clear();
        if (!String.IsNullOrWhiteSpace(processor))
        {
            field.Processors.Add(new EditProcessorRef { Processor = new SchemaName(processor.Trim()) });
        }
        SetValue(field, value);
        document.MarkChanged();
    }

    private static void SetValue(EditField field, string? value)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            field.Value = default;
        }
        else
        {
            field.Value = value.Trim();
        }
    }

    /// <summary>The fixed value of a field; null when it has none (or a reference).</summary>
    public static string? GetValue(EditField field) => field.Value is string value ? value : null;

    /// <summary>
    /// True when a field named <paramref name="name"/> can be defined for <paramref name="range"/> in the message:
    /// the range is inside one placeholder (or between the framing when the slot has no root yet) and the name is free.
    /// </summary>
    public static bool CanDefine(MessageLayout? layout, byte[] message, ByteRange range, string name)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (layout is null)
        {
            return IsFramed(message) ? range.Start >= 1 && range.End <= message.Length - 2 : range.End < message.Length;
        }
        return layout.Find(range.Start) is { IsPlaceholder: true } span && range.End <= span.End
            && span.Field.Parent?.Members.Any(member => String.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase)) != true;
    }

    /// <summary>
    /// Turns the field back into an unknown span: it is replaced by a placeholder that is merged with the placeholders next to it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The field is not in the layout.</exception>
    public static void RemoveField(SchemaWorkspaceDocument document, MessageLayout layout, EditField field)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layout);
        var index = layout.Spans.ToList().FindIndex(span => ReferenceEquals(span.Field, field));
        if (index < 0 || field.Parent is not { } parent)
        {
            throw new InvalidOperationException($"'{field.Name}' is not part of the message layout.");
        }

        // the neighbours in the same group that are placeholders are merged with it
        var first = index;
        while (first > 0 && layout.Spans[first - 1].IsPlaceholder && ReferenceEquals(layout.Spans[first - 1].Field.Parent, parent))
        {
            first--;
        }
        var last = index;
        while (last < layout.Spans.Count - 1 && layout.Spans[last + 1].IsPlaceholder && ReferenceEquals(layout.Spans[last + 1].Field.Parent, parent))
        {
            last++;
        }
        var merged = layout.Spans.Skip(first).Take(last - first + 1).ToList();
        var start = merged[0].Start;
        Replace(parent, merged.Select(span => (EditNode)span.Field).ToList(), [CreatePlaceholder(parent, start, merged[^1].End - start + 1)]);
        document.MarkChanged();
    }

    private static EditField CreatePlaceholder(EditGroup parent, int start, int length) => new(parent)
    {
        Name = PlaceholderPrefix + start.ToString(CultureInfo.InvariantCulture),
        DataType = new SchemaName(PlaceholderDataType),
        ByteLength = length,
        Processors = [],
        Properties = [],
    };

    // replaces the consecutive members 'old' of the parent by 'replacement'
    private static void Replace(EditGroup parent, IReadOnlyList<EditNode> old, IReadOnlyList<EditNode> replacement)
    {
        var index = parent.Members.IndexOf(old[0]);
        foreach (var node in old)
        {
            parent.Members.Remove(node);
        }
        for (var position = 0; position < replacement.Count; position++)
        {
            parent.Members.Insert(index + position, replacement[position]);
        }
    }

    private static void ValidateName(EditGroup? parent, string name, EditField? except)
    {
        if (name.Length == 0)
        {
            throw new InvalidOperationException("Enter the name of the field.");
        }
        if (name.StartsWith(PlaceholderPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Names starting with '{PlaceholderPrefix}' are reserved for unknown spans.");
        }
        if (name.Contains(SchemaPath.Separator) || name.Any(Char.IsWhiteSpace))
        {
            throw new InvalidOperationException("A field name cannot contain spaces or dots.");
        }
        if (parent?.Members.Any(member => !ReferenceEquals(member, except) && String.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase)) == true)
        {
            throw new InvalidOperationException($"'{parent.Name}' already has a member named '{name}'.");
        }
    }

    /// <summary>
    /// Adds an include of the schema that a processor reference refers to ('ref:Midi.sysexbyte' needs the 'Midi' schema),
    /// when the document does not include it yet.
    /// </summary>
    public static void EnsureInclude(SchemaWorkspaceDocument document, string? processor)
    {
        ArgumentNullException.ThrowIfNull(document);
        const string reference = "ref:";
        if (processor?.Trim() is not { } text || !text.StartsWith(reference, StringComparison.Ordinal))
        {
            return;
        }
        var separator = text.IndexOf(SchemaName.Separator, reference.Length);
        if (separator <= reference.Length)
        {
            return;
        }
        var schema = text[reference.Length..separator];
        if (!String.Equals(schema, document.Schema.Name, StringComparison.OrdinalIgnoreCase)
            && !document.Schema.Includes.Any(include => String.Equals(include.Schema, schema, StringComparison.OrdinalIgnoreCase)))
        {
            document.AddInclude(schema);
        }
    }

    /// <summary>A copy of the document (through the schema model), to try a change on without touching the document.</summary>
    public static EditDocument Copy(EditDocument document)
        => EditNodeMapper.ToEditDocument(EditNodeMapper.ToSchemaDocument(document));

    /// <summary>An empty document (no members) with the name.</summary>
    public static EditDocument CreateEmpty(string name)
    {
        var document = SchemaFileService.CreateDocument(name);
        document.Members.Clear();
        return document;
    }

    /// <summary>The field at the path (see <see cref="GetPath"/>); null when there is none.</summary>
    public static EditField? FindField(EditDocument document, string path)
    {
        EditGroup? group = document;
        EditNode? node = null;
        foreach (var name in path.Split(SchemaPath.Separator))
        {
            node = group?.Members.FirstOrDefault(member => String.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase));
            group = node as EditGroup;
        }
        return node as EditField;
    }

    /// <summary>The path of a node in the compiled schema (and its instance): the names from the root down, e.g. 'Message1.Level'.</summary>
    public static string GetPath(EditNode node)
    {
        var names = new List<string>();
        for (EditNode? current = node; current is not null and not EditDocument; current = current.Parent)
        {
            names.Insert(0, current.Name);
        }
        return String.Join(SchemaPath.Separator, names);
    }
}
