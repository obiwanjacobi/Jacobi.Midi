using System.Globalization;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Core.Schemas;

namespace Jacobi.Midi.Console.Core.Files;

/// <summary>
/// What all console files share: the choice of text format, the <c>format</c> attribute of the root that names the
/// kind of file and its version, the schema sources (<c>assembly</c>, <c>schema</c> and <c>exchange</c> elements) and the notation
/// of colors.
/// </summary>
internal static class ConsoleFile
{
    // the roles of the sources, with the list of the sources each one is read into and written from
    private static readonly (FileRole Role, Func<SchemaSources, IReadOnlyList<string>> Get, Func<SchemaSources, IReadOnlyList<string>, SchemaSources> With)[] SourceRoles =
    [
        (FileRoles.Assemblies, sources => sources.Assemblies, (sources, paths) => sources with { Assemblies = paths }),
        (FileRoles.Schemas, sources => sources.Schemas, (sources, paths) => sources with { Schemas = paths }),
        (FileRoles.Exchanges, sources => sources.Exchanges, (sources, paths) => sources with { Exchanges = paths }),
    ];

    /// <summary>Adds the sources as <c>assembly</c>, <c>schema</c> and <c>exchange</c> elements.</summary>
    public static void AddSources(FileElement element, SchemaSources sources)
    {
        foreach (var (role, get, _) in SourceRoles)
        {
            foreach (var path in get(sources))
                element.Add(new FileElement(role.ElementName!, role).Set(role.ValueAttribute!, path));
        }
    }

    /// <summary>True when the element is a source element (<c>assembly</c>, <c>schema</c> or <c>exchange</c>).</summary>
    public static bool IsSource(FileElement element)
        => SourceRoles.Any(source => source.Role.ElementName == element.Name);

    /// <summary>True when the element is a source element (see <see cref="IsSource"/>); its path is added to the sources.</summary>
    public static bool TryReadSource(FileElement element, ref SchemaSources sources)
    {
        foreach (var (role, get, with) in SourceRoles)
        {
            if (role.ElementName != element.Name)
                continue;
            sources = with(sources, [.. get(sources), element.GetRequired(role.ValueAttribute!)]);
            element.EnsureAllRead();
            return true;
        }
        return false;
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

    /// <summary>The colors of the areas: <c>color</c> (the accent), <c>plateColor</c> and <c>panelColor</c>.</summary>
    public static AreaColors GetColors(FileElement element)
        => new(GetColor(element, ColorAttribute(ColorArea.Accent)), GetColor(element, ColorAttribute(ColorArea.Plate)), GetColor(element, ColorAttribute(ColorArea.Panel)));

    /// <summary>Sets the colors the areas have; see <see cref="GetColors"/>.</summary>
    public static FileElement SetColors(FileElement element, AreaColors colors)
    {
        foreach (var area in AreaColors.Areas)
            element.Set(ColorAttribute(area), colors[area]);
        return element;
    }

    private static string ColorAttribute(ColorArea area)
        => area == ColorArea.Accent ? "color" : $"{FileNames.Of(area)}Color";

    /// <summary>A color: <c>#RRGGBB</c> or <c>#AARRGGBB</c>.</summary>
    public static string? GetColor(FileElement element, string attribute)
    {
        if (element.Get(attribute) is not { } text)
            return null;
        return RgbColor.TryParse(text, out _)
            ? text
            : throw element.Error($"'{attribute}': '{text}' is not a color (#RRGGBB or #AARRGGBB).");
    }

    /// <summary>A whole number of at least <paramref name="minimum"/>.</summary>
    public static int? GetInt(FileElement element, string attribute, int minimum)
    {
        var value = element.GetInt(attribute);
        return value < minimum
            ? throw element.Error($"'{attribute}': {value} is less than {minimum}.")
            : value;
    }

    /// <summary>A number from <paramref name="minimum"/> to <paramref name="maximum"/>.</summary>
    public static decimal? GetDecimal(FileElement element, string attribute, decimal minimum, decimal maximum)
    {
        var value = element.GetDecimal(attribute);
        return value < minimum || value > maximum
            ? throw element.Error(String.Create(CultureInfo.InvariantCulture, $"'{attribute}': {value} is not from {minimum} to {maximum}."))
            : value;
    }

    /// <summary>The name of a value of an enum (camel case, see <see cref="FileNames"/>).</summary>
    public static TEnum? GetEnum<TEnum>(FileElement element, string attribute)
        where TEnum : struct, Enum
    {
        if (element.Get(attribute) is not { } text)
            return null;
        return FileNames.TryParse<TEnum>(text, out var value)
            ? value
            : throw element.Error($"'{attribute}': '{text}' is not one of {String.Join(", ", Enum.GetValues<TEnum>().Select(FileNames.Of))}.");
    }
}
