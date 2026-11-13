using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.SchemaEditor.Models;

/// <summary>
/// Edits the General tab values of one <see cref="EditNode"/> live, in text form: the name, the data type of a field and
/// the type-specific properties (nodeDef, value, byteLength, byteOffset, byteSize, count, selectedIndex, memberNodeDef and pipeline).
/// Each setter validates the text and writes it to the node at once, then marks the document changed.
/// Invalid text throws an <see cref="ArgumentException"/>: the bound editor shows the error and the node keeps its value.
/// Value-or-reference properties are written as a constant, 'ref:path' or 'pub:namespace.name'; empty means not set.
/// Nothing is written for a read-only document.
/// </summary>
internal sealed partial class NodeFacets : ObservableObject
{
    private readonly SchemaWorkspaceDocument? _document;
    private readonly EditNode? _node;
    private readonly SchemaSetWorkspace? _schemas;

    /// <summary>The pipeline choices of an open choice; empty inherits the mode of a node definition.</summary>
    public static IReadOnlyList<string> PipelineOptions { get; } = [String.Empty, nameof(SchemaPipelineMode.Integrate), nameof(SchemaPipelineMode.Isolate)];

    /// <summary>
    /// Edits <paramref name="node"/> of <paramref name="document"/>; without a node the values are empty and edits are ignored.
    /// </summary>
    /// <param name="schemas">
    /// The loaded schemas: the data type defs of the schemas the document includes are known to the value check; null for the document only.
    /// </param>
    public NodeFacets(SchemaWorkspaceDocument? document = null, EditNode? node = null, SchemaSetWorkspace? schemas = null)
    {
        _document = document;
        _node = node;
        _schemas = schemas;
    }

    public string Name
    {
        get => _node?.Name ?? String.Empty;
        set => Edit(nameof(Name), value, Name, node =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, "The name");
            node.Name = value.Trim();
        });
    }

    /// <summary>
    /// The documentation of the node (<see cref="EditNode.Doc"/>, the <c>midi.doc</c> property); empty when it has none.
    /// </summary>
    public string Doc
    {
        get => _node?.Doc ?? String.Empty;
        set
        {
            var text = value?.ReplaceLineEndings("\n").Trim() ?? String.Empty;
            Edit(nameof(Doc), text, Doc, node => node.Doc = text);
        }
    }

    /// <summary>
    /// The data type of a field; a field needs a data type or a node def.
    /// </summary>
    public string DataType
    {
        get => (_node as EditField)?.DataType?.ToString() ?? String.Empty;
        set => Edit(nameof(DataType), value, DataType, node =>
        {
            if (node is not EditField fieldNode)
            {
                return;
            }
            var dataType = ParseName(value);
            if (dataType is null && fieldNode.NodeDef is null)
            {
                throw new ArgumentException("A field data type or node definition is required.");
            }
            fieldNode.DataType = dataType;
        }, nameof(ValueWarning), nameof(DataTypeWarning), nameof(DataTypeNote));
    }

    public string NodeDef
    {
        get => _node?.NodeDef?.ToString() ?? String.Empty;
        set => Edit(nameof(NodeDef), value, NodeDef, node =>
        {
            var nodeDef = ParseName(value);
            if (nodeDef is null && node is EditField { DataType: null })
            {
                throw new ArgumentException("A field data type or node definition is required.");
            }
            node.NodeDef = nodeDef;
        });
    }

    public string Value
    {
        get => (_node as EditField)?.Value is { } value ? Format(value) : String.Empty;
        set => Edit(nameof(Value), value, Value, node =>
        {
            if (node is EditField fieldNode)
            {
                fieldNode.Value = ParseValueOrRef(value, "The value", (text, _) => text);
            }
        }, nameof(ValueWarning));
    }

    public string ByteLength
    {
        get => (_node as EditField)?.ByteLength is { } value ? Format(value) : String.Empty;
        set => Edit(nameof(ByteLength), value, ByteLength, node =>
        {
            if (node is EditField fieldNode)
            {
                fieldNode.ByteLength = ParseValueOrRef(value, "The byte length", ParseInt);
            }
        });
    }

    public string ByteOffset
    {
        get => (_node as EditField)?.ByteOffset?.ToString(CultureInfo.InvariantCulture) ?? String.Empty;
        set => Edit(nameof(ByteOffset), value, ByteOffset, node =>
        {
            if (node is EditField fieldNode)
            {
                fieldNode.ByteOffset = String.IsNullOrWhiteSpace(value) ? null : ParseInt(value.Trim(), "The byte offset");
            }
        });
    }

    public string ByteSize
    {
        get => (_node as EditGroup)?.ByteSize is { } value ? Format(value) : String.Empty;
        set => Edit(nameof(ByteSize), value, ByteSize, node =>
        {
            if (node is EditGroup group)
            {
                group.ByteSize = ParseValueOrRef(value, "The byte size", ParseInt);
            }
        });
    }

    public string Count
    {
        get => (_node as EditRepeat)?.Count is { } value ? Format(value) : String.Empty;
        set => Edit(nameof(Count), value, Count, node =>
        {
            if (node is EditRepeat repeat)
            {
                repeat.Count = ParseValueOrRef(value, "The count", ParseInt);
            }
        });
    }

    public string SelectedIndex
    {
        get => (_node as EditChoice)?.SelectedIndex is { } value ? Format(value) : String.Empty;
        set => Edit(nameof(SelectedIndex), value, SelectedIndex, node =>
        {
            if (node is EditChoice choice)
            {
                choice.SelectedIndex = ParseValueOrRef(value, "The selected index", ParseInt);
            }
        });
    }

    public string MemberNodeDef
    {
        get => (_node as EditChoice)?.MemberNodeDef?.ToString() ?? String.Empty;
        set => Edit(nameof(MemberNodeDef), value, MemberNodeDef, node =>
        {
            if (node is not EditChoice choice)
            {
                return;
            }
            var memberNodeDef = ParseName(value);
            if (memberNodeDef is not null && choice.Members.Count > 0)
            {
                throw new ArgumentException("Open choices select their body at runtime and cannot contain explicit members.");
            }
            choice.MemberNodeDef = memberNodeDef;
        });
    }

    public string Pipeline
    {
        get => (_node as EditChoice)?.Pipeline?.ToString() ?? String.Empty;
        set => Edit(nameof(Pipeline), value, Pipeline, node =>
        {
            if (node is EditChoice choice)
            {
                choice.Pipeline = String.IsNullOrWhiteSpace(value) ? null
                    : Enum.TryParse<SchemaPipelineMode>(value, ignoreCase: true, out var mode) ? mode
                    : throw new ArgumentException($"'{value}' is not a pipeline mode.");
            }
        });
    }

    /// <summary>
    /// The label of the value-or-reference property <paramref name="facet"/> (the name of <see cref="Value"/>, <see cref="ByteLength"/>,
    /// <see cref="ByteSize"/>, <see cref="Count"/> or <see cref="SelectedIndex"/>), e.g. "byte size".
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The facet is not a value-or-reference property.</exception>
    public static string GetValueOrRefLabel(string facet) => facet switch
    {
        nameof(Value) => "value",
        nameof(ByteLength) => "byte length",
        nameof(ByteSize) => "byte size",
        nameof(Count) => "count",
        nameof(SelectedIndex) => "selected index",
        _ => throw new ArgumentOutOfRangeException(nameof(facet), facet, "Not a value-or-reference property.")
    };

    /// <summary>The text of the value-or-reference property <paramref name="facet"/> (see <see cref="GetValueOrRefLabel"/>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The facet is not a value-or-reference property.</exception>
    public string GetValueOrRef(string facet) => facet switch
    {
        nameof(Value) => Value,
        nameof(ByteLength) => ByteLength,
        nameof(ByteSize) => ByteSize,
        nameof(Count) => Count,
        nameof(SelectedIndex) => SelectedIndex,
        _ => throw new ArgumentOutOfRangeException(nameof(facet), facet, "Not a value-or-reference property.")
    };

    /// <summary>Writes the text of the value-or-reference property <paramref name="facet"/> (see <see cref="GetValueOrRefLabel"/>).</summary>
    /// <exception cref="ArgumentException">The text is not valid for the property.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The facet is not a value-or-reference property.</exception>
    public void SetValueOrRef(string facet, string text)
    {
        switch (facet)
        {
            case nameof(Value): Value = text; break;
            case nameof(ByteLength): ByteLength = text; break;
            case nameof(ByteSize): ByteSize = text; break;
            case nameof(Count): Count = text; break;
            case nameof(SelectedIndex): SelectedIndex = text; break;
            default: throw new ArgumentOutOfRangeException(nameof(facet), facet, "Not a value-or-reference property.");
        }
    }

    /// <summary>
    /// A warning when the Value of a field does not fit its data type (see <see cref="DataTypeValueValidator"/>); otherwise null.
    /// Informational only: the value is still written.
    /// </summary>
    public string? ValueWarning => _node is EditField
        ? DataTypeValueValidator.Validate(Scope, DataType, Value)
        : null;

    /// <summary>
    /// A warning when the data type of a field is a data type def of a loaded schema the document does not include
    /// (see <see cref="DataTypeValueValidator.ValidateInclude"/>); otherwise null.
    /// </summary>
    public string? DataTypeWarning => _node is EditField
        ? DataTypeValueValidator.ValidateInclude(Scope, DataType)
        : null;

    /// <summary>
    /// A note when the data type of a field is custom (not registered and not defined in the document or the schemas it includes); otherwise null.
    /// </summary>
    public string? DataTypeNote => _node is EditField && DataTypeValueValidator.IsCustom(Scope, DataType)
        ? "Custom data type: its values are not validated."
        : null;

    /// <summary>
    /// What the reference properties of the node refer to, by facet name (e.g. <c>References[Count]</c>):
    /// see <see cref="FindReference"/>. A new instance after every edit, so bindings to it are refreshed.
    /// </summary>
    public NodeReferences References => new(this);

    /// <summary>
    /// What the property <paramref name="facet"/> of the node refers to, so it can be followed (→): the node def of
    /// <see cref="NodeDef"/> or <see cref="MemberNodeDef"/> (<see cref="SchemaSetWorkspace.FindNodeDef"/>), the data type def of
    /// <see cref="DataType"/> (<see cref="SchemaSetWorkspace.FindDataTypeDef"/>), or the field of a 'ref:' in a value-or-reference
    /// property (<see cref="SchemaSetWorkspace.FindNodeRef"/>). Looked up in all loaded schemas.
    /// </summary>
    public NodeReference FindReference(string facet)
    {
        var label = facet switch
        {
            nameof(NodeDef) => "node def",
            nameof(MemberNodeDef) => "member node def",
            nameof(DataType) => "data type",
            _ => GetValueOrRefLabel(facet)
        };
        var text = (facet switch
        {
            nameof(NodeDef) => NodeDef,
            nameof(MemberNodeDef) => MemberNodeDef,
            nameof(DataType) => DataType,
            _ => GetValueOrRef(facet)
        }).Trim();

        NodeReference NotFollowed(string problem) => new(label, null, problem);
        if (_node is null)
        {
            return NotFollowed("No node is selected.");
        }
        if (text.Length == 0)
        {
            return NotFollowed($"The {label} of {_node.Name} is not set.");
        }
        if (_schemas is null)
        {
            return NotFollowed("The loaded schemas are not known here.");
        }

        object? target;
        string? problem;
        switch (facet)
        {
            case nameof(NodeDef) or nameof(MemberNodeDef):
                target = _schemas.FindNodeDef(_node, new SchemaName(text), out problem);
                break;
            case nameof(DataType):
                target = _schemas.FindDataTypeDef(_node, new SchemaName(text), out problem);
                break;
            default:
                if (!SchemaNodeRef.TryParse(text, out var reference))
                {
                    return NotFollowed($"The {label} of {_node.Name} is not a reference to a field (ref:).");
                }
                target = _schemas.FindNodeRef(_node, reference, out problem);
                break;
        }
        return new(label, target, target is null ? $"The {label} of {_node.Name} cannot be followed: {problem}" : null);
    }

    // the document and the schemas it includes, as loaded now
    private SchemaScope? Scope => _document is null ? null : SchemaScope.Of(_document.Schema, _schemas);

    /// <summary>
    /// Writes <paramref name="value"/> to the node through <paramref name="assign"/> (which validates it) and marks the document changed.
    /// Nothing happens when the text equals the <paramref name="current"/> text (e.g. an editor that lost focus without a change).
    /// </summary>
    private void Edit(string propertyName, string? value, string current, Action<EditNode> assign, params string[] dependents)
    {
        if (_node is null || _document is null || _document.IsReadOnly
            || String.Equals((value ?? String.Empty).Trim(), current, StringComparison.Ordinal))
        {
            return;
        }
        assign(_node);
        _document.MarkChanged();
        OnPropertyChanged(propertyName);
        // any edit may change what a reference refers to (e.g. a renamed node)
        OnPropertyChanged(nameof(References));
        foreach (var dependent in dependents)
        {
            OnPropertyChanged(dependent);
        }
    }

    private static string Format<T>(SchemaValueOrRef<T> value) => value switch
    {
        SchemaNodeRef reference => reference.ToString(),
        SchemaPubRef reference => reference.ToString(),
        T constant => Convert.ToString(constant, CultureInfo.InvariantCulture) ?? String.Empty,
        _ => String.Empty
    };

    private static SchemaName? ParseName(string? text)
        => String.IsNullOrWhiteSpace(text) ? (SchemaName?)null : new SchemaName(text.Trim());

    private static SchemaValueOrRef<T> ParseValueOrRef<T>(string? text, string what, Func<string, string, T> parseConstant)
    {
        text = text?.Trim() ?? String.Empty;
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

/// <summary>
/// What a reference property of a node refers to (see <see cref="NodeFacets.FindReference"/>): the node or document item
/// to follow (→) to, or why it cannot be followed.
/// </summary>
/// <param name="Label">How the property is called in messages, e.g. "byte size".</param>
/// <param name="Target">The node or data type def it refers to; null when it cannot be followed.</param>
/// <param name="Problem">Why it cannot be followed; null when it can.</param>
internal sealed record NodeReference(string Label, object? Target, string? Problem)
{
    /// <summary>True when the reference can be followed (→).</summary>
    public bool CanGoTo => Target is not null;

    /// <summary>The tool tip of the → button: why it cannot be followed, or what it goes to.</summary>
    public string Tip => Problem ?? $"Go to the {Label} this refers to";
}

/// <summary>
/// The references of the node edited by <see cref="NodeFacets"/>, by facet name, for bindings such as
/// <c>References[Count].CanGoTo</c>. Each is looked up when it is read.
/// </summary>
internal sealed class NodeReferences(NodeFacets facets)
{
    public NodeReference this[string facet] => facets.FindReference(facet);
}
