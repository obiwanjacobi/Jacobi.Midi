using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Xml;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Midi;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.Services;
using YamlDotNet.Core;

namespace Jacobi.Midi.SchemaEditor.ViewModels;

internal sealed partial class MainViewModel : ViewModelBase
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

    /// <summary>
    /// MIDI endpoint/group selection and connection. Disabled when no MIDI API is provided.
    /// </summary>
    public MidiBarViewModel MidiBar { get; init; } = new(null);

    public ObservableCollection<SchemaWorkspaceDocument> Documents { get; } = [];

    public ObservableCollection<SysExMessage> SysExMessages { get; } = [];
    public ObservableCollection<SysExComparisonRow> ComparedBytes { get; } = [];
    public IReadOnlyList<string> DataTypeOptions { get; } = ["sys.uint8", "sys.uint16", "sys.bool", "sys.bytes", "sys.string"];
    public bool HasSelectedDocument => SelectedDocument is not null;

    [ObservableProperty]
    public partial SchemaWorkspaceDocument? SelectedDocument { get; set; }

    /// <summary>
    /// The item selected in the schema tree: an <see cref="EditNode"/> or another document item
    /// (include, data type def, processor def, ...).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNode))]
    [NotifyCanExecuteChangedFor(nameof(DeleteNodeCommand))]
    public partial object? SelectedItem { get; set; }

    /// <summary>
    /// The <see cref="SelectedItem"/> when it is a schema node, otherwise null.
    /// </summary>
    public EditNode? SelectedNode
    {
        get => SelectedItem as EditNode;
        set => SelectedItem = value;
    }

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
    public partial string ReverseFieldDataType { get; set; } = "sys.bytes";

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
        AddDocument(SchemaFileService.CreateDocument(name), format, null, null);
        SelectedDocument!.IsDirty = true;
        StatusMessage = $"Created {name}. Add fields to the SysEx group.";
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
                    AddDocument(SchemaFileService.Parse(source, format), format, file.Name, file);
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
            var node = SelectedNode;
            SelectedDocument.ApplyNodeEdits(
                node,
                NodeName.Trim(),
                NodeDataType.Trim(),
                NodeDescription.Trim(),
                NodeSampleValue.Trim());
            StatusMessage = $"Updated {NodeName}.";
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            StatusMessage = $"Schema edit was not applied: {exception.Message}";
        }
    }

    /// <summary>
    /// Adds a new node of <paramref name="kind"/> with a default name to the target group (see <see cref="GetTargetGroup"/>)
    /// and selects it for editing.
    /// </summary>
    [RelayCommand]
    private void AddNode(SchemaNodeKind kind)
    {
        if (SelectedDocument is null)
        {
            StatusMessage = "Create or open a schema before adding a node.";
            return;
        }

        try
        {
            var parent = GetTargetGroup();
            var name = GetUniqueName(parent, $"New{kind}");
            EditNode node = kind switch
            {
                SchemaNodeKind.Field => SelectedDocument.AddField(parent, name, "sys.uint8", String.Empty),
                SchemaNodeKind.Group => SelectedDocument.AddGroup(parent, name),
                SchemaNodeKind.Repeat => SelectedDocument.AddRepeat(parent, name, count: 1),
                SchemaNodeKind.Choice => SelectedDocument.AddChoice(parent, name, selectedIndex: 0),
                _ => throw new NotSupportedException($"Cannot add a node of kind {kind}.")
            };
            SelectedNode = node;
            StatusMessage = $"Added {name} to {parent.Name}.";
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            StatusMessage = $"Node was not added: {exception.Message}";
        }
    }

    /// <summary>
    /// Removes the selected node and selects its parent group.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteNode))]
    private void DeleteNode()
    {
        if (SelectedDocument is null || SelectedNode is not { } node)
        {
            return;
        }

        try
        {
            var parent = node.Parent is EditDocument ? null : node.Parent;
            SelectedDocument.RemoveNode(node);
            SelectedNode = parent;
            StatusMessage = $"Deleted {node.Name}.";
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            StatusMessage = $"Node was not deleted: {exception.Message}";
        }
    }

    private bool CanDeleteNode() => SelectedNode is not null and not EditDocument;

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
        try
        {
            var field = SelectedDocument.AddField(
                GetTargetGroup(),
                ReverseFieldName.Trim(),
                ReverseFieldDataType.Trim(),
                sample);
            SelectedNode = field;
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
        SelectedItem = null;
    }

    partial void OnSelectedItemChanged(object? item)
    {
        var value = item as EditNode;
        NodeName = value?.Name ?? String.Empty;
        NodeDataType = (value as EditField)?.DataType?.ToString() ?? String.Empty;
        NodeDescription = GetProperty(value, "description");
        NodeSampleValue = GetProperty(value, "sample");
    }

    partial void OnSelectedByteStartChanged(int value) => UpdateByteSelection();
    partial void OnSelectedByteLengthChanged(int value) => UpdateByteSelection();
    partial void OnSelectedSysExMessageChanged(SysExMessage? value) => UpdateByteSelection();

    private void AddDocument(EditDocument schema, SchemaFormat format, string? fileName, IStorageFile? storageFile)
    {
        var document = new SchemaWorkspaceDocument(schema, format, fileName);
        Documents.Add(document);
        if (storageFile is not null)
        {
            _storageFiles[document] = storageFile;
        }
        SelectedDocument = document;
    }

    private async Task SaveSelectedDocument(bool saveAs)
    {
        var document = SelectedDocument;
        if (document is null)
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
                _storageFiles.TryGetValue(document, out file);
            }
            if (file is null)
            {
                var extension = SchemaFileService.Extension(document.Format);
                file = await _storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save MIDI schema",
                    SuggestedFileName = $"{document.Schema.Name}{extension}",
                    DefaultExtension = extension.TrimStart('.'),
                    FileTypeChoices = [GetSchemaFileType(document.Format)]
                });
                if (file is null)
                {
                    return;
                }
            }

            if (SchemaFileService.FromFileName(file.Name) != document.Format)
            {
                StatusMessage = $"The selected file extension must match the document format ({SchemaFileService.Extension(document.Format)}).";
                return;
            }

            var revision = document.Revision;
            var source = SchemaFileService.Serialize(document.Schema, document.Format);
            await using (var stream = await file.OpenWriteAsync())
            {
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                    stream.SetLength(0);
                }
                using var writer = new StreamWriter(stream);
                await writer.WriteAsync(source);
                await writer.FlushAsync();
            }

            _storageFiles[document] = file;
            document.FileName = file.Name;
            if (document.Revision == revision)
            {
                document.IsDirty = false;
            }
            StatusMessage = $"Saved {file.Name}.";
        }
        catch (Exception exception) when (IsSchemaOrFileError(exception))
        {
            StatusMessage = $"Could not save schema: {exception.Message}";
        }
    }

    private EditGroup GetTargetGroup() => SelectedNode switch
    {
        EditGroup group and not EditDocument => group,
        EditField { Parent: { } parent } => parent,
        _ => SelectedDocument?.Schema.Members.OfType<EditGroup>().FirstOrDefault()
            ?? throw new InvalidOperationException("Select a group before adding a node.")
    };

    private static string GetUniqueName(EditGroup parent, string baseName)
    {
        var name = baseName;
        var suffix = 2;
        while (parent.Members.Any(member => String.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{baseName}{suffix++}";
        }
        return name;
    }

    private static string GetProperty(EditNode? node, string name)
        => node?.Properties.FirstOrDefault(property => String.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))?.Value
            ?? String.Empty;

    private static FilePickerFileType GetSchemaFileType(SchemaFormat format) => format switch
    {
        SchemaFormat.Json => SchemaFileTypes[1],
        SchemaFormat.Xml => SchemaFileTypes[2],
        SchemaFormat.Yaml => SchemaFileTypes[3],
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

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
