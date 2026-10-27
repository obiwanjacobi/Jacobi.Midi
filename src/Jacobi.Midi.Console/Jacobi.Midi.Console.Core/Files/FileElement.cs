using System.Globalization;

namespace Jacobi.Midi.Console.Core.Files;

/// <summary>
/// A format-neutral element of a console file: a name, ordered attributes with text values and ordered child
/// elements. The rig and dashboard mappers work with these elements only; <see cref="XmlFile"/> and
/// <see cref="JsonFile"/> turn them into text and back. While mapping, the attributes that are read and whether the
/// children were taken are tracked, so content nobody reads (a typo, an unknown setting, an element where none
/// belongs) is reported by <see cref="EnsureAllRead"/>.
/// </summary>
internal sealed class FileElement(string name, FileRole? role = null)
{
    private readonly List<FileAttribute> _attributes = [];
    private readonly List<FileElement> _children = [];
    private readonly HashSet<string> _read = new(StringComparer.Ordinal);
    private bool _childrenTaken;

    public string Name => name;

    /// <summary>The role of the element in its parent; shapes the JSON (see <see cref="FileRoles"/>).</summary>
    public FileRole? Role => role;

    public FileElement? Parent { get; private set; }

    public IReadOnlyList<FileAttribute> Attributes => _attributes;

    public IReadOnlyList<FileElement> Children => _children;

    /// <summary>The location in the file, for messages: e.g. <c>dashboard/row[0]/knob[1]</c>.</summary>
    public string Location
        => Parent is null ? Name : $"{Parent.Location}/{Name}[{Parent._children.IndexOf(this)}]";

    public FileElement Add(FileElement child)
    {
        child.Parent = this;
        _children.Add(child);
        return child;
    }

    // writing; a null value is not written

    public FileElement Set(string attribute, string? value, FileValueKind kind = FileValueKind.Text)
    {
        if (value is not null)
            _attributes.Add(new FileAttribute(attribute, value, kind));
        return this;
    }

    public FileElement Set(string attribute, int? value)
        => Set(attribute, value?.ToString(CultureInfo.InvariantCulture), FileValueKind.Number);

    /// <summary>Writes the flag only when it is true.</summary>
    public FileElement SetFlag(string attribute, bool value)
        => value ? Set(attribute, "true", FileValueKind.Boolean) : this;

    public FileElement Set(string attribute, int first, int second)
        => Set(attribute, String.Create(CultureInfo.InvariantCulture, $"{first},{second}"), FileValueKind.NumberList);

    /// <summary>The value of an attribute without marking it read (for the file formats).</summary>
    public string? Find(string attribute)
        => _attributes.FirstOrDefault(candidate => candidate.Name == attribute)?.Value;

    // reading; marks the attribute read

    public string? Get(string attribute)
    {
        var value = Find(attribute);
        if (value is not null)
            _read.Add(attribute);
        return value;
    }

    public string GetRequired(string attribute)
        => Get(attribute) ?? throw Error($"'{attribute}' is required.");

    public int? GetInt(string attribute)
    {
        if (Get(attribute) is not { } text)
            return null;
        return Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Error($"'{attribute}': '{text}' is not a whole number.");
    }

    public bool GetFlag(string attribute)
    {
        if (Get(attribute) is not { } text)
            return false;
        return Boolean.TryParse(text, out var value)
            ? value
            : throw Error($"'{attribute}': '{text}' is not true or false.");
    }

    /// <summary>A pair of whole numbers, written as <c>6,3</c>.</summary>
    public (int First, int Second)? GetPair(string attribute)
    {
        if (Get(attribute) is not { } text)
            return null;
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 2
            && Int32.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var first)
            && Int32.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var second))
        {
            return (first, second);
        }
        throw Error($"'{attribute}': '{text}' is not a pair of whole numbers (e.g. 2,2).");
    }

    /// <summary>An enum value by its file name (camel case, see <see cref="FileNames"/>).</summary>
    public TEnum? GetEnum<TEnum>(string attribute)
        where TEnum : struct, Enum
    {
        if (Get(attribute) is not { } text)
            return null;
        return FileNames.TryParse<TEnum>(text, out var value)
            ? value
            : throw Error($"'{attribute}': '{text}' is not one of {String.Join(", ", Enum.GetValues<TEnum>().Select(FileNames.Of))}.");
    }

    /// <summary>The child elements, for the mapper to read; an element whose children are not taken may have none.</summary>
    public IReadOnlyList<FileElement> TakeChildren()
    {
        _childrenTaken = true;
        return _children;
    }

    /// <summary>
    /// Throws when the element has attributes that were not read, or child elements while its children were not
    /// taken: content unknown to the mapper.
    /// </summary>
    public void EnsureAllRead()
    {
        var unknown = _attributes.Where(attribute => !_read.Contains(attribute.Name)).Select(attribute => $"'{attribute.Name}'").ToList();
        if (unknown.Count > 0)
            throw Error($"unknown attribute{(unknown.Count > 1 ? "s" : "")} {String.Join(", ", unknown)}.");
        if (!_childrenTaken && _children.Count > 0)
            throw _children[0].Error($"'{Name}' cannot contain '{_children[0].Name}'.");
    }

    public FileFormatException Error(string message) => new($"{Location}: {message}");
}

/// <summary>An attribute of a <see cref="FileElement"/>: a name and a text value, with the kind of value for JSON.</summary>
internal sealed record FileAttribute(string Name, string Value, FileValueKind Kind);

/// <summary>
/// How an attribute value is written in JSON. XML writes all values as text; reading accepts both.
/// </summary>
internal enum FileValueKind
{
    Text,
    Number,
    Boolean,
    /// <summary>Numbers separated by commas (<c>6,3</c>); a JSON array of numbers.</summary>
    NumberList,
}
