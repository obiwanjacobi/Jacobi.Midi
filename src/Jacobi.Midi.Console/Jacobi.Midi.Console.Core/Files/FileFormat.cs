namespace Jacobi.Midi.Console.Core.Files;

/// <summary>The text formats of the console's files (rig, dashboard).</summary>
public enum FileFormat
{
    Xml,
    Json,
}

/// <summary>
/// Selects the file format by file extension, as the schema files do.
/// </summary>
public static class FileFormats
{
    /// <summary>The format of a file by its extension (<c>.xml</c> or <c>.json</c>, case-insensitive).</summary>
    public static FileFormat FromPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var extension = Path.GetExtension(path);
        if (String.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
            return FileFormat.Xml;
        if (String.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
            return FileFormat.Json;
        throw new ArgumentException($"'{path}': unknown file format; use .xml or .json.", nameof(path));
    }
}

/// <summary>
/// A console file that cannot be read: invalid XML or JSON, or content that does not fit the file's structure. The
/// message starts with the location in the file (e.g. <c>dashboard/row[0]/knob[1]</c>).
/// </summary>
public sealed class FileFormatException : Exception
{
    public FileFormatException(string message)
        : base(message)
    { }

    public FileFormatException(string message, Exception innerException)
        : base(message, innerException)
    { }
}
