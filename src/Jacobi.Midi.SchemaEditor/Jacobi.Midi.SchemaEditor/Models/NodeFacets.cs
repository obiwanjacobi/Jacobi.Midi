using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.SchemaEditor.Models;

/// <summary>
/// The staged, text form of the type-specific properties of an <see cref="EditNode"/>
/// (nodeDef, value, byteLength, byteOffset, byteSize, count, selectedIndex, memberNodeDef and pipeline).
/// Reads them from a node for editing and validates and writes them back as one unit.
/// Value-or-reference properties are written as a constant, 'ref:path' or 'pub:namespace.name'; empty means not set.
/// </summary>
internal sealed partial class NodeFacets : ObservableObject
{
    /// <summary>The pipeline choices of an open choice; empty inherits the mode of a node definition.</summary>
    public static IReadOnlyList<string> PipelineOptions { get; } = [String.Empty, nameof(SchemaPipelineMode.Integrate), nameof(SchemaPipelineMode.Isolate)];

    public string NodeDef { get; set; } = String.Empty;
    [ObservableProperty]
    public partial string Value { get; set; } = String.Empty;
    public string ByteLength { get; set; } = String.Empty;
    public string ByteOffset { get; set; } = String.Empty;
    public string ByteSize { get; set; } = String.Empty;
    public string Count { get; set; } = String.Empty;
    public string SelectedIndex { get; set; } = String.Empty;
    public string MemberNodeDef { get; set; } = String.Empty;
    public string Pipeline { get; set; } = String.Empty;

    /// <summary>
    /// Reads the staged values from <paramref name="node"/> (empty values for a null node).
    /// </summary>
    public static NodeFacets Read(EditNode? node)
    {
        var facets = new NodeFacets { NodeDef = node?.NodeDef?.ToString() ?? String.Empty };
        switch (node)
        {
            case EditField field:
                facets.Value = Format(field.Value);
                facets.ByteLength = Format(field.ByteLength);
                facets.ByteOffset = field.ByteOffset?.ToString(CultureInfo.InvariantCulture) ?? String.Empty;
                break;
            case EditGroup group:
                facets.ByteSize = Format(group.ByteSize);
                if (group is EditRepeat repeat)
                {
                    facets.Count = Format(repeat.Count);
                }
                else if (group is EditChoice choice)
                {
                    facets.SelectedIndex = Format(choice.SelectedIndex);
                    facets.MemberNodeDef = choice.MemberNodeDef?.ToString() ?? String.Empty;
                    facets.Pipeline = choice.Pipeline?.ToString() ?? String.Empty;
                }
                break;
        }
        return facets;
    }

    /// <summary>
    /// Writes the staged values to <paramref name="node"/>. Nothing is changed when a value is invalid.
    /// </summary>
    /// <exception cref="ArgumentException">A value could not be parsed.</exception>
    public void Apply(EditNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var nodeDef = ParseName(NodeDef);
        switch (node)
        {
            case EditField field:
                var value = ParseValueOrRef(Value, "The value", (text, _) => text);
                var byteLength = ParseValueOrRef(ByteLength, "The byte length", ParseInt);
                var byteOffset = String.IsNullOrWhiteSpace(ByteOffset) ? (int?)null : ParseInt(ByteOffset, "The byte offset");
                field.Value = value;
                field.ByteLength = byteLength;
                field.ByteOffset = byteOffset;
                break;
            case EditRepeat repeat:
                var count = ParseValueOrRef(Count, "The count", ParseInt);
                var repeatSize = ParseValueOrRef(ByteSize, "The byte size", ParseInt);
                repeat.Count = count;
                repeat.ByteSize = repeatSize;
                break;
            case EditChoice choice:
                var selectedIndex = ParseValueOrRef(SelectedIndex, "The selected index", ParseInt);
                var memberNodeDef = ParseName(MemberNodeDef);
                var pipeline = String.IsNullOrWhiteSpace(Pipeline) ? (SchemaPipelineMode?)null
                    : Enum.TryParse<SchemaPipelineMode>(Pipeline, ignoreCase: true, out var mode) ? mode
                    : throw new ArgumentException($"'{Pipeline}' is not a pipeline mode.");
                var choiceSize = ParseValueOrRef(ByteSize, "The byte size", ParseInt);
                if (memberNodeDef is not null && choice.Members.Count > 0)
                {
                    throw new ArgumentException("Open choices select their body at runtime and cannot contain explicit members.");
                }
                choice.SelectedIndex = selectedIndex;
                choice.MemberNodeDef = memberNodeDef;
                choice.Pipeline = pipeline;
                choice.ByteSize = choiceSize;
                break;
            case EditGroup group:
                group.ByteSize = ParseValueOrRef(ByteSize, "The byte size", ParseInt);
                break;
        }
        node.NodeDef = nodeDef;
    }

    private static string Format<T>(SchemaValueOrRef<T> value) => value switch
    {
        SchemaNodeRef reference => reference.ToString(),
        SchemaPubRef reference => reference.ToString(),
        T constant => Convert.ToString(constant, CultureInfo.InvariantCulture) ?? String.Empty,
        _ => String.Empty
    };

    private static SchemaName? ParseName(string text)
        => String.IsNullOrWhiteSpace(text) ? (SchemaName?)null : new SchemaName(text.Trim());

    private static SchemaValueOrRef<T> ParseValueOrRef<T>(string text, string what, Func<string, string, T> parseConstant)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return default;
        }
        if (text.StartsWith(SchemaNodeRef.Prefix, StringComparison.Ordinal))
        {
            return SchemaNodeRef.TryParse(text, out var node) ? node : throw new ArgumentException($"{what} has an invalid reference '{text}'.");
        }
        if (text.StartsWith(SchemaPubRef.Prefix, StringComparison.Ordinal))
        {
            return SchemaPubRef.TryParse(text, out var pub) ? pub : throw new ArgumentException($"{what} has an invalid reference '{text}'.");
        }
        return parseConstant(text, what);
    }

    private static int ParseInt(string text, string what)
        => Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new ArgumentException($"{what} must be a whole number, 'ref:path' or 'pub:namespace.name'.");
}
