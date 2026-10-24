using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Xml;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.BinarySerializer.Descriptors;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Assemblies;
using Jacobi.Midi.SchemaEditor.Document;
using Jacobi.Midi.SchemaEditor.Instantiate;
using Jacobi.Midi.SchemaEditor.Midi;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.NodeEditor;
using Jacobi.Midi.SchemaEditor.SchemaTree;
using Jacobi.Midi.SchemaEditor.Services;
using Jacobi.Midi.SchemaEditor.Workbench;
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

    private static readonly FilePickerFileType[] AssemblyFileTypes =
    [
        new("Assemblies") { Patterns = ["*.dll"] }
    ];

    private static readonly FilePickerFileType[] SysExFileTypes =
    [
        new("SysEx files") { Patterns = ["*.syx"] }
    ];

    private static readonly FilePickerFileType[] SessionFileTypes =
    [
        new("Workbench sessions") { Patterns = ["*" + WorkbenchSessionFile.Extension] },
        new("JSON files") { Patterns = ["*.json"] }
    ];

    private readonly Dictionary<SchemaWorkspaceDocument, IStorageFile> _storageFiles = [];
    private IStorageProvider? _storageProvider;
    private IClipboard? _clipboard;

    /// <summary>
    /// MIDI endpoint/group selection and connection. Disabled when no MIDI API is provided.
    /// </summary>
    public MidiBarViewModel MidiBar { get; init; } = new(null);

    public ObservableCollection<SchemaWorkspaceDocument> Documents { get; } = [];

    /// <summary>
    /// The documents shown in the schema tree, in the order of <see cref="Documents"/>. A document stays in the tree
    /// until a document that is not in the tree is selected: that replaces all unpinned documents in the tree.
    /// Changing the pin state does not change the tree.
    /// </summary>
    public ObservableCollection<EditDocument> TreeDocuments { get; } = [];

    // the open documents shown in the schema tree
    private readonly HashSet<SchemaWorkspaceDocument> _treeDocuments = [];
    // set while a selection in the schema tree switches the selected document: the selection is kept
    private bool _keepSelectedItem;
    // set while a newly selected document switches to the schema tab: its root is selected instead of the first in the tree
    private bool _selectingDocument;

    /// <summary>
    /// The opened assemblies with their embedded schemas (not editable, not part of <see cref="Documents"/>) and processors.
    /// </summary>
    public AssemblyWorkspace OpenedAssemblies { get; } = new();

    /// <summary>
    /// The properties the processors of the <see cref="OpenedAssemblies"/> publish; the node editor checks processor properties against them.
    /// </summary>
    private ProcessorCatalog ProcessorCatalog { get; }

    /// <summary>
    /// All loaded schemas: the <see cref="Documents"/> and the embedded schemas of the <see cref="OpenedAssemblies"/>.
    /// Resolves the schema names (includes, node defs) and compiles the schemas for Instantiate, Match and the workbench.
    /// </summary>
    public SchemaSetWorkspace LoadedSchemas { get; }

    public MainViewModel()
    {
        ProcessorCatalog = new ProcessorCatalog(OpenedAssemblies.Processors);
        LoadedSchemas = new SchemaSetWorkspace(Documents, OpenedAssemblies);
        // a schema that was opened, closed or renamed may replace (or no longer replace) another one with its name
        LoadedSchemas.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(DocumentSourceNote));
            OnPropertyChanged(nameof(HasDocumentSourceNote));
        };
        // pre-loaded: the 'sys' processors of the serializer, and the base 'midi' schema and 'midi' processors
        // that the vendor and device schemas build on
        foreach (var assembly in (Assembly[])[typeof(Jacobi.BinarySerializer.Processors.ProcessorFactory).Assembly,
            typeof(Jacobi.Midi.Device.Processors.ProcessorFactory).Assembly])
        {
            OpenedAssemblies.Load(assembly, Path.GetFileName(assembly.Location));
        }
    }

    /// <summary>
    /// The active tab of the left panel: <see cref="SchemaLeftTab"/> (the schema tree) or <see cref="AssembliesLeftTab"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDocument), nameof(IsReadOnly), nameof(CanPin), nameof(DataTypeOptions), nameof(SelectedItemPath))]
    public partial int SelectedLeftTab { get; set; }

    public const int SchemaLeftTab = 0;
    public const int AssembliesLeftTab = 1;

    /// <summary>
    /// The item selected in the assembly tree: a <see cref="LoadedAssembly"/>, a <see cref="LoadedSchema"/>,
    /// a node of an embedded schema or a <see cref="ProcessorNamespaceItem"/>; null when none is selected.
    /// </summary>
    [ObservableProperty]
    public partial object? SelectedAssemblyItem { get; set; }

    /// <summary>
    /// The read-only document of the embedded schema selected in the assembly tree; null when none is selected.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDocument), nameof(IsReadOnly), nameof(CanPin), nameof(DataTypeOptions))]
    public partial SchemaWorkspaceDocument? InspectedDocument { get; private set; }

    /// <summary>
    /// The assembly whose details are shown instead of the node editor; null unless an assembly is selected.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNodeEditor))]
    public partial LoadedAssembly? InspectedAssembly { get; private set; }

    /// <summary>
    /// The processor namespace whose details (its processors) are shown instead of the node editor; null unless one is selected.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNodeEditor))]
    public partial ProcessorNamespaceItem? InspectedNamespace { get; private set; }

    public bool ShowNodeEditor => InspectedAssembly is null && InspectedNamespace is null;

    /// <summary>
    /// The document the node editor shows: on the assemblies tab the read-only <see cref="InspectedDocument"/> of an embedded schema
    /// (null when none is selected), otherwise the <see cref="SelectedDocument"/>.
    /// </summary>
    public SchemaWorkspaceDocument? ActiveDocument => SelectedLeftTab == AssembliesLeftTab ? InspectedDocument : SelectedDocument;

    /// <summary>
    /// True when the node editor shows an embedded schema: it cannot be changed.
    /// </summary>
    public bool IsReadOnly => ActiveDocument?.IsReadOnly == true;

    /// <summary>
    /// True when the <see cref="SelectedDocument"/> can be pinned to the schema tree: only on the schema tab.
    /// </summary>
    public bool CanPin => SelectedLeftTab == SchemaLeftTab && SelectedDocument is not null;

    /// <summary>
    /// The mode of the main window: <see cref="SchemaMode"/> (author schemas) or <see cref="WorkbenchMode"/>
    /// (reverse engineer the dumps of a device in the <see cref="Workbench"/>).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSchemaMode), nameof(IsWorkbenchMode))]
    public partial int SelectedMode { get; set; }

    public const int SchemaMode = 0;
    public const int WorkbenchMode = 1;

    public bool IsSchemaMode => SelectedMode != WorkbenchMode;
    public bool IsWorkbenchMode => SelectedMode == WorkbenchMode;

    /// <summary>
    /// The reverse-engineering workbench shown in <see cref="WorkbenchMode"/>; it receives dumps through the <see cref="MidiBar"/>.
    /// </summary>
    public WorkbenchViewModel Workbench => field ??= new WorkbenchViewModel(new WorkbenchStorage
    {
        OpenSysEx = OpenSysExFile,
        OpenSession = OpenSessionFile,
        SaveSession = SaveSessionFile
    }, MidiBar, new WorkbenchSchemaHost
    {
        // the target schema must be an open document: an embedded schema cannot be changed
        FindDocument = name => LoadedSchemas.Find(name) is { IsEmbedded: false } entry ? entry.Document : null,
        CreateDocument = CreateWorkbenchDocument,
        Compile = (copy, copyOf) => new SchemaInstantiator(LoadedSchemas.Compile(copy, copyOf)),
        Processors = OpenedAssemblies.Processors
    });

    // the target schema of the workbench: an empty XML document (like the device schemas)
    private SchemaWorkspaceDocument CreateWorkbenchDocument(string name)
    {
        AddDocument(WorkbenchSchema.CreateEmpty(name), SchemaFormat.Xml, null, null);
        SelectedDocument!.IsDirty = true;
        StatusMessage = $"Created {name} from the workbench.";
        return SelectedDocument;
    }

    partial void OnSelectedModeChanged(int value)
    {
        if (value == WorkbenchMode)
        {
            // the target schema may have been edited (or opened, closed) in Schema mode
            Workbench.RefreshSchema();
        }
    }

    /// <summary>
    /// The data types to choose from for a field: the types registered in the <see cref="DataTypeRegistry"/>
    /// followed by the data type defs of the <see cref="ActiveDocument"/>. The data type editors also accept a custom value.
    /// </summary>
    public IReadOnlyList<string> DataTypeOptions => Models.DataTypeOptions.Get(ActiveDocument?.Schema);

    /// <summary>
    /// The processors a processor def can reference: the processors of the processor namespaces of the opened assemblies (e.g. 'sys.*').
    /// The processors of nodes, data type defs and the document are offered by their <see cref="ItemDetailsViewModel.ProcessorOptions"/>.
    /// </summary>
    public IReadOnlyList<string> ProcessorKeyOptions => ProcessorCatalog.GetProcessorKeys();

    /// <summary>
    /// The node defs to choose from for the nodeDef of the edited node: the node defs of the same kind
    /// in the <see cref="ActiveDocument"/> ('name') and in its included schemas ('schema.name').
    /// </summary>
    public IReadOnlyList<string> NodeDefOptions
        => EditedNode is { } node ? GetNodeDefNames(node.Kind, exclude: node) : [];

    /// <summary>
    /// The node defs to choose from for the member nodeDef of a choice: the group node defs, see <see cref="NodeDefOptions"/>.
    /// </summary>
    public IReadOnlyList<string> MemberNodeDefOptions => GetNodeDefNames(SchemaNodeKind.Group, exclude: EditedNode);

    private List<string> GetNodeDefNames(SchemaNodeKind kind, EditNode? exclude)
    {
        if (ActiveDocument?.Schema is not { } document)
        {
            return [];
        }
        // a node def cannot be its own node def
        var names = document.NodeDefs
            .Where(def => def.Kind == kind && !ReferenceEquals(def, exclude))
            .Select(def => def.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var include in document.Includes)
        {
            if (LoadedSchemas.Find(include.Schema, document)?.Schema is { } included)
            {
                names.AddRange(included.NodeDefs
                    .Where(def => def.Kind == kind)
                    .Select(def => $"{included.Name}{SchemaName.Separator}{def.Name}")
                    .Order(StringComparer.OrdinalIgnoreCase));
            }
        }
        return names;
    }

    public bool HasSelectedDocument => SelectedDocument is not null;

    // The Add menu only offers the items that are valid for the current selection.
    // With the node defs item selected, the node kinds add a node def.
    public bool CanAddMembers => GetTargetGroup() is not null || CanAddNodeDefs;
    private bool CanAddNodeDefs => SelectedItem is NodeDefsItem && ActiveDocument is not null && !IsReadOnly;
    public bool CanAddItemProcessor => ItemDetails?.HasProcessors == true;
    public bool CanAddItemProperty => ItemDetails is not null;
    public bool CanAdd => CanAddMembers || CanAddItemProperty;
    public bool ShowItemEntriesSeparator => CanAddMembers && CanAddItemProperty;

    /// <summary>
    /// True when the document root is selected: the editor then shows the document tabs.
    /// </summary>
    public bool IsDocumentSelected => SelectedItem is EditDocument;

    /// <summary>
    /// For the selected document: which schema with the same name is used instead of it, or which schemas it is used instead of,
    /// when schemas are compiled (see <see cref="SchemaSetWorkspace.GetSourceNote"/>); null when no other schema has its name.
    /// </summary>
    public string? DocumentSourceNote
        => SelectedItem is EditDocument && ActiveDocument is { } active ? LoadedSchemas.GetSourceNote(active) : null;

    public bool HasDocumentSourceNote => DocumentSourceNote is not null;

    /// <summary>
    /// The includes, data type defs and processor defs tabs of the document; null unless the document root is selected.
    /// </summary>
    [ObservableProperty]
    public partial DocumentTabsViewModel? DocumentTabs { get; private set; }

    /// <summary>
    /// The active tab of the node editor: <see cref="GeneralTab"/>, <see cref="ProcessorsTab"/>, <see cref="PropertiesTab"/>
    /// or, when the document root is selected, one of the document collection tabs.
    /// </summary>
    [ObservableProperty]
    public partial int SelectedEditorTab { get; set; }

    public const int GeneralTab = 0;
    public const int ProcessorsTab = 1;
    public const int PropertiesTab = 2;
    public const int IncludesTab = 3;
    public const int DataTypeDefsTab = 4;
    public const int ProcessorDefsTab = 5;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDocument), nameof(IsReadOnly), nameof(CanPin), nameof(DataTypeOptions))]
    [NotifyCanExecuteChangedFor(nameof(InstantiateSchemaCommand), nameof(MatchSchemaCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseDocumentCommand))]
    public partial SchemaWorkspaceDocument? SelectedDocument { get; set; }

    /// <summary>
    /// The open instantiate dialog (shown as an overlay); null when there is none.
    /// </summary>
    [ObservableProperty]
    public partial InstantiateSchemaViewModel? InstantiateDialog { get; private set; }

    /// <summary>
    /// The rename dialog while it is shown; null otherwise.
    /// </summary>
    [ObservableProperty]
    public partial DocumentViewModel? RenameDialog { get; private set; }

    /// <summary>
    /// The close document dialog while it is shown (to confirm discarding unsaved changes); null otherwise.
    /// </summary>
    [ObservableProperty]
    public partial CloseDocumentViewModel? CloseDialog { get; private set; }

    /// <summary>
    /// The confirmation dialog while it is shown (e.g. to confirm a delete); null otherwise.
    /// </summary>
    [ObservableProperty]
    public partial ConfirmViewModel? ConfirmDialog { get; private set; }

    /// <summary>
    /// The item selected in the schema tree: an <see cref="EditNode"/> (a member or a node def; the <see cref="EditDocument"/> for the root),
    /// the <see cref="NodeDefsItem"/> of a document or null.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNode))]
    [NotifyPropertyChangedFor(nameof(SelectedItemPath))]
    [NotifyPropertyChangedFor(nameof(IsDocumentSelected))]
    [NotifyPropertyChangedFor(nameof(DocumentSourceNote), nameof(HasDocumentSourceNote))]
    [NotifyPropertyChangedFor(nameof(CanArrange), nameof(CanChangeNodeType))]
    [NotifyCanExecuteChangedFor(nameof(DeleteItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstantiateSchemaCommand), nameof(MatchSchemaCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveNodeUpCommand), nameof(MoveNodeDownCommand), nameof(IndentNodeCommand), nameof(OutdentNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeNodeKindCommand))]
    public partial object? SelectedItem { get; set; }

    /// <summary>
    /// The path of the <see cref="SelectedItem"/>, starting with the document name:
    /// <c>Name/Header/Id</c> for a member, <c>Name/NodeDefs/Header</c> for a node def or <c>Name/NodeDefs</c> for the node defs item.
    /// </summary>
    public string SelectedItemPath => GetItemPath(SelectedItem);

    private string GetItemPath(object? item)
    {
        var root = ActiveDocument?.Schema.Name ?? String.Empty;
        if (item is NodeDefsItem)
        {
            return $"{root}/{nameof(EditDocument.NodeDefs)}";
        }
        if (item is not EditNode node || node is EditDocument)
        {
            return root;
        }

        var names = new List<string>();
        var top = node;
        for (EditNode? current = node; current is not null and not EditDocument; current = current.Parent)
        {
            names.Insert(0, current.Name);
            top = current;
        }

        // node defs are listed under their collection
        var path = String.Join('/', names);
        return ActiveDocument?.Schema.NodeDefs.Contains(top) == true ? $"{root}/{nameof(EditDocument.NodeDefs)}/{path}" : $"{root}/{path}";
    }

    /// <summary>
    /// The node the staged node values belong to: the selected node (a member or a node def), not the document root.
    /// Null when there is none.
    /// </summary>
    private EditNode? EditedNode => SelectedItem is EditDocument ? null : SelectedItem as EditNode;

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
    /// The effective value processors of the edited repeat (count) or choice (selected index), edited in its General tab;
    /// null for other nodes.
    /// </summary>
    [ObservableProperty]
    public partial ItemDetailsViewModel? ValueProcessorDetails { get; private set; }

    /// <summary>
    /// The General tab content: the <see cref="SelectedNode"/> (edited through the staged node values of this view model)
    /// or an <see cref="NodeEditor.ItemEditor"/> for the other document items; the <see cref="NodeDefsItem"/> shows a hint.
    /// </summary>
    [ObservableProperty]
    public partial object? GeneralEditor { get; private set; }

    /// <summary>
    /// The name of the selected document root. A changed name (committed when its editor loses focus) is applied by
    /// <see cref="RenameDocument"/> after the user confirmed it in the rename dialog.
    /// </summary>
    [ObservableProperty]
    public partial string DocumentName { get; set; } = String.Empty;

    partial void OnDocumentNameChanged(string value)
    {
        // set by the selection (or restored after a cancelled rename) when it equals the document name
        if (SelectedItem is EditDocument && SelectedDocument is { } document
            && !String.Equals(value.Trim(), document.Schema.Name, StringComparison.Ordinal))
        {
            RenameDocument();
        }
    }

    /// <summary>
    /// The live editor of the General tab values of the <see cref="EditedNode"/> (name, data type, nodeDef, count, selectedIndex, ...):
    /// changes are written to the node at once.
    /// </summary>
    [ObservableProperty]
    public partial NodeFacets NodeFacets { get; set; } = new();

    public IReadOnlyList<string> PipelineOptions => NodeFacets.PipelineOptions;

    partial void OnNodeFacetsChanged(NodeFacets oldValue, NodeFacets newValue)
    {
        oldValue.PropertyChanged -= OnNodeFacetsPropertyChanged;
        newValue.PropertyChanged += OnNodeFacetsPropertyChanged;
    }

    private void OnNodeFacetsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Models.NodeFacets.Name))
        {
            OnPropertyChanged(nameof(SelectedItemPath));
        }
    }

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

    /// <summary>
    /// Opens assemblies (.dll): their embedded schemas go to the schema set and the same assembly is passed to the processor manager.
    /// </summary>
    [RelayCommand]
    private async Task OpenAssemblies()
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
                Title = "Open assemblies with embedded schemas or processors",
                AllowMultiple = true,
                FileTypeFilter = AssemblyFileTypes
            });

            foreach (var file in files)
            {
                try
                {
                    var assembly = await LoadAssembly(file);
                    var known = OpenedAssemblies.Assemblies.Count;
                    var loaded = OpenedAssemblies.Load(assembly, file.Name);
                    StatusMessage = loaded.IsEmpty
                        ? $"{file.Name} contains no schemas or processors; it was not added."
                        : OpenedAssemblies.Assemblies.Count == known
                            ? $"{loaded.Name} was already opened."
                            : $"Opened {file.Name}: {loaded.Summary}.";
                }
                catch (Exception exception) when (IsAssemblyError(exception))
                {
                    StatusMessage = $"Could not open {file.Name}: {exception.Message}";
                }
            }
        }
        catch (Exception exception) when (IsStorageError(exception))
        {
            StatusMessage = $"Could not open assemblies: {exception.Message}";
        }
    }

    /// <summary>
    /// Loads the assembly from its path (so its dependencies resolve next to it) or, when the file has no local path
    /// (the Browser host), from its bytes.
    /// </summary>
    private static async Task<Assembly> LoadAssembly(IStorageFile file)
    {
        if (file.TryGetLocalPath() is { } path)
        {
            return Assembly.LoadFrom(path);
        }

        await using var stream = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return Assembly.Load(buffer.ToArray());
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

    /// <summary>
    /// Closes the selected document. A document with unsaved changes is only closed after the user confirmed it in the close dialog.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedDocument))]
    private void CloseDocument()
    {
        if (SelectedDocument is not { } document)
        {
            return;
        }

        if (document.IsDirty)
        {
            CloseDialog = new CloseDocumentViewModel(document, CloseCloseDialog);
            return;
        }
        RemoveDocument(document);
    }

    private void CloseCloseDialog(CloseDocumentViewModel dialog, bool confirmed)
    {
        CloseDialog = null;
        if (confirmed)
        {
            RemoveDocument(dialog.Document);
        }
    }

    /// <summary>
    /// Removes the document from the open documents and selects its neighbour (the next one, or else the previous one).
    /// </summary>
    private void RemoveDocument(SchemaWorkspaceDocument document)
    {
        var index = Documents.IndexOf(document);
        if (index < 0)
        {
            return;
        }

        var wasSelected = ReferenceEquals(SelectedDocument, document);
        _treeDocuments.Remove(document);
        _storageFiles.Remove(document);
        Documents.RemoveAt(index);
        if (wasSelected)
        {
            SelectedDocument = Documents.Count == 0 ? null : Documents[Math.Min(index, Documents.Count - 1)];
        }
        UpdateTreeDocuments();
        StatusMessage = $"Closed {document.Schema.Name}.";
    }

    /// <summary>
    /// Adds a new node of <paramref name="kind"/> with a default name to the target group (see <see cref="GetTargetGroup"/>)
    /// and selects it for editing. With the <see cref="NodeDefsItem"/> selected it adds a node def instead.
    /// </summary>
    [RelayCommand]
    private void AddNode(SchemaNodeKind kind)
    {
        if (ActiveDocument is null || IsReadOnly)
        {
            StatusMessage = "Create or open a schema before adding a node.";
            return;
        }
        if (SelectedItem is NodeDefsItem)
        {
            AddNodeDef(kind);
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
                SchemaNodeKind.Field => ActiveDocument.AddField(parent, name, "sys.uint8", String.Empty),
                SchemaNodeKind.Group => ActiveDocument.AddGroup(parent, name),
                SchemaNodeKind.Repeat => ActiveDocument.AddRepeat(parent, name, count: 1),
                SchemaNodeKind.Choice => ActiveDocument.AddChoice(parent, name, selectedIndex: 0),
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
    /// Adds a top-level node definition of <paramref name="kind"/> and selects it (below the <see cref="NodeDefsItem"/> in the schema tree).
    /// </summary>
    [RelayCommand]
    private void AddNodeDef(SchemaNodeKind kind)
    {
        if (ActiveDocument is null || IsReadOnly)
        {
            StatusMessage = "Create or open a schema before adding a node def.";
            return;
        }

        try
        {
            var definition = ActiveDocument.AddNodeDef(kind,
                GetUniqueName(ActiveDocument.Schema.NodeDefs.Select(existing => existing.Name), $"New{kind}Def"));
            SelectedNode = definition;
            SelectedEditorTab = GeneralTab;
            StatusMessage = $"Added a node def to {ActiveDocument.Schema.Name}.";
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            StatusMessage = $"The node def was not added: {exception.Message}";
        }
    }

    /// <summary>
    /// Creates the includes, data type defs and processor defs tabs of the document.
    /// </summary>
    private DocumentTabsViewModel CreateDocumentTabs(SchemaWorkspaceDocument document)
    {
        var schema = document.Schema;
        void Report(string message) => StatusMessage = message;
        return new DocumentTabsViewModel(
            new CollectionTabViewModel(document, schema.Includes, "include",
                () => document.AddInclude(GetUniqueName(schema.Includes.Select(include => include.Schema), "schema")), Report),
            new CollectionTabViewModel(document, schema.DataTypeDefs, "data type def",
                () => document.AddDataTypeDef(GetUniqueName(schema.DataTypeDefs.Select(definition => definition.Name), "NewDataType"), "sys.uint8"), Report, ProcessorCatalog, LoadedSchemas),
            new CollectionTabViewModel(document, schema.ProcessorDefs, "processor def",
                () => document.AddProcessorDef(GetUniqueName(schema.ProcessorDefs.Select(definition => definition.Name), "NewProcessor"), "namespace.id"), Report, ProcessorCatalog, LoadedSchemas));
    }

    /// <summary>
    /// Adds an own processor to the selected item and shows the Processors tab (the General tab for the document).
    /// </summary>
    [RelayCommand]
    private void AddItemProcessor()
    {
        if (ItemDetails is { HasProcessors: true } details)
        {
            details.AddProcessorCommand.Execute(null);
            SelectedEditorTab = details.ShowProcessorsTab ? ProcessorsTab : GeneralTab;
        }
    }

    /// <summary>
    /// Adds an own property to the selected item and shows the Properties tab (the General tab for the document).
    /// </summary>
    [RelayCommand]
    private void AddItemProperty()
    {
        if (ItemDetails is { } details)
        {
            details.AddPropertyCommand.Execute(null);
            SelectedEditorTab = details.ShowPropertiesTab ? PropertiesTab : GeneralTab;
        }
    }

    /// <summary>
    /// Asks to confirm the removal of the selected item (there is no undo); see <see cref="CloseDeleteDialog"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteItem))]
    private void DeleteItem()
    {
        if (ActiveDocument is null || IsReadOnly || GetDeletableItem() is not { } item)
        {
            return;
        }

        var schema = ActiveDocument.Schema;
        var (what, name) = item switch
        {
            EditNode node when schema.NodeDefs.Contains(node) => ("node def", node.Name),
            EditNode node => (node.Kind.ToString().ToLowerInvariant(), node.Name),
            EditDocumentRef include => ("include", include.Schema),
            EditDataTypeDef definition => ("data type def", definition.Name),
            EditProcessorDef definition => ("processor def", definition.Name),
            EditProcessorRef processor => ("processor", processor.Processor.ToString()),
            EditProperty property => ("property", property.Name),
            _ => ("item", item.ToString() ?? String.Empty)
        };
        var members = item is EditGroup { Members.Count: > 0 } group
            ? $" It has {group.Members.Count} member(s) that are deleted as well."
            : String.Empty;
        ConfirmDialog = new ConfirmViewModel("DELETE", $"Delete {what} '{name}'",
            $"The {what} will be removed from {schema.Name}.{members} This cannot be undone. Continue?",
            "Delete", (dialog, confirmed) => CloseDeleteDialog(item, confirmed));
    }

    /// <summary>
    /// Removes <paramref name="item"/> when the user confirmed it and it is still the selected item.
    /// A removed node selects its parent group, a removed top-level node the document root and a removed node def the <see cref="NodeDefsItem"/>.
    /// </summary>
    private void CloseDeleteDialog(object item, bool confirmed)
    {
        ConfirmDialog = null;
        if (!confirmed || ActiveDocument is null || IsReadOnly || !ReferenceEquals(GetDeletableItem(), item))
        {
            return;
        }

        try
        {
            var schema = ActiveDocument.Schema;
            object parent = item switch
            {
                EditNode { Parent: { } group and not EditDocument } => group,
                EditNode node when schema.NodeDefs.Contains(node) => NodeDefsItem.Get(schema),
                _ => schema
            };
            ActiveDocument.RemoveItem(item);
            SelectedItem = parent;
            StatusMessage = "Deleted the selected item.";
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            StatusMessage = $"Item was not deleted: {exception.Message}";
        }
    }

    /// <summary>
    /// The item the Delete command removes: the selected node or node def; not the document root or the node defs item.
    /// </summary>
    private object? GetDeletableItem() => SelectedItem is EditDocument or NodeDefsItem ? null : SelectedItem;

    private bool CanDeleteItem() => !IsReadOnly && GetDeletableItem() is not null;

    /// <summary>
    /// The selected node (a member or a node def) when it can be moved or changed; null for the document root or a read-only document.
    /// </summary>
    private EditNode? ArrangeableNode => !IsReadOnly && ActiveDocument is not null && SelectedItem is EditNode node and not EditDocument ? node : null;

    // The Arrange menu: moving applies to any node, changing the type only to a group, repeat or choice.
    public bool CanArrange => ArrangeableNode is not null;
    public bool CanChangeNodeType => ArrangeableNode is EditGroup;

    /// <summary>
    /// Moves the selected node one place up among its siblings.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMoveNodeUp))]
    private void MoveNodeUp() => ArrangeNode(node => ActiveDocument!.MoveNode(node, -1), "Moved {0} up.");

    private bool CanMoveNodeUp() => ArrangeableNode is { } node && ActiveDocument!.CanMoveNode(node, -1);

    /// <summary>
    /// Moves the selected node one place down among its siblings.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMoveNodeDown))]
    private void MoveNodeDown() => ArrangeNode(node => ActiveDocument!.MoveNode(node, 1), "Moved {0} down.");

    private bool CanMoveNodeDown() => ArrangeableNode is { } node && ActiveDocument!.CanMoveNode(node, 1);

    /// <summary>
    /// Moves the selected node into the group right before it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanIndentNode))]
    private void IndentNode() => ArrangeNode(node => ActiveDocument!.IndentNode(node), "Moved {0} into the group before it.");

    private bool CanIndentNode() => ArrangeableNode is { } node && ActiveDocument!.CanIndentNode(node);

    /// <summary>
    /// Moves the selected node out of its parent group, right after the parent.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOutdentNode))]
    private void OutdentNode() => ArrangeNode(node => ActiveDocument!.OutdentNode(node), "Moved {0} out of its group.");

    private bool CanOutdentNode() => ArrangeableNode is { } node && ActiveDocument!.CanOutdentNode(node);

    /// <summary>
    /// Applies <paramref name="arrange"/> to the selected node and selects it again: moving it in the tree drops its selection there.
    /// </summary>
    private void ArrangeNode(Action<EditNode> arrange, string message)
    {
        if (ArrangeableNode is not { } node)
        {
            return;
        }
        try
        {
            arrange(node);
            SelectedItem = null;
            SelectedItem = node;
            StatusMessage = String.Format(CultureInfo.CurrentCulture, message, node.Name);
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            StatusMessage = $"{node.Name} was not moved: {exception.Message}";
        }
    }

    /// <summary>
    /// Changes the selected group, repeat or choice into a node of <paramref name="kind"/>. When values of the current kind
    /// would be lost, the user confirms it first (there is no undo).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChangeNodeKind))]
    private void ChangeNodeKind(SchemaNodeKind kind)
    {
        if (!CanChangeNodeKind(kind) || SelectedItem is not EditGroup node)
        {
            return;
        }
        var lost = SchemaWorkspaceDocument.GetLostByKindChange(node, kind);
        if (lost.Count == 0)
        {
            ApplyNodeKind(node, kind);
            return;
        }
        var what = node.Kind.ToString().ToLowerInvariant();
        ConfirmDialog = new ConfirmViewModel("CHANGE TYPE", $"Change {what} '{node.Name}' into a {kind.ToString().ToLowerInvariant()}",
            $"The {String.Join(", ", lost)} of the {what} will be removed. This cannot be undone. Continue?",
            "Change", (dialog, confirmed) =>
            {
                ConfirmDialog = null;
                if (confirmed && ReferenceEquals(SelectedItem, node))
                {
                    ApplyNodeKind(node, kind);
                }
            });
    }

    private bool CanChangeNodeKind(SchemaNodeKind kind)
        => ArrangeableNode is EditGroup node && node.Kind != kind && kind is SchemaNodeKind.Group or SchemaNodeKind.Repeat or SchemaNodeKind.Choice;

    private void ApplyNodeKind(EditGroup node, SchemaNodeKind kind)
    {
        try
        {
            SelectedItem = ActiveDocument!.ChangeNodeKind(node, kind);
            StatusMessage = $"Changed {node.Name} into a {kind.ToString().ToLowerInvariant()}.";
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            StatusMessage = $"The type of {node.Name} was not changed: {exception.Message}";
        }
    }

    /// <summary>
    /// Opens the instantiate dialog for the selected group of the <see cref="ActiveDocument"/> (an open document or,
    /// read-only, an embedded schema selected in the assembly tree). The document is compiled first with all the open documents
    /// and the embedded schemas of the opened assemblies (see <see cref="SchemaSetWorkspace.Compile(EditDocument, SchemaWorkspaceDocument?)"/>);
    /// when that fails the problem is shown in the dialog.
    /// The schemas that were left out or replaced are shown as warnings in the dialog.
    /// </summary>
    /// <param name="group">
    /// The group to instantiate (from the context menu of a tree); it is selected in the tree of the active left tab first.
    /// Null instantiates the selected group.
    /// </param>
    [RelayCommand(CanExecute = nameof(CanInstantiateSchema))]
    private void InstantiateSchema(EditGroup? group) => OpenInstantiateDialog(group, isMatch: false);

    /// <summary>
    /// Opens the instantiate dialog in match mode for the selected group (see <see cref="InstantiateSchema"/>):
    /// a SysEx file or a message received from MIDI is read (deserialized) with the schema into the values of the group,
    /// with the plan of its root (a nested group is read as a range of that plan). Read problems are shown in the dialog.
    /// </summary>
    /// <param name="group">The group to match (from the context menu of a tree); null matches the selected group.</param>
    [RelayCommand(CanExecute = nameof(CanInstantiateSchema))]
    private void MatchSchema(EditGroup? group) => OpenInstantiateDialog(group, isMatch: true);

    private void OpenInstantiateDialog(EditGroup? group, bool isMatch)
    {
        if (group is not null && !ReferenceEquals(group, SelectedItem))
        {
            if (SelectedLeftTab == AssembliesLeftTab)
            {
                SelectedAssemblyItem = group;
            }
            else
            {
                SelectedItem = group;
            }
        }
        if (ActiveDocument is not { } active || SelectedItem is not EditGroup root || root is EditDocument)
        {
            return;
        }

        try
        {
            // compiled with all loaded schemas (open documents and embedded schemas)
            // the processors of the opened assemblies are used to detect address maps and when the values are serialized (or deserialized)
            var instantiator = new SchemaInstantiator(LoadedSchemas.Compile(active.Schema), OpenedAssemblies.Processors);
            var instance = instantiator.Create(root);
            var rootName = instantiator.FindRootName(root);
            Func<ICollection<string>, IReadOnlyList<SerializedMessage>>? serialize = rootName is { } writeRoot
                ? warnings => instantiator.Serialize(writeRoot, instance, OpenedAssemblies.Processors, warnings)
                : null;
            Func<string?>? describeTransfer = rootName is { } transferRoot
                ? () => instantiator.DescribeTransfer(transferRoot, instance, OpenedAssemblies.Processors)
                : null;
            SysExMatchViewModel? match = null;
            if (isMatch)
            {
                Func<byte[], ICollection<string>, long>? deserialize = rootName is { } readRoot
                    ? (bytes, warnings) => instantiator.Deserialize(readRoot, instance, bytes, OpenedAssemblies.Processors, warnings)
                    : null;
                match = new SysExMatchViewModel(deserialize, OpenSysExFile, MidiBar);
            }
            InstantiateDialog = new InstantiateSchemaViewModel(instance, serialize, SaveSysExFile, MidiBar, CloseInstantiateDialog, match,
                instantiator.Warnings, describeTransfer);
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            // the error is shown in the dialog: the status bar is easily overlooked
            StatusMessage = $"Cannot {(isMatch ? "match" : "instantiate")} {root.Name}: {exception.Message}";
            InstantiateDialog = new InstantiateSchemaViewModel(root.Name, exception.Message, CloseInstantiateDialog, isMatch);
        }
    }

    /// <summary>
    /// Renames the selected document to the staged <see cref="DocumentName"/> after the user confirmed it in the rename dialog.
    /// The dialog offers to rename the includes of the old name in the other open documents as well,
    /// or warns that includes may become invalid when no open document includes it.
    /// </summary>
    private void RenameDocument()
    {
        if (IsReadOnly || SelectedDocument is not { } document || SelectedItem is not EditDocument)
        {
            StatusMessage = "Select the document root before renaming it.";
            return;
        }

        var oldName = document.Schema.Name;
        var newName = DocumentName.Trim();
        if (newName.Length == 0)
        {
            StatusMessage = "The document name is required.";
            DocumentName = oldName;
            return;
        }
        if (String.Equals(newName, oldName, StringComparison.Ordinal))
        {
            StatusMessage = "The document name did not change.";
            return;
        }

        var dependents = GetIncludingDocuments(document, oldName).Select(dependent => dependent.Schema.Name).ToList();
        RenameDialog = new DocumentViewModel(oldName, newName, dependents, CloseRenameDialog);
    }

    private IEnumerable<SchemaWorkspaceDocument> GetIncludingDocuments(SchemaWorkspaceDocument document, string name)
        => Documents.Where(other => !ReferenceEquals(other, document)
            && other.Schema.Includes.Any(include => String.Equals(include.Schema, name, StringComparison.OrdinalIgnoreCase)));

    private void CloseRenameDialog(DocumentViewModel dialog, bool confirmed)
    {
        RenameDialog = null;
        var document = SelectedDocument;
        if (!confirmed || document is null || document.Schema.Name != dialog.OldName)
        {
            DocumentName = SelectedDocument?.Schema.Name ?? String.Empty;
            return;
        }

        var renamed = 0;
        if (dialog.HasDependents && dialog.UpdateIncludes)
        {
            foreach (var dependent in GetIncludingDocuments(document, dialog.OldName).ToList())
            {
                foreach (var include in dependent.Schema.Includes.Where(include => String.Equals(include.Schema, dialog.OldName, StringComparison.OrdinalIgnoreCase)))
                {
                    include.Schema = dialog.NewName;
                    renamed++;
                }
                dependent.MarkChanged();
            }
        }

        document.Schema.Name = dialog.NewName;
        document.MarkChanged();
        OnPropertyChanged(nameof(SelectedItemPath));
        StatusMessage = renamed > 0
            ? $"Renamed {dialog.OldName} to {dialog.NewName} and {renamed} include(s) in other documents."
            : $"Renamed {dialog.OldName} to {dialog.NewName}.";
    }

    private bool CanInstantiateSchema(EditGroup? group)
        => group is not null
            ? group is not EditDocument
            : ActiveDocument is not null && SelectedItem is EditGroup and not EditDocument;

    private void CloseInstantiateDialog(InstantiateSchemaViewModel dialog) => InstantiateDialog = null;

    /// <summary>
    /// Lets the user pick a SysEx (.syx) file and reads it.
    /// </summary>
    /// <returns>The name and contents of the file; null when the user cancelled.</returns>
    /// <exception cref="InvalidOperationException">The platform does not provide file-open access.</exception>
    private async Task<(string Name, byte[] Bytes)?> OpenSysExFile()
    {
        if (_storageProvider is null || !_storageProvider.CanOpen)
        {
            throw new InvalidOperationException("This platform does not provide file-open access.");
        }

        var files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open SysEx",
            AllowMultiple = false,
            FileTypeFilter = SysExFileTypes
        });
        if (files.Count == 0)
        {
            return null;
        }

        var file = files[0];
        await using var stream = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return (file.Name, buffer.ToArray());
    }

    /// <summary>
    /// Lets the user pick a SysEx (.syx) file, starting with <paramref name="suggestedName"/>, and saves <paramref name="bytes"/> to it.
    /// </summary>
    /// <returns>The name of the saved file; null when the user cancelled.</returns>
    /// <exception cref="InvalidOperationException">The platform does not provide file-save access.</exception>
    private async Task<string?> SaveSysExFile(string suggestedName, byte[] bytes)
    {
        if (_storageProvider is null || !_storageProvider.CanSave)
        {
            throw new InvalidOperationException("This platform does not provide file-save access.");
        }

        var file = await _storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save SysEx",
            SuggestedFileName = suggestedName,
            DefaultExtension = "syx",
            FileTypeChoices = SysExFileTypes
        });
        if (file is null)
        {
            return null;
        }

        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek)
        {
            stream.Position = 0;
            stream.SetLength(0);
        }
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
        return file.Name;
    }

    /// <summary>
    /// Lets the user pick a workbench session file and reads it.
    /// </summary>
    /// <returns>The file and its text; null when the user cancelled.</returns>
    /// <exception cref="InvalidOperationException">The platform does not provide file-open access.</exception>
    private async Task<(SessionFile File, string Text)?> OpenSessionFile()
    {
        if (_storageProvider is null || !_storageProvider.CanOpen)
        {
            throw new InvalidOperationException("This platform does not provide file-open access.");
        }

        var files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open workbench session",
            AllowMultiple = false,
            FileTypeFilter = SessionFileTypes
        });
        if (files.Count == 0)
        {
            return null;
        }

        var file = files[0];
        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);
        return (new SessionFile(file.Name, file), await reader.ReadToEndAsync());
    }

    /// <summary>
    /// Writes a workbench session to <paramref name="target"/>; without a target the user picks a file, starting with <paramref name="suggestedName"/>.
    /// </summary>
    /// <returns>The written file; null when the user cancelled.</returns>
    /// <exception cref="InvalidOperationException">The platform does not provide file-save access.</exception>
    private async Task<SessionFile?> SaveSessionFile(SessionFile? target, string suggestedName, string text)
    {
        if (target?.Handle is not IStorageFile file)
        {
            if (_storageProvider is null || !_storageProvider.CanSave)
            {
                throw new InvalidOperationException("This platform does not provide file-save access.");
            }
            var picked = await _storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save workbench session",
                SuggestedFileName = suggestedName,
                DefaultExtension = "json",
                FileTypeChoices = SessionFileTypes
            });
            if (picked is null)
            {
                return null;
            }
            file = picked;
        }

        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek)
        {
            stream.Position = 0;
            stream.SetLength(0);
        }
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(text);
        return new SessionFile(file.Name, file);
    }

    partial void OnSelectedDocumentChanged(SchemaWorkspaceDocument? value)
    {
        OnPropertyChanged(nameof(HasSelectedDocument));
        // a document that is not in the tree yet replaces the unpinned documents in it
        if (value is not null && !_treeDocuments.Contains(value))
        {
            _treeDocuments.RemoveWhere(document => !document.IsPinned);
            _treeDocuments.Add(value);
        }
        UpdateTreeDocuments();
        // picking a schema document leaves the assemblies tab (selecting its root below, not the first in the tree)
        _selectingDocument = true;
        try
        {
            SelectedLeftTab = SchemaLeftTab;
        }
        finally
        {
            _selectingDocument = false;
        }
        // a document picked in the strip selects its root in the schema tree
        if (!_keepSelectedItem)
        {
            SelectedItem = value?.Schema;
        }
        NotifyAddOptionsChanged();
    }

    /// <summary>
    /// Shows the documents of <see cref="_treeDocuments"/> in the schema tree in the order of <see cref="Documents"/>.
    /// The documents that stay are not removed, so their tree keeps its state.
    /// </summary>
    private void UpdateTreeDocuments()
    {
        var shown = Documents
            .Where(_treeDocuments.Contains)
            .Select(document => document.Schema)
            .ToList();

        for (var index = TreeDocuments.Count - 1; index >= 0; index--)
        {
            if (!shown.Contains(TreeDocuments[index]))
            {
                TreeDocuments.RemoveAt(index);
            }
        }
        // both follow the order of the documents: insert the missing ones in place
        for (var index = 0; index < shown.Count; index++)
        {
            if (index >= TreeDocuments.Count || !ReferenceEquals(TreeDocuments[index], shown[index]))
            {
                TreeDocuments.Insert(index, shown[index]);
            }
        }
    }

    /// <summary>
    /// The open document <paramref name="item"/> (an <see cref="EditDocument"/>, its <see cref="NodeDefsItem"/>, or a member or node def in it)
    /// belongs to; null when there is none.
    /// </summary>
    private SchemaWorkspaceDocument? FindDocument(object? item)
    {
        if (item is NodeDefsItem nodeDefs)
        {
            return Documents.FirstOrDefault(document => ReferenceEquals(document.Schema, nodeDefs.Document));
        }
        var node = item as EditNode;
        while (node?.Parent is not null)
        {
            node = node.Parent;
        }
        // a node def has no parent: it belongs to the document that has it
        return node is null ? null
            : Documents.FirstOrDefault(document => ReferenceEquals(document.Schema, node) || document.Schema.NodeDefs.Contains(node));
    }

    /// <summary>
    /// Each tab starts with the first item of its tree selected; with an empty tree nothing is selected,
    /// so the node editor shows nothing of the other tab.
    /// </summary>
    partial void OnSelectedLeftTabChanged(int value)
    {
        if (value == AssembliesLeftTab)
        {
            SelectedItem = null;
            SelectedAssemblyItem = OpenedAssemblies.Assemblies.FirstOrDefault();
        }
        else
        {
            SelectedAssemblyItem = null;
            if (!_selectingDocument)
            {
                SelectedItem = TreeDocuments.FirstOrDefault();
            }
        }
        NotifyAddOptionsChanged();
    }

    /// <summary>
    /// Shows the item selected in the assembly tree: an embedded schema, its node defs item or one of its nodes or node defs
    /// in the (read-only) node editor, an assembly or a processor namespace as its details.
    /// </summary>
    partial void OnSelectedAssemblyItemChanged(object? value)
    {
        var schema = value switch
        {
            LoadedSchema loadedSchema => loadedSchema,
            EditNode node => OpenedAssemblies.FindSchema(node),
            NodeDefsItem nodeDefs => OpenedAssemblies.FindSchema(nodeDefs.Document),
            _ => null
        };
        InspectedAssembly = value as LoadedAssembly;
        InspectedNamespace = value as ProcessorNamespaceItem;
        // the document first: the selection below is shown for it
        InspectedDocument = schema?.Document;
        SelectedItem = value switch
        {
            LoadedSchema loadedSchema => loadedSchema.Document.Schema,
            EditNode node when schema is not null => node,
            NodeDefsItem nodeDefs when schema is not null => nodeDefs,
            _ => null
        };
    }

    partial void OnSelectedItemChanged(object? value)
    {
        // an item of another (pinned) document in the schema tree selects that document
        if (SelectedLeftTab == SchemaLeftTab && FindDocument(value) is { } owner && !ReferenceEquals(owner, SelectedDocument))
        {
            _keepSelectedItem = true;
            try
            {
                SelectedDocument = owner;
            }
            finally
            {
                _keepSelectedItem = false;
            }
        }

        var node = value as EditNode;
        ItemDetails?.Dispose();
        ItemDetails = ActiveDocument is null ? null : ItemDetailsViewModel.Create(ActiveDocument, value, ProcessorCatalog, LoadedSchemas);
        // the node defs item shows a hint
        GeneralEditor = value is NodeDefsItem ? value : node;
        DocumentTabs?.Dispose();
        DocumentTabs = ActiveDocument is not null && value is EditDocument ? CreateDocumentTabs(ActiveDocument) : null;
        DocumentName = (value as EditDocument)?.Name ?? String.Empty;
        LoadNodeEditors();
        // a tab the new selection does not have would stay selected (but hidden)
        if (!IsEditorTabAvailable(SelectedEditorTab))
        {
            SelectedEditorTab = GeneralTab;
        }
        NotifyAddOptionsChanged();
    }

    /// <summary>
    /// Creates the live editors of the <see cref="EditedNode"/> (General tab values and value processors) and refreshes the option lists.
    /// </summary>
    private void LoadNodeEditors()
    {
        var node = EditedNode;
        ValueProcessorDetails?.Dispose();
        ValueProcessorDetails = ActiveDocument is null ? null : ItemDetailsViewModel.CreateValueProcessors(ActiveDocument, node, ProcessorCatalog, LoadedSchemas);
        // the data type defs, processor defs and opened assemblies may have changed since the last selection
        OnPropertyChanged(nameof(DataTypeOptions));
        OnPropertyChanged(nameof(ProcessorKeyOptions));
        OnPropertyChanged(nameof(NodeDefOptions));
        OnPropertyChanged(nameof(MemberNodeDefOptions));
        NodeFacets = new NodeFacets(ActiveDocument, node);
    }

    private bool IsEditorTabAvailable(int tab) => tab switch
    {
        ProcessorsTab => ItemDetails?.ShowProcessorsTab == true,
        PropertiesTab => ItemDetails?.ShowPropertiesTab == true,
        IncludesTab or DataTypeDefsTab or ProcessorDefsTab => IsDocumentSelected,
        _ => true
    };

    private void AddDocument(EditDocument schema, SchemaFormat format, string? fileName, IStorageFile? storageFile)
    {
        var document = new SchemaWorkspaceDocument(schema, format, fileName);
        // the LoadedSchemas follow the open documents
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

    /// <summary>
    /// The group a new member node is added to: the selected group (or node def), or the parent group of the selected field.
    /// Without a selection, or with the document root selected, it is the first top-level group.
    /// Null when nodes cannot be added: an open choice, a top-level node def field, or a non-node selection.
    /// </summary>
    private EditGroup? GetTargetGroup()
    {
        if (ActiveDocument is null || IsReadOnly)
        {
            return null;
        }
        var group = SelectedItem switch
        {
            EditGroup selected and not EditDocument => selected,
            EditField field => field.Parent,
            null or EditDocument => ActiveDocument.Schema.Members.OfType<EditGroup>().FirstOrDefault(),
            _ => null
        };
        return group is EditDocument or EditChoice { MemberNodeDef: not null } ? null : group;
    }

    private void NotifyAddOptionsChanged()
    {
        foreach (var name in (string[])[nameof(CanAddMembers), nameof(CanAddItemProcessor), nameof(CanAddItemProperty), nameof(CanAdd),
            nameof(ShowItemEntriesSeparator)])
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

    private static FilePickerFileType GetSchemaFileType(SchemaFormat format) => format switch
    {
        SchemaFormat.Json => SchemaFileTypes[1],
        SchemaFormat.Xml => SchemaFileTypes[2],
        SchemaFormat.Yaml => SchemaFileTypes[3],
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private static bool IsSchemaOrFileError(Exception exception)
        => IsSchemaError(exception) || IsStorageError(exception);

    private static bool IsSchemaError(Exception exception)
        => exception is ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or JsonException
            or XmlException
            or YamlException;

    private static bool IsAssemblyError(Exception exception)
        => IsStorageError(exception)
            || exception is BadImageFormatException or ArgumentException or TypeLoadException;

    private static bool IsStorageError(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException;
}
