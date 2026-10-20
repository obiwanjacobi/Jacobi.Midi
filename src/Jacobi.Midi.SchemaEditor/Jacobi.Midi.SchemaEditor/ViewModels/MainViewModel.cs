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

    private readonly Dictionary<SchemaWorkspaceDocument, IStorageFile> _storageFiles = [];
    private IStorageProvider? _storageProvider;
    private IClipboard? _clipboard;

    /// <summary>
    /// MIDI endpoint/group selection and connection. Disabled when no MIDI API is provided.
    /// </summary>
    public MidiBarViewModel MidiBar { get; init; } = new(null);

    public ObservableCollection<SchemaWorkspaceDocument> Documents { get; } = [];

    /// <summary>
    /// The opened assemblies with their embedded schemas (not editable, not part of <see cref="Documents"/>) and processors.
    /// </summary>
    public AssemblyWorkspace OpenedAssemblies { get; } = new();

    /// <summary>
    /// The active tab of the left panel: <see cref="SchemaLeftTab"/> (the schema tree) or <see cref="AssembliesLeftTab"/>.
    /// </summary>
    [ObservableProperty]
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
    [NotifyPropertyChangedFor(nameof(ActiveDocument), nameof(IsReadOnly), nameof(DataTypeOptions))]
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
    /// The document the node editor shows: the read-only <see cref="InspectedDocument"/> of an embedded schema
    /// or else the <see cref="SelectedDocument"/>.
    /// </summary>
    public SchemaWorkspaceDocument? ActiveDocument => InspectedDocument ?? SelectedDocument;

    /// <summary>
    /// True when the node editor shows an embedded schema: it cannot be changed.
    /// </summary>
    public bool IsReadOnly => ActiveDocument?.IsReadOnly == true;

    public ObservableCollection<SysExMessage> SysExMessages { get; } = [];
    public ObservableCollection<SysExComparisonRow> ComparedBytes { get; } = [];
    private static readonly string[] RegisteredDataTypes = DataTypeRegistry.CreateDefault().Types
        .Select(descriptor => descriptor.Name.FullName)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>
    /// The data types to choose from for a field: the types registered in the <see cref="DataTypeRegistry"/>
    /// followed by the data type defs of the <see cref="ActiveDocument"/>. The data type editors also accept a custom value.
    /// </summary>
    public IReadOnlyList<string> DataTypeOptions
    {
        get
        {
            var document = ActiveDocument?.Schema;
            if (document is null)
            {
                return RegisteredDataTypes;
            }
            return [.. RegisteredDataTypes, .. document.DataTypeDefs
                .Select(def => $"{document.Name}{SchemaName.Separator}{def.Name}")
                .Where(name => !RegisteredDataTypes.Contains(name, StringComparer.OrdinalIgnoreCase))];
        }
    }

    public bool HasSelectedDocument => SelectedDocument is not null;

    // The Add menu only offers the items that are valid for the current selection.
    public bool CanAddMembers => GetTargetGroup() is not null;
    public bool CanAddItemProcessor => ItemDetails?.HasProcessors == true;
    public bool CanAddItemProperty => ItemDetails is not null;
    public bool CanAdd => CanAddMembers || CanAddItemProperty;
    public bool ShowItemEntriesSeparator => CanAddMembers && CanAddItemProperty;

    /// <summary>
    /// True when the document root is selected: the editor then shows the document tabs.
    /// </summary>
    public bool IsDocumentSelected => SelectedItem is EditDocument;

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
    public const int NodeDefsTab = 4;
    public const int DataTypeDefsTab = 5;
    public const int ProcessorDefsTab = 6;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDocument), nameof(IsReadOnly), nameof(DataTypeOptions))]
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
    /// The rename dialog while it is shown; null otherwise.
    /// </summary>
    [ObservableProperty]
    public partial DocumentViewModel? RenameDialog { get; private set; }

    /// <summary>
    /// The item selected in the schema tree or in the node defs tab: an <see cref="EditNode"/>
    /// (the <see cref="EditDocument"/> for the root) or null.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNode))]
    [NotifyPropertyChangedFor(nameof(SelectedItemPath))]
    [NotifyPropertyChangedFor(nameof(IsDocumentSelected))]
    [NotifyCanExecuteChangedFor(nameof(DeleteItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstantiateSchemaCommand))]
    public partial object? SelectedItem { get; set; }

    /// <summary>
    /// The path of the <see cref="SelectedItem"/>, starting with the document name:
    /// <c>Name/Header/Id</c> for a member or <c>Name/NodeDefs/Header</c> for a node def.
    /// </summary>
    public string SelectedItemPath => GetItemPath(SelectedItem is EditDocument ? SelectedNodeDef ?? SelectedItem : SelectedItem);

    private string GetItemPath(object? item)
    {
        var root = ActiveDocument?.Schema.Name ?? String.Empty;
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
    /// The node def selected in the Node Defs tab of the document root; null when none is selected.
    /// It is edited next to the tab's tree and does not replace the <see cref="SelectedItem"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedItemPath))]
    [NotifyCanExecuteChangedFor(nameof(DeleteItemCommand))]
    public partial EditNode? SelectedNodeDef { get; set; }

    /// <summary>
    /// The effective processors and properties of the <see cref="SelectedNodeDef"/>; null when none is selected.
    /// </summary>
    [ObservableProperty]
    public partial ItemDetailsViewModel? SelectedNodeDefDetails { get; private set; }

    /// <summary>
    /// The node the staged node values belong to: the <see cref="SelectedNodeDef"/> while the document root is selected,
    /// otherwise the selected node. Null when there is none.
    /// </summary>
    private EditNode? EditedNode => SelectedItem is EditDocument ? SelectedNodeDef : SelectedItem as EditNode;

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

    /// <summary>
    /// The staged name of the selected document root; applied by <see cref="RenameDocumentCommand"/> after confirmation.
    /// </summary>
    [ObservableProperty]
    public partial string DocumentName { get; set; } = String.Empty;

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

    /// <summary>
    /// A warning when the staged Value does not fit the staged data type (see <see cref="DataTypeValueValidator"/>); otherwise null.
    /// Informational only: it never blocks applying the edits.
    /// </summary>
    public string? ValueWarning => DataTypeValueValidator.Validate(ActiveDocument?.Schema, NodeDataType, NodeFacets.Value);

    /// <summary>
    /// A note when the staged data type is custom (not registered and not defined in the document); otherwise null.
    /// </summary>
    public string? DataTypeNote => DataTypeValueValidator.IsCustom(ActiveDocument?.Schema, NodeDataType)
        ? "Custom data type: its values are not validated."
        : null;

    partial void OnNodeDataTypeChanged(string value)
    {
        OnPropertyChanged(nameof(ValueWarning));
        OnPropertyChanged(nameof(DataTypeNote));
    }

    partial void OnNodeFacetsChanged(NodeFacets oldValue, NodeFacets newValue)
    {
        oldValue.PropertyChanged -= OnNodeFacetsPropertyChanged;
        newValue.PropertyChanged += OnNodeFacetsPropertyChanged;
        OnPropertyChanged(nameof(ValueWarning));
    }

    private void OnNodeFacetsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Models.NodeFacets.Value))
        {
            OnPropertyChanged(nameof(ValueWarning));
        }
    }

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
                    StatusMessage = OpenedAssemblies.Assemblies.Count == known
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

    [RelayCommand]
    private void ApplyNodeEdits()
    {
        if (ActiveDocument is null || EditedNode is not { } node)
        {
            StatusMessage = "Select a schema node before editing its properties.";
            return;
        }
        if (IsReadOnly)
        {
            StatusMessage = "The schema of an assembly is read-only.";
            return;
        }

        try
        {
            ActiveDocument.ApplyNodeEdits(
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
        if (ActiveDocument is null || IsReadOnly)
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
                SchemaNodeKind.Field => ActiveDocument.AddField(parent, name, "sys.uint8", String.Empty),
                SchemaNodeKind.Group => ActiveDocument.AddGroup(parent, name),
                SchemaNodeKind.Repeat => ActiveDocument.AddRepeat(parent, name, count: 1),
                SchemaNodeKind.Choice => ActiveDocument.AddChoice(parent, name, selectedIndex: 0),
                _ => throw new NotSupportedException($"Cannot add a node of kind {kind}.")
            };
            // a node added to a selected node def stays in the Node Defs tab
            if (SelectedItem is EditDocument && SelectedNodeDef is not null)
            {
                SelectedNodeDef = node;
            }
            else
            {
                SelectedNode = node;
            }
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
            // the node def is edited next to the Node Defs tab: the document root stays selected
            SelectedItem = ActiveDocument.Schema;
            SelectedNodeDef = definition;
            SelectedEditorTab = NodeDefsTab;
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
                () => document.AddDataTypeDef(GetUniqueName(schema.DataTypeDefs.Select(definition => definition.Name), "NewDataType"), "sys.uint8"), Report),
            new CollectionTabViewModel(document, schema.ProcessorDefs, "processor def",
                () => document.AddProcessorDef(GetUniqueName(schema.ProcessorDefs.Select(definition => definition.Name), "NewProcessor"), "namespace.id"), Report));
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
    /// Removes the selected node or node def. A removed node selects its parent group, a removed top-level node the document root.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteItem))]
    private void DeleteItem()
    {
        if (ActiveDocument is null || IsReadOnly || GetDeletableItem() is not { } item)
        {
            return;
        }

        try
        {
            var inNodeDefsTab = SelectedItem is EditDocument;
            var parent = item is EditNode { Parent: { } group and not EditDocument } ? group : null;
            ActiveDocument.RemoveItem(item);
            if (inNodeDefsTab)
            {
                SelectedNodeDef = parent;
            }
            else
            {
                SelectedItem = parent ?? ActiveDocument.Schema;
            }
            StatusMessage = "Deleted the selected item.";
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            StatusMessage = $"Item was not deleted: {exception.Message}";
        }
    }

    /// <summary>
    /// The item the Delete command removes: the selected node, or the selected node def while the document root is selected.
    /// </summary>
    private object? GetDeletableItem() => SelectedItem is EditDocument ? SelectedNodeDef : SelectedItem;

    private bool CanDeleteItem() => !IsReadOnly && GetDeletableItem() is not null;

    /// <summary>
    /// Opens the instantiate dialog for the selected group of the <see cref="ActiveDocument"/> (an open document or,
    /// read-only, an embedded schema selected in the assembly tree). The document and the documents it includes
    /// (taken from the open documents and the embedded schemas of the opened assemblies) are compiled first; when that fails the problem is reported and no dialog opens.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstantiateSchema))]
    private void InstantiateSchema()
    {
        if (ActiveDocument is not { } active || SelectedItem is not EditGroup root)
        {
            return;
        }

        try
        {
            // the embedded schemas of the opened assemblies can be included too; an open document takes precedence by name
            var workspace = Documents.Select(document => document.Schema).ToList();
            var embedded = OpenedAssemblies.Assemblies
                .SelectMany(assembly => assembly.Schemas)
                .Select(schema => schema.Document.Schema)
                .Where(schema => !workspace.Any(open => String.Equals(open.Name, schema.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var instantiator = SchemaInstantiator.Create(active.Schema, [.. workspace, .. embedded]);
            InstantiateDialog = new InstantiateSchemaViewModel(instantiator.Create(root), CloseInstantiateDialog);
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            // the error is shown in the dialog: the status bar is easily overlooked
            StatusMessage = $"Cannot instantiate {root.Name}: {exception.Message}";
            InstantiateDialog = new InstantiateSchemaViewModel(root.Name, exception.Message, CloseInstantiateDialog);
        }
    }

    /// <summary>
    /// Renames the selected document to the staged <see cref="DocumentName"/> after the user confirmed it in the rename dialog.
    /// The dialog offers to rename the includes of the old name in the other open documents as well,
    /// or warns that includes may become invalid when no open document includes it.
    /// </summary>
    [RelayCommand]
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

    private bool CanInstantiateSchema()
        => ActiveDocument is not null && SelectedItem is EditGroup and not EditDocument;

    private void CloseInstantiateDialog(InstantiateSchemaViewModel dialog, bool confirmed)
    {
        InstantiateDialog = null;
        if (confirmed && dialog.Root is { } instance)
        {
            LastInstance = instance;
            StatusMessage = $"Instantiated {instance.Name} with {instance.Fields().Count()} field value(s).";
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
        // picking a schema document leaves the assemblies tab
        SelectedLeftTab = SchemaLeftTab;
        SelectedItem = null;
        NotifyAddOptionsChanged();
    }

    partial void OnSelectedLeftTabChanged(int value)
    {
        // each tab starts without a selection: the node editor shows what the tab's tree has selected
        SelectedAssemblyItem = null;
        SelectedItem = null;
    }

    /// <summary>
    /// Shows the item selected in the assembly tree: an embedded schema or one of its nodes in the (read-only) node editor,
    /// an assembly or a processor namespace as its details.
    /// </summary>
    partial void OnSelectedAssemblyItemChanged(object? value)
    {
        var schema = value switch
        {
            LoadedSchema loadedSchema => loadedSchema,
            EditNode node => OpenedAssemblies.FindSchema(node),
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
            _ => null
        };
    }

    partial void OnSelectedNodeDefChanged(EditNode? value)
    {
        SelectedNodeDefDetails?.Dispose();
        SelectedNodeDefDetails = ActiveDocument is null || value is null ? null : ItemDetailsViewModel.Create(ActiveDocument, value);
        LoadStagedNode();
        NotifyAddOptionsChanged();
    }

    partial void OnSelectedItemChanged(object? value)
    {
        var node = value as EditNode;
        SelectedNodeDef = null;
        ItemDetails?.Dispose();
        ItemDetails = ActiveDocument is null ? null : ItemDetailsViewModel.Create(ActiveDocument, value);
        GeneralEditor = node;
        DocumentTabs?.Dispose();
        DocumentTabs = ActiveDocument is not null && value is EditDocument ? CreateDocumentTabs(ActiveDocument) : null;
        DocumentName = (value as EditDocument)?.Name ?? String.Empty;
        LoadStagedNode();
        // a tab the new selection does not have would stay selected (but hidden)
        if (!IsEditorTabAvailable(SelectedEditorTab))
        {
            SelectedEditorTab = GeneralTab;
        }
        NotifyAddOptionsChanged();
    }

    /// <summary>
    /// Stages the values of the <see cref="EditedNode"/> for the node editors.
    /// </summary>
    private void LoadStagedNode()
    {
        var node = EditedNode;
        // the data type defs of the document may have changed since the last selection
        OnPropertyChanged(nameof(DataTypeOptions));
        NodeName = node?.Name ?? String.Empty;
        NodeDataType = (node as EditField)?.DataType?.ToString() ?? String.Empty;
        NodeDescription = GetProperty(node, "description");
        NodeSampleValue = GetProperty(node, "sample");
        NodeFacets = NodeFacets.Read(node);
    }

    private bool IsEditorTabAvailable(int tab) => tab switch
    {
        ProcessorsTab => ItemDetails?.ShowProcessorsTab == true,
        PropertiesTab => ItemDetails?.ShowPropertiesTab == true,
        IncludesTab or NodeDefsTab or DataTypeDefsTab or ProcessorDefsTab => IsDocumentSelected,
        _ => true
    };

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
        // while the document root is selected, nodes are added to the node def selected in the Node Defs tab
        var group = (SelectedItem is EditDocument ? SelectedNodeDef : SelectedItem) switch
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

    private static bool IsAssemblyError(Exception exception)
        => IsStorageError(exception)
            || exception is BadImageFormatException or ArgumentException or TypeLoadException;

    private static bool IsStorageError(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException;
}
