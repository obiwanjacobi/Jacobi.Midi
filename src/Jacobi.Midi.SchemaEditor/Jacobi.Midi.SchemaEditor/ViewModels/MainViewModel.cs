using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Xml;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.Services;
using YamlDotNet.Core;

namespace Jacobi.Midi.SchemaEditor.ViewModels;

public sealed partial class MainViewModel : ViewModelBase
{
    private static readonly FilePickerFileType[] SchemaFileTypes =
    [
        new("MIDI schema files") { Patterns = ["*.json", "*.xml", "*.yaml", "*.yml"] },
        new("JSON schema") { Patterns = ["*.json"] },
        new("XML schema") { Patterns = ["*.xml"] },
        new("YAML schema") { Patterns = ["*.yaml", "*.yml"] }
    ];

    private readonly Dictionary<SchemaWorkspaceDocument, IStorageFile> _storageFiles = [];
    private IStorageProvider? _storageProvider;
    private IClipboard? _clipboard;

    public ObservableCollection<SchemaWorkspaceDocument> Documents { get; } = [];
    public ObservableCollection<SchemaTreeItem> SchemaNodes { get; } = [];
    public ObservableCollection<SysExMessage> SysExMessages { get; } = [];
    public ObservableCollection<SysExComparisonRow> ComparedBytes { get; } = [];
    public IReadOnlyList<string> DataTypeOptions { get; } = ["sys.uint8", "sys.uint16", "sys.bool", "sys.bytes", "sys.string"];
    public bool HasSelectedDocument => SelectedDocument is not null;

    [ObservableProperty]
    public partial SchemaWorkspaceDocument? SelectedDocument { get; set; }

    [ObservableProperty]
    public partial SchemaTreeItem? SelectedNode { get; set; }

    [ObservableProperty]
    public partial string NodeName { get; set; } = String.Empty;

    [ObservableProperty]
    public partial string NodeDataType { get; set; } = "sys.uint8";

    [ObservableProperty]
    public partial string NodeDescription { get; set; } = String.Empty;

    [ObservableProperty]
    public partial string NodeSampleValue { get; set; } = String.Empty;

    [ObservableProperty]
    public partial string HexInput { get; set; } = String.Empty;

    [ObservableProperty]
    public partial string ReverseFieldName { get; set; } = "CapturedBytes";

    [ObservableProperty]
    public partial string NewFieldName { get; set; } = "NewField";

    [ObservableProperty]
    public partial string NewGroupName { get; set; } = "NewGroup";

    [ObservableProperty]
    public partial string ReverseFieldDataType { get; set; } = "sys.bytes";

    [ObservableProperty]
    public partial string NewFieldDataType { get; set; } = "sys.uint8";

    [ObservableProperty]
    public partial string NewFieldSampleValue { get; set; } = String.Empty;

    [ObservableProperty]
    public partial int SelectedByteStart { get; set; }

    [ObservableProperty]
    public partial int SelectedByteLength { get; set; } = 1;

    [ObservableProperty]
    public partial SysExMessage? SelectedSysExMessage { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Create a schema or open an existing JSON, XML, or YAML file.";

    public void SetStorageProvider(IStorageProvider storageProvider, IClipboard? clipboard)
    {
        _storageProvider = storageProvider;
        _clipboard = clipboard;
    }

    [RelayCommand]
    private void NewDocument()
    {
        var name = "NewSchema";
        var suffix = 2;
        while (Documents.Any(document => String.Equals(document.Schema.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"NewSchema{suffix++}";
        }

        var format = SchemaFormat.Json;
        var source = SchemaFileService.CreateTemplate(name, format);
        AddDocument(source, format, null, null);
        StatusMessage = $"Created {name}. Add fields to the SysEx group or import a message below.";
    }

    [RelayCommand]
    private async Task OpenFiles()
    {
        if (_storageProvider is null || !_storageProvider.CanOpen)
        {
            StatusMessage = "This platform does not provide file-open access.";
            return;
        }

        try
        {
            var files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open MIDI schema files",
                AllowMultiple = true,
                FileTypeFilter = SchemaFileTypes
            });

            foreach (var file in files)
            {
                try
                {
                    var format = SchemaFileService.FromFileName(file.Name);
                    await using var stream = await file.OpenReadAsync();
                    using var reader = new StreamReader(stream);
                    var source = await reader.ReadToEndAsync();
                    AddDocument(source, format, file.Name, file);
                    StatusMessage = $"Opened {file.Name}.";
                }
                catch (Exception exception) when (IsSchemaOrFileError(exception))
                {
                    StatusMessage = $"Could not open {file.Name}: {exception.Message}";
                }
            }
        }
        catch (Exception exception) when (IsStorageError(exception))
        {
            StatusMessage = $"Could not open schema files: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveDocument()
    {
        await SaveSelectedDocument(saveAs: false);
    }

    [RelayCommand]
    private async Task SaveDocumentAs()
    {
        await SaveSelectedDocument(saveAs: true);
    }

    [RelayCommand]
    private void ApplyNodeEdits()
    {
        if (SelectedDocument is null || SelectedNode is null)
        {
            StatusMessage = "Select a schema node before editing its properties.";
            return;
        }

        try
        {
            var path = SelectedNode.MemberPath;
            var source = SchemaFileService.UpdateNode(
                SelectedDocument.Source,
                SelectedDocument.Format,
                path,
                NodeName.Trim(),
                NodeDataType.Trim(),
                NodeDescription.Trim(),
                NodeSampleValue.Trim());
            UpdateSelectedDocument(source, path);
            StatusMessage = $"Updated {NodeName}.";
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            StatusMessage = $"Schema edit was not applied: {exception.Message}";
        }
    }

    [RelayCommand]
    private void AddField()
    {
        if (SelectedDocument is null)
        {
            StatusMessage = "Create or open a schema before adding a field.";
            return;
        }

        try
        {
            var groupPath = SelectedNode is null
                ? []
                : String.Equals(SelectedNode.Kind, "Field", StringComparison.OrdinalIgnoreCase)
                    ? SelectedNode.MemberPath[..^1]
                    : SelectedNode.MemberPath;
            var source = SchemaFileService.AddField(
                SelectedDocument.Source,
                SelectedDocument.Format,
                groupPath,
                NewFieldName.Trim(),
                NewFieldDataType.Trim(),
                NewFieldSampleValue.Trim());
            var newPath = GetAppendedNodePath(groupPath);
            UpdateSelectedDocument(source, newPath);
            StatusMessage = $"Added {NewFieldName} to {SelectedDocument.Schema.Name}.";
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            StatusMessage = $"Field was not added: {exception.Message}";
        }
    }

    [RelayCommand]
    private void AddGroup()
    {
        if (SelectedDocument is null)
        {
            StatusMessage = "Create or open a schema before adding a group.";
            return;
        }

        try
        {
            var groupPath = SelectedNode is null
                ? []
                : String.Equals(SelectedNode.Kind, "Field", StringComparison.OrdinalIgnoreCase)
                    ? SelectedNode.MemberPath[..^1]
                    : SelectedNode.MemberPath;
            var source = SchemaFileService.AddGroup(
                SelectedDocument.Source,
                SelectedDocument.Format,
                groupPath,
                NewGroupName.Trim());
            var newPath = GetAppendedNodePath(groupPath);
            UpdateSelectedDocument(source, newPath);
            StatusMessage = $"Added group {NewGroupName} to {SelectedDocument.Schema.Name}.";
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            StatusMessage = $"Group was not added: {exception.Message}";
        }
    }

    [RelayCommand]
    private void ImportSysEx()
    {
        var tokens = HexInput
            .Replace(",", " ", StringComparison.Ordinal)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            StatusMessage = "Enter a space- or comma-separated SysEx byte sequence first.";
            return;
        }

        var bytes = new byte[tokens.Length];
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? tokens[index][2..] : tokens[index];
            if (!Byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[index]))
            {
                StatusMessage = $"'{tokens[index]}' is not a two-digit hexadecimal byte.";
                return;
            }
        }

        var message = new SysExMessage(bytes);
        SysExMessages.Add(message);
        SelectedSysExMessage = message;
        if (SysExMessages.Count == 1)
        {
            SelectedByteStart = message.HasStartMarker ? 1 : 0;
            SelectedByteLength = Math.Max(1, message.Bytes.Length - (message.HasStartMarker ? 1 : 0) - (message.HasEndMarker ? 1 : 0));
        }
        UpdateByteSelection();
        UpdateComparison();
        StatusMessage = $"Imported {bytes.Length} bytes. {message.BoundarySummary}.";
    }

    [RelayCommand]
    private async Task ExportSysEx()
    {
        if (SysExMessages.Count == 0)
        {
            StatusMessage = "Import a SysEx message before exporting it.";
            return;
        }
        if (_clipboard is null)
        {
            StatusMessage = $"SysEx bytes: {SysExMessages[0].Hex}";
            return;
        }

        try
        {
            await _clipboard.SetTextAsync(SysExMessages[0].Hex);
            StatusMessage = "Copied the first SysEx message as hexadecimal text.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            StatusMessage = $"Could not copy SysEx bytes: {exception.Message}";
        }
    }

    [RelayCommand]
    private void AddSelectedBytes()
    {
        if (SelectedSysExMessage is null || SelectedDocument is null)
        {
            StatusMessage = "Import a SysEx message and open a schema before isolating bytes.";
            return;
        }

        var bytes = SelectedSysExMessage.Bytes;
        if (SelectedByteStart < 0 || SelectedByteLength < 1 || SelectedByteStart + SelectedByteLength > bytes.Length)
        {
            StatusMessage = "The selected byte range is outside the imported message.";
            return;
        }
        if (Enumerable.Range(SelectedByteStart, SelectedByteLength).Any(index => SelectedSysExMessage.ByteCells[index].IsStart || SelectedSysExMessage.ByteCells[index].IsEnd))
        {
            StatusMessage = "F0 and F7 delimiters are detected automatically; select only message data bytes.";
            return;
        }

        var selectedBytes = bytes.AsSpan(SelectedByteStart, SelectedByteLength).ToArray();
        var sample = String.Join(' ', selectedBytes.Select(value => value.ToString("X2")));
        var groupPath = SelectedNode is null
            ? []
            : String.Equals(SelectedNode.Kind, "Field", StringComparison.OrdinalIgnoreCase)
                ? SelectedNode.MemberPath[..^1]
                : SelectedNode.MemberPath;

        try
        {
            var source = SchemaFileService.AddField(
                SelectedDocument.Source,
                SelectedDocument.Format,
                groupPath,
                ReverseFieldName.Trim(),
                ReverseFieldDataType.Trim(),
                sample);
            var newPath = GetAppendedNodePath(groupPath);
            UpdateSelectedDocument(source, newPath);
            StatusMessage = $"Added {ReverseFieldName} from bytes {SelectedByteStart}–{SelectedByteStart + SelectedByteLength - 1}.";
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            StatusMessage = $"Selected bytes were not added: {exception.Message}";
        }
    }

    public void SelectByte(int index)
    {
        SelectedByteStart = index;
        SelectedByteLength = 1;
        UpdateByteSelection();
    }

    partial void OnSelectedDocumentChanged(SchemaWorkspaceDocument? value)
    {
        OnPropertyChanged(nameof(HasSelectedDocument));
        SelectedNode = null;
        SchemaNodes.Clear();
        if (value is not null)
        {
            AddSchemaNodes(value.Schema.Members, []);
        }
    }

    partial void OnSelectedNodeChanged(SchemaTreeItem? value)
    {
        NodeName = value?.Name ?? String.Empty;
        NodeDataType = value is null || String.IsNullOrEmpty(value.DataType) ? "sys.uint8" : value.DataType;
        NodeDescription = value?.Description ?? String.Empty;
        NodeSampleValue = value?.SampleValue ?? String.Empty;
    }

    partial void OnSelectedByteStartChanged(int value) => UpdateByteSelection();
    partial void OnSelectedByteLengthChanged(int value) => UpdateByteSelection();
    partial void OnSelectedSysExMessageChanged(SysExMessage? value) => UpdateByteSelection();

    private void AddDocument(string source, SchemaFormat format, string? fileName, IStorageFile? storageFile)
    {
        var schema = SchemaFileService.Parse(source, format);
        var document = new SchemaWorkspaceDocument(schema, source, format, fileName);
        Documents.Add(document);
        if (storageFile is not null)
        {
            _storageFiles[document] = storageFile;
        }
        SelectedDocument = document;
    }

    private async Task SaveSelectedDocument(bool saveAs)
    {
        if (SelectedDocument is null)
        {
            StatusMessage = "Create or open a schema before saving.";
            return;
        }
        if (_storageProvider is null || !_storageProvider.CanSave)
        {
            StatusMessage = "This platform does not provide file-save access.";
            return;
        }

        try
        {
            IStorageFile? file = null;
            if (!saveAs)
            {
                _storageFiles.TryGetValue(SelectedDocument, out file);
            }
            if (file is null)
            {
                var extension = SchemaFileService.Extension(SelectedDocument.Format);
                file = await _storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save MIDI schema",
                    SuggestedFileName = $"{SelectedDocument.Schema.Name}{extension}",
                    DefaultExtension = extension.TrimStart('.'),
                    FileTypeChoices = [GetSchemaFileType(SelectedDocument.Format)]
                });
                if (file is null)
                {
                    return;
                }
            }

            if (SchemaFileService.FromFileName(file.Name) != SelectedDocument.Format)
            {
                StatusMessage = $"The selected file extension must match the document format ({SchemaFileService.Extension(SelectedDocument.Format)}).";
                return;
            }

            SchemaFileService.Parse(SelectedDocument.Source, SelectedDocument.Format);
            await using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(SelectedDocument.Source);
            await writer.FlushAsync();

            _storageFiles[SelectedDocument] = file;
            SelectedDocument.FileName = file.Name;
            SelectedDocument.IsDirty = false;
            StatusMessage = $"Saved {file.Name}.";
        }
        catch (Exception exception) when (IsSchemaOrFileError(exception))
        {
            StatusMessage = $"Could not save schema: {exception.Message}";
        }
    }

    private int[] GetAppendedNodePath(IReadOnlyList<int> groupPath)
    {
        var parent = groupPath.Count == 0
            ? SchemaNodes.FirstOrDefault()
            : FindNode(SchemaNodes, groupPath);
        if (parent is null)
        {
            throw new InvalidOperationException("The target group could not be found after editing.");
        }
        return [.. parent.MemberPath, parent.Children.Count];
    }

    private static FilePickerFileType GetSchemaFileType(SchemaFormat format) => format switch
    {
        SchemaFormat.Json => SchemaFileTypes[1],
        SchemaFormat.Xml => SchemaFileTypes[2],
        SchemaFormat.Yaml => SchemaFileTypes[3],
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private void UpdateSelectedDocument(string source, int[]? selectedPath)
    {
        if (SelectedDocument is null)
        {
            return;
        }

        var schema = SchemaFileService.Parse(source, SelectedDocument.Format);
        SelectedDocument.Source = source;
        SelectedDocument.Schema = schema;
        SelectedDocument.IsDirty = true;

        SchemaNodes.Clear();
        AddSchemaNodes(schema.Members, []);
        SelectedNode = selectedPath is null ? null : FindNode(SchemaNodes, selectedPath);
    }

    private void AddSchemaNodes(IReadOnlyList<SchemaNode> nodes, int[] parentPath)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            SchemaNodes.Add(new SchemaTreeItem(nodes[index], [.. parentPath, index], 0));
        }
    }

    private static SchemaTreeItem? FindNode(IEnumerable<SchemaTreeItem> roots, IReadOnlyList<int> path)
    {
        var current = roots.FirstOrDefault(node => node.MemberPath.Length > 0 && node.MemberPath[0] == path[0]);
        for (var depth = 1; current is not null && depth < path.Count; depth++)
        {
            current = current.Children.FirstOrDefault(node => node.MemberPath[depth] == path[depth]);
        }
        return current;
    }

    private void UpdateByteSelection()
    {
        if (SelectedSysExMessage is null)
        {
            return;
        }

        foreach (var cell in SelectedSysExMessage.ByteCells)
        {
            cell.IsSelected = cell.Index >= SelectedByteStart
                && cell.Index < SelectedByteStart + Math.Max(SelectedByteLength, 0);
        }
    }

    private void UpdateComparison()
    {
        ComparedBytes.Clear();
        if (SysExMessages.Count < 2)
        {
            return;
        }

        var first = SysExMessages[0].Bytes;
        var second = SysExMessages[1].Bytes;
        for (var index = 0; index < Math.Max(first.Length, second.Length); index++)
        {
            var left = index < first.Length ? first[index].ToString("X2") : "—";
            var right = index < second.Length ? second[index].ToString("X2") : "—";
            ComparedBytes.Add(new SysExComparisonRow
            {
                Index = index,
                Left = left,
                Right = right,
                IsDifferent = !String.Equals(left, right, StringComparison.Ordinal)
            });
        }
    }

    private static bool IsSchemaOrFileError(Exception exception)
        => IsSchemaError(exception) || IsStorageError(exception);

    private static bool IsSchemaError(Exception exception)
        => exception is ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or JsonException
            or XmlException
            or YamlException;

    private static bool IsStorageError(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException;
}
