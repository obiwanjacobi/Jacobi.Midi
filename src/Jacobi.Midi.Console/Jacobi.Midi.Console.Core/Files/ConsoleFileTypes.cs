using Jacobi.Midi.Device.Exchange;

namespace Jacobi.Midi.Console.Core.Files;

/// <summary>The kinds of files the console opens.</summary>
public enum ConsoleFileKind
{
    Rig,
    Dashboard,
    Schema,
    Exchange,
    Dump,
}

/// <summary>
/// A type of file the console opens: its kind, its name in a file picker (e.g. "Rig (XML)") and the end of its file
/// name (e.g. <c>.rig.xml</c>).
/// </summary>
public sealed record ConsoleFileType(ConsoleFileKind Kind, string Name, string Extension)
{
    /// <summary>The pattern of the file names in a file picker, e.g. <c>*.rig.xml</c>.</summary>
    public string Pattern => $"*{Extension}";
}

/// <summary>
/// The convention of the names of the files the console opens: the end of a file name tells its kind
/// (<c>*.rig.xml</c>, <c>*.dashboard.json</c>, <c>*.schema.xml</c>, <c>*.exchange.xml</c>, <c>*.syx</c>, ...). The
/// single description of the file types, used by the file pickers and to tell the kind of a picked file.
/// </summary>
public static class ConsoleFileTypes
{
    /// <summary>All types, in the order a file picker offers them.</summary>
    public static IReadOnlyList<ConsoleFileType> All { get; } =
    [
        new(ConsoleFileKind.Rig, "Rig (XML)", ".rig.xml"),
        new(ConsoleFileKind.Rig, "Rig (JSON)", ".rig.json"),
        new(ConsoleFileKind.Dashboard, "Dashboard (XML)", ".dashboard.xml"),
        new(ConsoleFileKind.Dashboard, "Dashboard (JSON)", ".dashboard.json"),
        new(ConsoleFileKind.Schema, "Schema (XML)", ".schema.xml"),
        new(ConsoleFileKind.Schema, "Schema (JSON)", ".schema.json"),
        .. ExchangeDefinitions.Extensions.Select(extension =>
            new ConsoleFileType(ConsoleFileKind.Exchange, $"Exchange ({extension[(extension.LastIndexOf('.') + 1)..].ToUpperInvariant()})", extension)),
        new(ConsoleFileKind.Dump, "SysEx dump", ".syx"),
    ];

    /// <summary>The types of a kind of file, e.g. the XML and JSON rig files.</summary>
    public static IReadOnlyList<ConsoleFileType> Of(ConsoleFileKind kind) => [.. All.Where(type => type.Kind == kind)];

    /// <summary>The kind of a file by the end of its name; null when the name does not follow the convention.</summary>
    public static ConsoleFileKind? KindOf(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return All.FirstOrDefault(type => fileName.EndsWith(type.Extension, StringComparison.OrdinalIgnoreCase))?.Kind;
    }
}
