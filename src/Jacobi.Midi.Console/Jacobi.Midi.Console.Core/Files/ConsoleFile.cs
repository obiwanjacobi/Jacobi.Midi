using System.Globalization;
using Jacobi.Midi.Console.Core.Schemas;

namespace Jacobi.Midi.Console.Core.Files;

/// <summary>
/// What all console files share: the choice of text format, the <c>format</c> attribute of the root that names the
/// kind of file and its version, the schema sources (<c>assembly</c> and <c>schema</c> elements) and the notation
/// of colours.
/// </summary>
internal static class ConsoleFile
{
    /// <summary>Adds the sources as <c>assembly</c> and <c>schema</c> elements.</summary>
    public static void AddSources(FileElement element, SchemaSources sources)
    {
        foreach (var assembly in sources.Assemblies)
            element.Add(new FileElement(FileRoles.Assemblies.ElementName!, FileRoles.Assemblies).Set(FileRoles.Assemblies.ValueAttribute!, assembly));
        foreach (var schema in sources.Schemas)
            element.Add(new FileElement(FileRoles.Schemas.ElementName!, FileRoles.Schemas).Set(FileRoles.Schemas.ValueAttribute!, schema));
    }

    /// <summary>True when the element is an <c>assembly</c> or <c>schema</c> element, read into the lists.</summary>
    public static bool TryReadSource(FileElement element, List<string> assemblies, List<string> schemas)
    {
        var role = element.Name == FileRoles.Assemblies.ElementName ? FileRoles.Assemblies
            : element.Name == FileRoles.Schemas.ElementName ? FileRoles.Schemas
            : null;
        if (role is null)
            return false;

        (role == FileRoles.Assemblies ? assemblies : schemas).Add(element.GetRequired(role.ValueAttribute!));
        element.EnsureAllRead();
        return true;
    }

    public const string FormatAttribute = "format";

    public static FileElement Read(string text, FileFormat format, string rootName)
    {
        ArgumentNullException.ThrowIfNull(text);
        return format switch
        {
            FileFormat.Xml => XmlFile.Read(text, rootName),
            FileFormat.Json => JsonFile.Read(text, rootName),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };
    }

    public static string Write(FileElement root, FileFormat format)
        => format switch
        {
            FileFormat.Xml => XmlFile.Write(root),
            FileFormat.Json => JsonFile.Write(root),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

    /// <summary>Checks the root's <c>format</c>: the kind of file and the version this code reads.</summary>
    public static void CheckFormat(FileElement root, string expected)
    {
        var format = root.GetRequired(FormatAttribute);
        if (format != expected)
            throw root.Error($"'{FormatAttribute}': '{format}' is not supported; expected '{expected}'.");
    }

    /// <summary>A colour: <c>#RRGGBB</c> or <c>#AARRGGBB</c>.</summary>
    public static string? GetColor(FileElement element, string attribute)
    {
        if (element.Get(attribute) is not { } text)
            return null;
        return text.Length is 7 or 9 && text[0] == '#'
            && UInt32.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _)
            ? text
            : throw element.Error($"'{attribute}': '{text}' is not a colour (#RRGGBB or #AARRGGBB).");
    }

    /// <summary>A whole number of at least <paramref name="minimum"/>.</summary>
    public static int? GetInt(FileElement element, string attribute, int minimum)
    {
        var value = element.GetInt(attribute);
        return value < minimum
            ? throw element.Error($"'{attribute}': {value} is less than {minimum}.")
            : value;
    }
}
