namespace Jacobi.Midi.Console.Core.Files;

/// <summary>
/// The paths one console file uses to refer to another (a schema file, an assembly, a rig): relative to the directory
/// of the file that refers, or absolute when they have no common root (another drive). Written with forward slashes,
/// so a file works on every platform.
/// </summary>
public static class FilePaths
{
    /// <summary>The path a file refers to, as a full path: a relative path is taken from <paramref name="baseDirectory"/>.</summary>
    /// <param name="baseDirectory">The directory of the file that refers; null when it is not known (the path is returned as it is).</param>
    public static string Resolve(string path, string? baseDirectory)
        => baseDirectory is null || Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDirectory, path));

    /// <summary>The path to write for <paramref name="fullPath"/> in a file in <paramref name="baseDirectory"/>.</summary>
    public static string RelativeTo(string fullPath, string baseDirectory)
        => Path.GetRelativePath(baseDirectory, fullPath).Replace('\\', '/');

    /// <summary>True when the paths are of the same file (case-insensitive: the console runs on Windows too).</summary>
    public static bool AreSame(string path, string other)
        => String.Equals(Path.GetFullPath(path), Path.GetFullPath(other), StringComparison.OrdinalIgnoreCase);
}
