using CommunityToolkit.Mvvm.ComponentModel;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Services;

namespace Jacobi.Midi.SchemaEditor.Models;

public sealed partial class SchemaWorkspaceDocument : ObservableObject
{
    public SchemaWorkspaceDocument(SchemaDocument schema, string source, SchemaFormat format, string? fileName = null)
    {
        Schema = schema;
        Source = source;
        Format = format;
        FileName = fileName;
    }

    public SchemaFormat Format { get; }

    [ObservableProperty]
    public partial SchemaDocument Schema { get; set; }

    [ObservableProperty]
    public partial string Source { get; set; }

    [ObservableProperty]
    public partial string? FileName { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    public string DisplayName => $"{Schema.Name}{(IsDirty ? " *" : String.Empty)}";

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnSchemaChanged(SchemaDocument value) => OnPropertyChanged(nameof(DisplayName));
}
