using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// Where an open rig or dashboard was read from: the file to save it to (null when it has none, as a new dashboard), its
/// folder (the relative paths in it are relative to; null in the browser), and whether it changed since (observable,
/// for the changed markers).
/// </summary>
internal sealed partial class FileOrigin : ObservableObject
{
    public string Name { get; set; } = "untitled.xml";

    public IStorageFile? File { get; set; }

    public string? Directory { get; set; }

    /// <summary>The edits done since it was read or saved, less those undone; undone edits of before count as well.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    public partial int Changes { get; set; }

    public bool IsChanged => Changes != 0;

    public static FileOrigin Of(IStorageFile file)
        => new()
        {
            Name = file.Name,
            File = file,
            Directory = file.TryGetLocalPath() is { } path ? Path.GetDirectoryName(path) : null,
        };
}
