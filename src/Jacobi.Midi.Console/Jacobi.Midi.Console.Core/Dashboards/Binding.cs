using System.Diagnostics.CodeAnalysis;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Console.Core.Dashboards;

/// <summary>
/// What fills a slot of a parameter control, as written in the dashboard: a device parameter, <c>[device:]path</c>, or
/// an index variable, <c>$name</c>. The path is a schema path in the notation of the serializer's node references: a
/// repeat item is given by its index (<c>Programs[5].Program.Level</c>) or by an index variable
/// (<c>Programs[$patch].Program.Level</c>). Without its own <see cref="Device"/> the path is relative to the nearest base
/// and the device is the nearest one of the control or the layout controls around it; a variable is the nearest
/// declaration (see <see cref="BindingResolver"/>).
/// </summary>
/// <param name="Path">The schema path with repeat indices: relative to the nearest base, or from the map root when
/// there is none; or <c>$name</c> for an index variable.</param>
public sealed record Binding(string Path)
{
    /// <summary>Separates the device alias from the path.</summary>
    public const char DeviceSeparator = ':';

    /// <summary>
    /// The device alias of this binding. With it, <see cref="Path"/> is a full path from the map root: a base belongs
    /// to the device it was given with.
    /// </summary>
    public string? Device { get; init; }

    /// <summary>The name of the index variable this binding is to; null for a device parameter.</summary>
    public string? Variable => Path.Length > 1 && Path[0] == SchemaPaths.VariablePrefix ? Path[1..] : null;

    /// <summary>The binding as written: <c>[device:]path</c> or <c>$name</c>.</summary>
    public override string ToString() => Device is null ? Path : $"{Device}{DeviceSeparator}{Path}";

    /// <summary>
    /// Parses <c>[device:]path</c> (the path must be a valid schema path with repeat indices) or <c>$name</c>
    /// (a valid variable name, without device).
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out Binding? binding, [NotNullWhen(false)] out string? error)
    {
        binding = null;
        if (String.IsNullOrWhiteSpace(text))
        {
            error = "a binding needs a path.";
            return false;
        }

        if (text[0] == SchemaPaths.VariablePrefix)
        {
            if (!SchemaPaths.IsVariableName(text[1..]))
            {
                error = $"'{text}': '{text[1..]}' is not a variable name (a letter or '_', then letters, digits or '_').";
                return false;
            }
            error = null;
            binding = new Binding(text);
            return true;
        }

        var separator = text.IndexOf(DeviceSeparator);
        var device = separator < 0 ? null : text[..separator];
        var path = separator < 0 ? text : text[(separator + 1)..];
        if (device is { Length: 0 })
        {
            error = $"'{text}': the device alias before '{DeviceSeparator}' is empty.";
            return false;
        }
        if (path.StartsWith(SchemaPaths.VariablePrefix))
        {
            error = $"'{text}': an index variable belongs to the dashboard, not to a device: write '{path}'.";
            return false;
        }
        // the variables are checked when the binding is resolved; any value will do to check the path
        if (!SchemaPaths.TryParse(path, _ => 0, out _, out error))
        {
            error = $"'{text}': {error}";
            return false;
        }

        binding = new Binding(path) { Device = device };
        return true;
    }
}

/// <summary>
/// The identity of a device parameter: the device alias, the full schema path from the map root (without indices)
/// and the instance. Device state and synchronisation work with this identity.
/// </summary>
public sealed record ParameterReference(string Device, string Path, InstancePath Instance);

/// <summary>
/// Schema paths with repeat indices, used for bindings and bases: a constant index (<c>Programs[5].Program.Level</c>)
/// or an index variable (<c>Programs[$patch].Program.Level</c>). Parsed with the serializer's node reference notation
/// after the variables are replaced by their values.
/// </summary>
public static partial class SchemaPaths
{
    /// <summary>Starts the name of an index variable in a repeat index: <c>[$patch]</c>.</summary>
    public const char VariablePrefix = '$';

    [System.Text.RegularExpressions.GeneratedRegex(@"\[\$([A-Za-z_][A-Za-z0-9_]*)\]")]
    private static partial System.Text.RegularExpressions.Regex VariableIndex();

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial System.Text.RegularExpressions.Regex VariableName();

    /// <summary>True when the text is a valid name of an index variable: a letter or '_', then letters, digits or '_'.</summary>
    public static bool IsVariableName(string? text) => text is not null && VariableName().IsMatch(text);

    /// <summary>
    /// Splits a path with repeat indices into the schema path and the instance, with the value of each index variable
    /// given by <paramref name="valueOf"/> (null when the variable is not declared).
    /// </summary>
    public static bool TryParse(string? text, Func<string, int?> valueOf, [NotNullWhen(true)] out ParsedPath? parsed,
        [NotNullWhen(false)] out string? error)
    {
        parsed = null;
        if (String.IsNullOrEmpty(text))
        {
            error = "the path is empty.";
            return false;
        }

        // the repeat each variable indexes: the path up to the segment with the variable, without indices
        var uses = new List<VariableUse>();
        var names = new List<string>();
        foreach (var segment in text.Split(SchemaPath.Separator))
        {
            var open = segment.IndexOf('[');
            names.Add(open < 0 ? segment : segment[..open]);
            if (VariableIndex().Match(segment) is { Success: true } match)
                uses.Add(new VariableUse(match.Groups[1].Value, String.Join(SchemaPath.Separator, names)));
        }

        string? undeclared = null;
        var constant = VariableIndex().Replace(text, match =>
        {
            var name = match.Groups[1].Value;
            if (valueOf(name) is { } value)
                return $"[{value}]";
            undeclared ??= name;
            return "[0]";
        });
        if (undeclared is not null)
        {
            error = $"'{text}': the index variable '{VariablePrefix}{undeclared}' is not declared.";
            return false;
        }

        if (!SchemaNodeRef.TryParse(SchemaNodeRef.Prefix + constant, out var nodeRef))
        {
            error = $"'{text}' is not a valid schema path (names separated by '.', a repeat item as 'Name[index]' or 'Name[{VariablePrefix}variable]').";
            return false;
        }
        if (nodeRef.IsRelative || nodeRef.Indices.Any(index => index.IsCurrent))
        {
            error = $"'{text}': a binding cannot be relative to a node ('^' or '[]').";
            return false;
        }

        error = null;
        parsed = new ParsedPath(nodeRef.Path, new InstancePath(nodeRef.Indices.Select(index => index.Index)), uses);
        return true;
    }
}

/// <summary>A parsed path: the schema path without indices, the instance, and where index variables are used.</summary>
public sealed record ParsedPath(string Path, InstancePath Instance, IReadOnlyList<VariableUse> Variables);

/// <summary>An index variable used in a path: its name and the schema path of the repeat it indexes.</summary>
public sealed record VariableUse(string Variable, string Repeat);
