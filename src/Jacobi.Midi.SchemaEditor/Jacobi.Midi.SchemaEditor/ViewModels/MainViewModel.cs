using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Xml;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Instantiate;
using Jacobi.Midi.SchemaEditor.Midi;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.NodeEditor;
using Jacobi.Midi.SchemaEditor.SchemaTree;
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

    /// <summary>
    /// True when the schema tree shows the document collections, false when it shows the document members.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedItemPath))]
    public partial bool IsDocumentView { get; set; }

    /// <summary>
    /// The document collection whose category header is selected in the schema tree; null when none is selected.
    /// </summary>
    [ObservableProperty]
    public partial DocumentCollection? SelectedCategory { get; set; }

    // The Add menu only offers the items that are valid for the current tree view and selection.
    public bool CanAddMembers => GetTargetGroup() is not null;
    public bool CanAddNodeDef => CanAddTo(DocumentCollection.NodeDefs);
    public bool CanAddDataTypeDef => CanAddTo(DocumentCollection.DataTypeDefs);
    public bool CanAddProcessorDef => CanAddTo(DocumentCollection.ProcessorDefs);
    public bool CanAddInclude => CanAddTo(DocumentCollection.Includes);
    public bool CanAddDocumentProcessor => CanAddTo(DocumentCollection.Processors);
    public bool CanAddDocumentProperty => CanAddTo(DocumentCollection.Properties);
    public bool CanAddDocumentItems => Enum.GetValues<DocumentCollection>().Any(CanAddTo);
    public bool CanAddItemProcessor => ItemDetails?.HasProcessors == true;
    public bool CanAddItemProperty => ItemDetails is not null;
    public bool CanAdd => CanAddMembers || CanAddDocumentItems || CanAddItemProperty;
    public bool ShowAddSeparator => CanAddMembers && CanAddDocumentItems;
    public bool ShowItemEntriesSeparator => (CanAddMembers || CanAddDocumentItems) && CanAddItemProperty;

    /// <summary>
    /// The active tab of the node editor: <see cref="GeneralTab"/>, <see cref="ProcessorsTab"/> or <see cref="PropertiesTab"/>.
    /// </summary>
    [ObservableProperty]
    public partial int SelectedEditorTab { get; set; }

    public const int GeneralTab = 0;
    public const int ProcessorsTab = 1;
    public const int PropertiesTab = 2;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstantiateSchemaCommand))]
    public partial SchemaWorkspaceDocument? SelectedDocument { get; set; }

    /// <summary>
    /// The open instantiate dialog (shown as an overlay); null when there is none.
    /// </summary>
    [ObservableProperty]
    public partial InstantiateSchemaViewModel? InstantiateDialog { get; private set; }

    /// <summary>
    /// The instance that the user confirmed in the last instantiate dialog; null when there is none yet.
    /// </summary>
    [ObservableProperty]
    public partial InstanceNode? LastInstance { get; private set; }

    /// <summary>
    /// The item selected in the schema tree: an <see cref="EditNode"/> or another document item
    /// (include, data type def, processor def, ...).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNode))]
    [NotifyPropertyChangedFor(nameof(SelectedItemPath))]
    [NotifyCanExecuteChangedFor(nameof(DeleteItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstantiateSchemaCommand))]
    public partial object? SelectedItem { get; set; }

    /// <summary>
    /// The path of the <see cref="SelectedItem"/> in the schema tree, starting with the document name:
    /// <c>Name/Header/Id</c> for a member, <c>Name/NodeDefs/Header</c> or <c>Name/Includes/Schema</c> for a document item.
    /// </summary>
    public string SelectedItemPath => GetItemPath(SelectedItem);

    private string GetItemPath(object? item)
    {
        var root = SelectedDocument?.Schema.Name ?? String.Empty;
        if (item is null or EditDocument)
        {
            return root;
        }

        if (item is EditNode node)
        {
            var names = new List<string>();
            for (EditNode? current = node; current is not null and not EditDocument; current = current.Parent)
            {
                names.Insert(0, current.Name);
            }

            // node defs are listed under their collection in the document view
            var path = String.Join('/', names);
            return SchemaTreeViewModel.IsDocumentItem(node) ? $"{root}/{nameof(EditDocument.NodeDefs)}/{path}" : $"{root}/{path}";
        }

        var (category, name) = item switch
        {
            EditDocumentRef include => (nameof(EditDocument.Includes), include.Schema),
            EditDataTypeDef dataTypeDef => (nameof(EditDocument.DataTypeDefs), dataTypeDef.Name),
            EditProcessorDef processorDef => (nameof(EditDocument.ProcessorDefs), processorDef.Name),
            EditProcessorRef processor => (nameof(EditDocument.Processors), processor.Processor.ToString()),
            EditProperty property => (nameof(EditDocument.Properties), property.Name),
            _ => (String.Empty, String.Empty)
        };
        return category.Length == 0 ? root : $"{root}/{category}/{name}";
    }

    /// <summary>
    /// The <see cref="SelectedItem"/> when it is a schema node, otherwise null.
    /// </summary>
    public EditNode? SelectedNode
    {
        get => SelectedItem as EditNode;
        set => SelectedItem = value;
    }

    /// <summary>
    /// The effective processors and properties of the <see cref="SelectedItem"/>;
    /// null when nothing is selected or the item has no processors or properties.
    /// </summary>
    [ObservableProperty]
    public partial ItemDetailsViewModel? ItemDetails { get; private set; }

    /// <summary>
    /// The General tab content: the <see cref="SelectedNode"/> (edited through the staged node values of this view model)
    /// or an <see cref="NodeEditor.ItemEditor"/> for the other document items.
    /// </summary>
    [ObservableProperty]
    public partial object? GeneralEditor { get; private set; }

    [ObservableProperty]
    public partial string NodeName { get; set; } = String.Empty;

    [ObservableProperty]
    public partial string NodeDataType { get; set; } = "sys.uint8";

    [ObservableProperty]
    public partial string NodeDescription { get; set; } = String.Empty;

    [ObservableProperty]
    public partial string NodeSampleValue { get; set; } = String.Empty;

    /// <summary>
    /// The staged type-specific properties of the <see cref="SelectedNode"/> (nodeDef, count, selectedIndex, ...).
    /// </summary>
    [ObservableProperty]
    public partial NodeFacets NodeFacets { get; set; } = new();

    public IReadOnlyList<string> PipelineOptions => NodeFacets.PipelineOptions;

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
                NodeSampleValue.Trim(),
                NodeFacets);
            NodeFacets = NodeFacets.Read(node);
            OnPropertyChanged(nameof(SelectedItemPath));
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
            if (GetTargetGroup() is not { } parent)
            {
                StatusMessage = "Select a group (or a field in it) before adding a node.";
                return;
            }
            var name = GetUniqueName(parent.Members.Select(member => member.Name), $"New{kind}");
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
    /// Adds a top-level node definition of <paramref name="kind"/> and selects it.
    /// </summary>
    [RelayCommand]
    private void AddNodeDef(SchemaNodeKind kind)
        => AddDocumentItem("node def", document => document.AddNodeDef(kind,
            GetUniqueName(document.Schema.NodeDefs.Select(definition => definition.Name), $"New{kind}Def")));

    [RelayCommand]
    private void AddInclude()
        => AddDocumentItem("include", document => document.AddInclude(
            GetUniqueName(document.Schema.Includes.Select(include => include.Schema), "schema")));

    [RelayCommand]
    private void AddDataTypeDef()
        => AddDocumentItem("data type def", document => document.AddDataTypeDef(
            GetUniqueName(document.Schema.DataTypeDefs.Select(definition => definition.Name), "NewDataType"), "sys.uint8"));

    [RelayCommand]
    private void AddProcessorDef()
        => AddDocumentItem("processor def", document => document.AddProcessorDef(
            GetUniqueName(document.Schema.ProcessorDefs.Select(definition => definition.Name), "NewProcessor"), "namespace.id"));

    [RelayCommand]
    private void AddDocumentProcessor()
        => AddDocumentItem("processor", document => document.AddProcessor("namespace.id"));

    [RelayCommand]
    private void AddDocumentProperty()
        => AddDocumentItem("property", document => document.AddProperty(
            GetUniqueName(document.Schema.Properties.Select(property => property.Name), "property"), null));

    /// <summary>
    /// Adds an own processor to the selected item and shows the Processors tab.
    /// </summary>
    [RelayCommand]
    private void AddItemProcessor()
    {
        if (ItemDetails is { HasProcessors: true } details)
        {
            details.AddProcessorCommand.Execute(null);
            SelectedEditorTab = ProcessorsTab;
        }
    }

    /// <summary>
    /// Adds an own property to the selected item and shows the Properties tab.
    /// </summary>
    [RelayCommand]
    private void AddItemProperty()
    {
        if (ItemDetails is { } details)
        {
            details.AddPropertyCommand.Execute(null);
            SelectedEditorTab = details.HasPropertiesTab ? PropertiesTab : GeneralTab;
        }
    }

    /// <summary>
    /// Removes the selected node or document item. A removed member selects its parent group.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteItem))]
    private void DeleteItem()
    {
        if (SelectedDocument is null || SelectedItem is not { } item)
        {
            return;
        }

        try
        {
            var parent = item is EditNode { Parent: { } group and not EditDocument } ? group : null;
            SelectedDocument.RemoveItem(item);
            SelectedItem = parent;
            StatusMessage = "Deleted the selected item.";
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            StatusMessage = $"Item was not deleted: {exception.Message}";
        }
    }

    private bool CanDeleteItem() => SelectedItem is not null and not EditDocument;

    /// <summary>
    /// Opens the instantiate dialog for the selected document root. The document and the documents it includes
    /// (taken from the open documents) are compiled first; when that fails the problem is reported and no dialog opens.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstantiateSchema))]
    private void InstantiateSchema()
    {
        if (SelectedDocument is null || SelectedItem is not EditGroup root)
        {
            return;
        }

        try
        {
            var instantiator = SchemaInstantiator.Create(SelectedDocument.Schema, Documents.Select(document => document.Schema));
            InstantiateDialog = new InstantiateSchemaViewModel(instantiator.Create(root), CloseInstantiateDialog);
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            // the error is shown in the dialog: the status bar is easily overlooked
            StatusMessage = $"Cannot instantiate {root.Name}: {exception.Message}";
            InstantiateDialog = new InstantiateSchemaViewModel(root.Name, exception.Message, CloseInstantiateDialog);
        }
    }

    private bool CanInstantiateSchema()
        => SelectedDocument is not null && SelectedItem is EditGroup and not EditDocument;

    private void CloseInstantiateDialog(InstantiateSchemaViewModel dialog, bool confirmed)
    {
        InstantiateDialog = null;
        if (confirmed && dialog.Root is { } instance)
        {
            LastInstance = instance;
            StatusMessage = $"Instantiated {instance.Name} with {instance.Fields().Count()} field value(s).";
        }
    }

    private void AddDocumentItem(string what, Func<SchemaWorkspaceDocument, object> add)
    {
        if (SelectedDocument is null)
        {
            StatusMessage = $"Create or open a schema before adding a {what}.";
            return;
        }

        try
        {
            SelectedItem = add(SelectedDocument);
            StatusMessage = $"Added a {what} to {SelectedDocument.Schema.Name}.";
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            StatusMessage = $"The {what} was not added: {exception.Message}";
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
        try
        {
            var field = SelectedDocument.AddField(
                GetTargetGroup() ?? throw new InvalidOperationException("Select a group before adding a node."),
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
        NotifyAddOptionsChanged();
    }

    partial void OnIsDocumentViewChanged(bool value)
    {
        // a selected item that is not shown in the other view would otherwise stay selected (and keep its path)
        if (SelectedItem is { } item && SchemaTreeViewModel.IsDocumentItem(item) != value)
        {
            SelectedItem = null;
        }

        NotifyAddOptionsChanged();
    }
    partial void OnSelectedCategoryChanged(DocumentCollection? value) => NotifyAddOptionsChanged();

    partial void OnSelectedItemChanged(object? value)
    {
        var node = value as EditNode;
        ItemDetails?.Dispose();
        ItemDetails = SelectedDocument is null ? null : ItemDetailsViewModel.Create(SelectedDocument, value);
        GeneralEditor = (object?)node ?? (SelectedDocument is null ? null : ItemEditor.Create(SelectedDocument, value));
        NodeName = node?.Name ?? String.Empty;
        NodeDataType = (node as EditField)?.DataType?.ToString() ?? String.Empty;
        NodeDescription = GetProperty(node, "description");
        NodeSampleValue = GetProperty(node, "sample");
        NodeFacets = NodeFacets.Read(node);
        // a tab the new selection does not have would stay selected (but hidden)
        if ((SelectedEditorTab == ProcessorsTab && !CanAddItemProcessor) || (SelectedEditorTab == PropertiesTab && !CanAddItemProperty))
        {
            SelectedEditorTab = GeneralTab;
        }
        NotifyAddOptionsChanged();
    }

    partial void OnSelectedByteStartChanged(int value) => UpdateByteSelection();
    partial void OnSelectedByteLengthChanged(int value) => UpdateByteSelection();
    partial void OnSelectedSysExMessageChanged(SysExMessage? value) => UpdateByteSelection();

    private void AddDocument(EditDocument schema, SchemaFormat format, string? fileName, IStorageFile? storageFile)
    {
        var document = new SchemaWorkspaceDocument(schema, format, fileName);
        Documents.Add(document);
        // MarkChanged raises DisplayName; includes or names may have changed
        document.PropertyChanged += (_, _) => RefreshIncludeStatus();
        RefreshIncludeStatus();
        if (storageFile is not null)
        {
            _storageFiles[document] = storageFile;
        }
        SelectedDocument = document;
    }

    /// <summary>
    /// Flags the includes of all open documents whose included schema is not loaded as another open document.
    /// </summary>
    private void RefreshIncludeStatus()
    {
        foreach (var document in Documents)
        {
            foreach (var include in document.Schema.Includes)
            {
                include.IsLoaded = Documents.Any(other => !ReferenceEquals(other, document)
                    && String.Equals(other.Schema.Name, include.Schema, StringComparison.OrdinalIgnoreCase));
            }
        }
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

    /// <summary>
    /// The group a new member node is added to: the selected group, or the parent group of the selected field.
    /// Without a selection in the Members view it is the first top-level group.
    /// Null when nodes cannot be added: an open choice, a top-level node def field, or a non-node selection.
    /// </summary>
    private EditGroup? GetTargetGroup()
    {
        if (SelectedDocument is null)
        {
            return null;
        }
        var group = SelectedItem switch
        {
            EditDocument => null,
            EditGroup selected => selected,
            EditField field => field.Parent,
            null when !IsDocumentView => SelectedDocument.Schema.Members.OfType<EditGroup>().FirstOrDefault(),
            _ => null
        };
        return group is EditDocument or EditChoice { MemberNodeDef: not null } ? null : group;
    }

    /// <summary>
    /// Document collection items can be added in the Document view: to the selected category,
    /// to the collection of the selected item, or to any collection when nothing is selected.
    /// </summary>
    private bool CanAddTo(DocumentCollection collection)
        => SelectedDocument is not null && IsDocumentView
            && (SelectedCategory ?? GetCollection(SelectedItem) ?? collection) == collection;

    private static DocumentCollection? GetCollection(object? item) => item switch
    {
        null => null,
        EditNode node => DocumentCollection.NodeDefs,
        EditDocumentRef => DocumentCollection.Includes,
        EditDataTypeDef => DocumentCollection.DataTypeDefs,
        EditProcessorDef => DocumentCollection.ProcessorDefs,
        EditProcessorRef => DocumentCollection.Processors,
        EditProperty => DocumentCollection.Properties,
        _ => null
    };

    private void NotifyAddOptionsChanged()
    {
        foreach (var name in (string[])[nameof(CanAddMembers), nameof(CanAddNodeDef), nameof(CanAddDataTypeDef),
            nameof(CanAddProcessorDef), nameof(CanAddInclude), nameof(CanAddDocumentProcessor), nameof(CanAddDocumentProperty),
            nameof(CanAddDocumentItems), nameof(CanAddItemProcessor), nameof(CanAddItemProperty), nameof(CanAdd),
            nameof(ShowAddSeparator), nameof(ShowItemEntriesSeparator)])
        {
            OnPropertyChanged(name);
        }
    }

    private static string GetUniqueName(IEnumerable<string> existingNames, string baseName)
    {
        var names = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = baseName;
        var suffix = 2;
        while (names.Contains(name))
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
