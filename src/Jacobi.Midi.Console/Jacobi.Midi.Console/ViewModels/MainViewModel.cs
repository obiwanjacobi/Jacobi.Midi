using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Editing;
using Jacobi.Midi.Console.Core.Files;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Core.Parameters;
using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Console.Core.Schemas;
using Jacobi.Midi.Console.Dashboards;
using Jacobi.Midi.Console.Looks;
using Jacobi.Midi.Core;

namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// The view model of the main view: the open rig (a <see cref="RigSession"/> with the device states), the dashboards
/// open on it (one <see cref="DashboardSession"/> each, all sharing the device states), the selected dashboard, the
/// Edit/Play mode and the look. Until the device synchronisation exists, the values come from dumps.
/// <para>
/// In Edit mode the left panel shows the rig (<see cref="RigTree"/>: the devices and their parameters) and the palettes
/// of controls; what is dragged from it is dropped on the dashboard (<see cref="Drop"/>, <see cref="Bind"/>). The user
/// selects a control (<see cref="SelectedControl"/>, its <see cref="Properties"/>) or a device of the rig
/// (<see cref="DeviceProperties"/>) and changes it, moves it (<see cref="Place"/>, <see cref="Move"/>) or deletes it.
/// Every change is an edit in the <see cref="History"/>, so it can be undone, and marks the dashboard or the rig as
/// changed; after it the dashboard is resolved again, after an edit of the rig the rig is opened again (the devices that
/// stay the same keep their values). Save writes them to their files. Every problem (files, schemas, devices, bindings,
/// dumps) is listed in the <see cref="Notifications"/>, with the information the user should know.
/// </para>
/// </summary>
internal sealed partial class MainViewModel : ViewModelBase, IDashboardEditor, IDisposable
{
    /// <summary>The size of the root grid of a new dashboard.</summary>
    private static readonly CellSize NewDashboardSize = new(48, 30);

    // what the console said about files, dumps and edits, besides the problems of the rig and the dashboard
    private readonly List<Notification> _reports = [];
    private Rig? _rig;
    /// <summary>Where the rig was read from, and whether it changed since.</summary>
    [ObservableProperty]
    public partial FileOrigin RigOrigin { get; private set; } = new();

    /// <param name="midiApi">The platform's MIDI backend; null when it has none (the console stays offline).</param>
    public MainViewModel(IMidiApi? midiApi = null)
    {
        Midi = new MidiViewModel(midiApi, Notify);
        Midi.Changed += (_, _) => OnMidiChanged();
        History.Changed += (_, _) =>
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(UndoTip));
            OnPropertyChanged(nameof(RedoTip));
        };
        // the console starts empty
        Status = "Open a rig (☰ › Open › Rig…), then open its dashboards or make a new one (+ in Edit mode).";
    }

    /// <summary>The edits of the open dashboards and rig.</summary>
    public EditHistory History { get; } = new();

    /// <summary>The mode (Offline or MIDI) and the MIDI connections of the devices of the rig.</summary>
    public MidiViewModel Midi { get; }

    public string UndoTip => History.NextUndo is { } edit ? $"Undo {edit.Description} (Ctrl+Z)" : "Nothing to undo";

    public string RedoTip => History.NextRedo is { } edit ? $"Redo {edit.Description} (Ctrl+Y)" : "Nothing to redo";

    /// <summary>The storage provider of the window or browser, for the file pickers; set by the view.</summary>
    public IStorageProvider? StorageProvider { get; set; }

    public IReadOnlyList<Look> Looks => ConsoleLooks.All;

    [ObservableProperty]
    public partial Look SelectedLook { get; set; } = ConsoleLooks.Default;

    /// <summary>The open rig; null until a rig is open.</summary>
    [ObservableProperty]
    public partial RigSession? Rig { get; private set; }

    /// <summary>The open dashboards in the order they were opened, each with where it was read from and its session on <see cref="Rig"/>.</summary>
    public ObservableCollection<DashboardEntry> Dashboards { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(DashboardName), nameof(DashboardZoom), nameof(SelectedSession))]
    [NotifyCanExecuteChangedFor(nameof(CloseDashboardCommand))]
    public partial DashboardEntry? SelectedDashboard { get; set; }

    /// <summary>
    /// The session of the selected dashboard: what is shown. A change is notified also when the dashboard stays
    /// selected but has a new session (the rig was opened again).
    /// </summary>
    public DashboardSession? SelectedSession => SelectedDashboard?.Session;

    /// <summary>Edit mode: controls are selected and changed, the values are locked; otherwise Play.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying))]
    public partial bool IsEditing { get; set; }

    /// <summary>Play mode: the controls are used, the layout is locked.</summary>
    public bool IsPlaying
    {
        get => !IsEditing;
        set => IsEditing = !value;
    }

    [ObservableProperty]
    public partial string Status { get; private set; } = String.Empty;

    /// <summary>
    /// The confirmation dialog while it is shown (e.g. to confirm closing a changed dashboard); null otherwise.
    /// </summary>
    [ObservableProperty]
    public partial ConfirmViewModel? ConfirmDialog { get; private set; }

    /// <summary>
    /// What the console tells the user: the problems (of files, schemas, devices, bindings, dumps) and information (a
    /// schema that replaces a built-in one, sizes given to a file).
    /// </summary>
    public ObservableCollection<Notification> Notifications { get; } = [];

    /// <summary>The header of the notifications: how many, and how many of them are problems.</summary>
    public string NotificationsHeader
    {
        get
        {
            var problems = Notifications.Count(notification => notification.IsProblem);
            return problems == 0 ? $"Notifications ({Notifications.Count})"
                : $"Notifications ({Notifications.Count}, {problems} problem{(problems == 1 ? "" : "s")})";
        }
    }

    public string Title => SelectedDashboard?.Dashboard.Name ?? "MIDI Console";

    /// <summary>The name of the selected dashboard; changing it is an edit.</summary>
    public string? DashboardName
    {
        get => SelectedDashboard?.Dashboard.Name;
        set
        {
            if (SelectedDashboard is not { Dashboard: var dashboard } entry || String.IsNullOrWhiteSpace(value))
                return;
            var edit = new ValueEdit<string>("dashboard name", () => dashboard.Name, name => dashboard.Name = name, value.Trim());
            if (!edit.Changes)
                return;
            DoDashboardEdit(entry, edit);
            Status = $"The dashboard is named '{dashboard.Name}'. Save to keep it.";
        }
    }

    /// <summary>
    /// The zoom of the selected dashboard in percent: its cell size (<see cref="Dashboard.CellSize"/>) to the default
    /// one; the whole dashboard scales with it. Changing it is an edit of the cell size, in whole pixels.
    /// </summary>
    public decimal? DashboardZoom
    {
        get => SelectedDashboard?.Dashboard.CellSize * 100m / Dashboard.DefaultCellSize;
        set
        {
            if (SelectedDashboard is not { Dashboard: var dashboard } entry || value is not { } zoom)
                return;
            var cellSize = Math.Clamp((int)Math.Round(Dashboard.DefaultCellSize * zoom / 100), Dashboard.MinimumCellSize, Dashboard.MaximumCellSize);
            var edit = new ValueEdit<int>("dashboard zoom", () => dashboard.CellSize, size => dashboard.CellSize = size, cellSize);
            if (!edit.Changes)
                return;
            DoDashboardEdit(entry, edit);
            Status = $"The dashboard is shown at {DashboardZoom:0}% (cells of {cellSize} pixels). Save to keep it.";
        }
    }

    /// <summary>The devices of the rig with their parameters, for the left panel of Edit mode.</summary>
    public ObservableCollection<RigTreeItem> RigTree { get; } = [];

    /// <summary>
    /// The device aliases the selected dashboard uses, for its properties in Edit mode: renaming one renames it
    /// throughout the dashboard (<see cref="RenameDashboardAlias"/>).
    /// </summary>
    public ObservableCollection<DashboardAliasViewModel> DashboardAliases { get; } = [];

    /// <summary>The item selected in the rig tree; a device shows its <see cref="DeviceProperties"/>.</summary>
    [ObservableProperty]
    public partial RigTreeItem? SelectedRigItem { get; set; }

    /// <summary>The properties of the device selected in the rig tree; null when none is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSelection))]
    public partial DevicePropertiesViewModel? DeviceProperties { get; private set; }

    /// <summary>True when the panel shows a device or a control; otherwise it shows the dashboard.</summary>
    public bool ShowsSelection => DeviceProperties is not null || Properties is not null;

    /// <summary>The parameter controls to drag onto a dashboard.</summary>
    public IReadOnlyList<PaletteItem> ControlPalette { get; }
        = [.. ControlCatalog.ParameterKinds.Select(kind => new PaletteItem(kind, DashboardBuilder.HasFace(kind)))];

    /// <summary>The layout controls to drag onto a dashboard.</summary>
    public IReadOnlyList<PaletteItem> LayoutPalette { get; }
        = [.. ControlCatalog.LayoutKinds.Select(kind => new PaletteItem(kind, IsAvailable: true))];

    /// <summary>The root groups of the schema library (address maps first), for the root of a device.</summary>
    public IReadOnlyList<string> MapRoots => Rig?.Library.Roots.Select(name => name.ToString()).ToList() ?? [];

    /// <summary>True when a root of the schema library is an address map: a root data messages carry.</summary>
    public bool IsAddressMap(string root) => Rig?.Library.Maps?.IsMap(new BinarySerializer.Schema.SchemaName(root)) == true;

    /// <summary>The control selected in Edit mode; null when none is.</summary>
    [ObservableProperty]
    public partial DashboardControl? SelectedControl { get; set; }

    /// <summary>The properties of <see cref="SelectedControl"/>, for the control panel; null when none is selected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSelection))]
    public partial ControlPropertiesViewModel? Properties { get; private set; }

    Look IColorEditor.Look => SelectedLook;

    void IDashboardEditor.Select(DashboardControl? control) => SelectedControl = control;

    /// <summary>The selected dashboard must be built again: an edit was done or undone.</summary>
    public event EventHandler? DashboardChanged;

    /// <summary>Opens another rig in place of the open one; when that changed since it was saved, after the user confirmed discarding the changes.</summary>
    [RelayCommand]
    private async Task OpenRig()
    {
        if (!await ConfirmDiscard(RigOrigin, "Open another rig", $"The rig '{RigOrigin.Name}' has unsaved changes. Opening another rig discards them.", "Open"))
            return;
        if (await PickFile("Open rig", "Rig", "*.xml", "*.json") is not { } file)
            return;
        try
        {
            _rig = RigFile.Read(file.Text, FileFormats.FromPath(file.Name));
            RigOrigin = file.Origin;
            // the edits and reports may be of the rig that was open
            History.Clear();
            _reports.Clear();
            OpenSessions();
        }
        catch (Exception exception) when (exception is FileFormatException or ArgumentException)
        {
            Report($"'{file.Name}' cannot be opened", exception.Message);
        }
    }

    /// <summary>Opens another dashboard on the rig, next to the open ones.</summary>
    [RelayCommand]
    private async Task OpenDashboard()
    {
        if (await PickFile("Open dashboard", "Dashboard", "*.xml", "*.json") is not { } file)
            return;
        Dashboard dashboard;
        try
        {
            dashboard = DashboardFile.Read(file.Text, FileFormats.FromPath(file.Name));
        }
        catch (Exception exception) when (exception is FileFormatException or ArgumentException)
        {
            Report($"'{file.Name}' cannot be opened", exception.Message);
            return;
        }

        var entry = new DashboardEntry(Complete(dashboard, file.Origin), file.Origin);
        Dashboards.Add(entry);
        if (Rig is not null && dashboard.Sources.IsEmpty)
        {
            OpenSession(entry, Rig);
            SelectedDashboard = entry;
            Status = $"'{dashboard.Name}' opened.";
        }
        else
        {
            // the dashboard brings its own schemas: the library is built again, and with it the device states
            OpenSessions(select: entry);
            if (Rig is not null && !dashboard.Sources.IsEmpty)
                Status = $"'{dashboard.Name}' opened; its schemas were added, so the values of the devices were reset.";
        }
        RefreshNotifications();
    }

    /// <summary>Opens a new, empty dashboard on the rig, in Edit mode: a grid to drop controls on.</summary>
    [RelayCommand]
    private void NewDashboard()
    {
        if (_rig is null)
        {
            Status = "Open a rig first: a dashboard is built from the parameters of its devices.";
            return;
        }
        var name = "New dashboard";
        for (var number = 2; Dashboards.Any(entry => entry.Dashboard.Name == name); number++)
            name = $"New dashboard {number}";
        var dashboard = new Dashboard { Name = name, Layout = new LayoutControl { Kind = LayoutControlKind.Grid, Size = NewDashboardSize } };
        // not saved yet: it counts as changed
        var entry = new DashboardEntry(dashboard, new FileOrigin { Name = $"{name}.dashboard.xml", Changes = 1 });
        Dashboards.Add(entry);
        if (Rig is null)
            OpenSessions(select: entry);
        else
        {
            OpenSession(entry, Rig);
            SelectedDashboard = entry;
        }
        IsEditing = true;
        Status = $"'{name}' opened: drag parameters from the rig or controls from the palettes onto it.";
    }

    /// <summary>Closes the selected dashboard; when it changed since it was saved, after the user confirmed discarding the changes.</summary>
    [RelayCommand(CanExecute = nameof(CanCloseDashboard))]
    private async Task CloseDashboard()
    {
        if (SelectedDashboard is not { } entry)
            return;
        if (!await ConfirmDiscard(entry.Origin, $"Close '{entry.Dashboard.Name}'", "The dashboard has unsaved changes. Closing it discards them.", "Close"))
            return;
        // the dashboard may be closed meanwhile
        if (Dashboards.Contains(entry))
            CloseDashboard(entry);
    }

    private void CloseDashboard(DashboardEntry entry)
    {
        var index = Dashboards.IndexOf(entry);
        Dashboards.RemoveAt(index);
        // the edits may be of the closed dashboard
        History.Clear();
        CloseSession(entry);
        SelectedDashboard = Dashboards.Count == 0 ? null : Dashboards[Math.Min(index, Dashboards.Count - 1)];
        Status = $"'{entry.Dashboard.Name}' closed.";
    }

    private bool CanCloseDashboard() => SelectedDashboard is not null;

    // a dump is the source of the values offline; in MIDI mode the devices are
    private bool CanLoadDump() => Midi.IsOffline;

    [RelayCommand(CanExecute = nameof(CanLoadDump))]
    private async Task LoadDump()
    {
        if (StorageProvider is null || Rig is null)
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load dump",
            FileTypeFilter = [new FilePickerFileType("SysEx dump") { Patterns = ["*.syx"] }, FilePickerFileTypes.All],
        });
        if (files.Count == 0)
            return;
        await using var stream = await files[0].OpenReadAsync();
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes);
        LoadDump(files[0].Name, bytes.ToArray());
    }

    /// <summary>Writes the selected dashboard and the rig when they changed; asks where for a file not saved before.</summary>
    [RelayCommand]
    private async Task Save()
    {
        var saved = new List<string>();
        if (_rig is not null && RigOrigin.IsChanged && await WriteRig(saveAs: false))
            saved.Add(RigOrigin.Name);
        if (SelectedDashboard is { Origin.IsChanged: true } entry && await WriteDashboard(saveAs: false))
            saved.Add(entry.Origin.Name);
        if (saved.Count > 0)
            Status = $"Saved {String.Join(" and ", saved.Select(name => $"'{name}'"))}.";
        else if (!RigOrigin.IsChanged && Dashboards.All(entry => !entry.Origin.IsChanged))
            Status = "Nothing changed.";
    }

    /// <summary>Writes the selected dashboard; asks where for a file not saved before.</summary>
    [RelayCommand]
    private Task SaveDashboard() => SaveDashboard(saveAs: false);

    [RelayCommand]
    private Task SaveDashboardAs() => SaveDashboard(saveAs: true);

    /// <summary>Writes the rig; asks where for a file not saved before.</summary>
    [RelayCommand]
    private Task SaveRig() => SaveRig(saveAs: false);

    [RelayCommand]
    private Task SaveRigAs() => SaveRig(saveAs: true);

    private async Task SaveDashboard(bool saveAs)
    {
        if (await WriteDashboard(saveAs))
            Status = $"Saved '{SelectedDashboard!.Origin.Name}'.";
    }

    private async Task SaveRig(bool saveAs)
    {
        if (await WriteRig(saveAs))
            Status = $"Saved '{RigOrigin.Name}'.";
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (History.Undo() is { } edit)
            Status = $"Undone: {edit.Description}.";
    }

    private bool CanUndo() => History.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (History.Redo() is { } edit)
            Status = $"Redone: {edit.Description}.";
    }

    private bool CanRedo() => History.CanRedo;

    public void SetControlColor(DashboardControl control, ColorArea area, RgbColor? color)
    {
        ArgumentNullException.ThrowIfNull(control);
        EditControl(control, $"{AreaColors.NameOf(area).ToLowerInvariant()} colour", () => control.Colors[area],
            value => control.Colors = control.Colors.With(area, value), color?.ToString());
    }

    public void SetDeviceColor(string alias, ColorArea area, RgbColor? color)
    {
        if (_rig?.Find(alias) is not { } device)
            return;
        var name = AreaColors.NameOf(area).ToLowerInvariant();
        var edit = new ValueEdit<string?>($"{name} colour of device '{alias}'", () => device.Colors[area],
            value => device.Colors = device.Colors.With(area, value), color?.ToString());
        if (!edit.Changes)
            return;
        DoRigEdit(edit);
        Status = $"{edit.Description}: {color?.ToString() ?? "the look's"}. Save to keep it in the rig.";
        // the dashboard's colour of the device wins: say so, the change does not show here
        if (SelectedDashboard?.Dashboard.FindDevice(alias)?.Colors[area] is { } own)
            Inform($"Device '{alias}'", $"the dashboard gives it its own {name} colour ({own}), so the rig's colour does not show on it.");
    }

    /// <summary>
    /// Changes a property of a control of a dashboard as an edit (nothing when the value is the same):
    /// <paramref name="what"/> names the property for the history and the status, <paramref name="shown"/> the new
    /// value for the status (by default the value itself).
    /// </summary>
    public void EditControl<T>(DashboardControl control, string what, Func<T> get, Action<T> set, T value, string? shown = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        var entry = EntryOf(control);
        if (entry is null)
            return;
        var edit = new ValueEdit<T>($"{what} of '{ControlCatalog.NameOf(control)}'", get, set, value);
        if (!edit.Changes)
            return;
        DoDashboardEdit(entry, edit);
        Status = $"{edit.Description}: {shown ?? value?.ToString() ?? "default"}. Save to keep it.";
    }

    /// <summary>Binds a slot of a control (null unbinds it); a device the dashboard does not list yet is added.</summary>
    public void SetSlot(ParameterControl control, string slot, Binding? binding)
    {
        ArgumentNullException.ThrowIfNull(control);
        var entry = EntryOf(control);
        if (entry is null || control.Slots.GetValueOrDefault(slot) == binding)
            return;
        var slots = new Dictionary<string, Binding>(control.Slots, StringComparer.Ordinal);
        if (binding is null)
            slots.Remove(slot);
        else
            slots[slot] = binding;
        var edits = new List<IEdit>();
        if (binding?.Device is { } device && DeviceEdit(entry.Dashboard, device) is { } addDevice)
            edits.Add(addDevice);
        edits.Add(SlotsEdit(control, slots));
        DoDashboardEdit(entry, new EditGroup($"binding of '{ControlCatalog.NameOf(control)}'", edits));
        Status = $"'{ControlCatalog.NameOf(control)}'.{slot}: {binding?.ToString() ?? "not bound"}. Save to keep it.";
    }

    /// <summary>Sets the device of a control (null inherits it); a device the dashboard does not list yet is added.</summary>
    public void SetControlDevice(DashboardControl control, string? alias)
    {
        ArgumentNullException.ThrowIfNull(control);
        var entry = EntryOf(control);
        if (entry is null || String.Equals(control.Device, alias, StringComparison.Ordinal))
            return;
        var edits = new List<IEdit>();
        if (alias is not null && DeviceEdit(entry.Dashboard, alias) is { } addDevice)
            edits.Add(addDevice);
        edits.Add(new ValueEdit<string?>("device", () => control.Device, value => control.Device = value, alias));
        DoDashboardEdit(entry, new EditGroup($"device of '{ControlCatalog.NameOf(control)}'", edits));
        Status = $"The device of '{ControlCatalog.NameOf(control)}': {alias ?? "inherited"}. Save to keep it.";
    }

    // the layout in cells needs every size and position: those the file leaves out are given, which changes it
    private Dashboard Complete(Dashboard dashboard, FileOrigin origin)
    {
        var given = GridLayout.Complete(dashboard.Layout);
        if (given > 0)
        {
            origin.Changes++;
            Inform($"'{origin.Name}'", $"{given} sizes and positions were missing and are given; save it to keep them.");
        }
        return dashboard;
    }

    /// <summary>
    /// Moves and sizes a control as one edit, with the controls it pushes aside and the grids that grow (see
    /// <see cref="GridLayout.Place"/>); a control outside a grid keeps no position.
    /// </summary>
    public void Place(DashboardControl control, CellPosition at, CellSize size)
    {
        ArgumentNullException.ThrowIfNull(control);
        var entry = EntryOf(control);
        if (entry is null)
            return;
        var (edits, moved) = PlacementEdits(entry.Dashboard.Layout, control, at, size);
        if (edits.Count == 0)
            return;

        var what = size != control.Size ? "size" : "position";
        var edit = new EditGroup($"{what} of '{ControlCatalog.NameOf(control)}'", edits);
        DoDashboardEdit(entry, edit);
        Status = $"{edit.Description}: {at.Column},{at.Row} {size.Columns}×{size.Rows}{Others(moved)}. Save to keep it.";
    }

    /// <summary>
    /// Adds what is dropped to a layout control of the selected dashboard as one edit: a parameter or group of the rig
    /// becomes a bound control or section (<see cref="ControlDrops"/>), a palette item a new, unbound control. In a grid
    /// or section it is placed at the cell (pushing others aside), elsewhere after the others. It is selected.
    /// </summary>
    public void Drop(DragItem item, LayoutControl container, CellPosition at)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(container);
        var entry = EntryOf(container);
        if (entry is null || entry.Session is not { } session)
            return;
        var plan = item switch
        {
            ParameterDragItem parameter => ControlDrops.Drop(entry.Dashboard, session.Resolution, parameter.Source, container),
            ControlDragItem { Kind: ParameterControlKind kind } => new DropPlan { Control = ControlCatalog.Create(kind) },
            ControlDragItem { Kind: LayoutControlKind kind } => new DropPlan { Control = ControlCatalog.Create(kind) },
            _ => null,
        };
        if (plan?.Control is not { } control || control.Size is not { } size)
            return;

        // a control dropped on tabs is a page, or goes on a page of its own
        var inserted = container.Kind == LayoutControlKind.Tabs ? ControlCatalog.PageFor(container, control) : control;
        if (inserted != control)
            control.At = new CellPosition(0, 0);
        var edits = new List<IEdit>(AdditionEdits(entry.Dashboard, plan));
        var children = container.Children.Append(inserted).ToList();
        edits.Add(ChildrenEdit(container, children));
        var (placements, moved) = WithChildren([(container, children)], () => PlacementEdits(entry.Dashboard.Layout, inserted, at, inserted.Size ?? size));
        edits.AddRange(placements);
        DoDashboardEdit(entry, new EditGroup($"add '{ControlCatalog.NameOf(control)}'", edits));
        SelectedControl = control;
        Status = $"'{ControlCatalog.NameOf(control)}' added to '{ControlCatalog.NameOf(container)}'{Declared(plan)}{Others(moved)}. Save to keep it.";
    }

    /// <summary>Binds the first slot of a control to the field dropped on it, as one edit (<see cref="ControlDrops.Bind"/>).</summary>
    public void Bind(ParameterDragItem item, ParameterControl control)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(control);
        var entry = EntryOf(control);
        if (entry is null || entry.Session is not { } session)
            return;
        if (item.Source.Node.Info is { } info && ControlCatalog.RefusalOf(control.Kind, info) is { } refusal)
        {
            Status = $"'{ControlCatalog.NameOf(control)}' is not bound: {refusal}";
            return;
        }
        var plan = ControlDrops.Bind(entry.Dashboard, session.Resolution, item.Source, control);
        var slots = new Dictionary<string, Binding>(control.Slots, StringComparer.Ordinal) { [plan.Slot!] = plan.Binding! };
        var edits = new List<IEdit>(AdditionEdits(entry.Dashboard, plan)) { SlotsEdit(control, slots) };
        DoDashboardEdit(entry, new EditGroup($"binding of '{ControlCatalog.NameOf(control)}'", edits));
        SelectedControl = control;
        Status = $"'{ControlCatalog.NameOf(control)}'.{plan.Slot}: {plan.Binding}{Declared(plan)}. Save to keep it.";
    }

    /// <summary>
    /// Moves a control into another layout control as one edit: at the cell of a grid or section (pushing others
    /// aside), after the others elsewhere; its bindings stay bound to the same parameters (<see cref="ControlMoves"/>).
    /// Within its own grid it is placed (<see cref="Place"/>).
    /// </summary>
    public void Move(DashboardControl control, LayoutControl container, CellPosition at)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(container);
        var entry = EntryOf(control);
        if (entry is null || control.Size is not { } size || GridLayout.ParentOf(entry.Dashboard.Layout, control) is not { } parent)
            return;
        if (parent == container)
        {
            Place(control, at, size);
            return;
        }
        if (ControlMoves.Contains(control, container))
        {
            Status = $"'{ControlCatalog.NameOf(control)}' cannot be moved into itself.";
            return;
        }

        var root = entry.Dashboard.Layout;
        var plan = ControlMoves.Move(root, control, container);
        // a control moved onto tabs is a page, or goes on a page of its own at its top left
        var inserted = container.Kind == LayoutControlKind.Tabs ? ControlCatalog.PageFor(container, control) : control;
        var from = parent.Children.Where(child => child != control).ToList();
        var to = container.Children.Append(inserted).ToList();
        var edits = new List<IEdit> { ChildrenEdit(parent, from), ChildrenEdit(container, to) };
        CellPosition? position = inserted != control ? new CellPosition(0, 0) : GridLayout.IsGrid(container) ? control.At : null;
        if (position != control.At)
            edits.Add(new ValueEdit<CellPosition?>("position", () => control.At, value => control.At = value, position));
        var (placements, moved) = WithChildren([(parent, from), (container, to)], () => PlacementEdits(root, inserted, at, inserted.Size ?? size));
        edits.AddRange(placements);
        if (plan.Device is { } device)
            edits.Add(new ValueEdit<string?>("device", () => control.Device, value => control.Device = value, device));
        if (plan.Base is { } basePath)
            edits.Add(new ValueEdit<string?>("base", () => control.Base, value => control.Base = value, basePath));
        edits.AddRange(plan.Slots.Select(change => SlotsEdit(change.Control, change.Slots)));
        DoDashboardEdit(entry, new EditGroup($"move of '{ControlCatalog.NameOf(control)}'", edits));
        foreach (var problem in plan.Problems)
            Report($"Moving '{ControlCatalog.NameOf(control)}'", problem);
        if (plan.Problems.Count == 0)
        {
            var rebound = plan.Slots.Count > 0 ? $"; {plan.Slots.Count} control(s) bound for their new place" : "";
            Status = $"'{ControlCatalog.NameOf(control)}' moved into '{ControlCatalog.NameOf(container)}'{rebound}{Others(moved)}. Save to keep it.";
        }
    }

    /// <summary>Selects nothing: no control, no device; the panel shows the dashboard (Esc, a click beside the dashboard).</summary>
    [RelayCommand]
    private void ClearSelection()
    {
        SelectedControl = null;
        SelectedRigItem = null;
    }

    /// <summary>Deletes the selected control (Delete).</summary>
    [RelayCommand]
    private void DeleteSelected()
    {
        if (IsEditing && SelectedControl is { } control)
            DeleteControl(control);
    }

    /// <summary>Takes a control and the controls inside it out of its dashboard, as one edit; the root stays.</summary>
    public void DeleteControl(DashboardControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var entry = EntryOf(control);
        if (entry is null)
            return;
        if (GridLayout.ParentOf(entry.Dashboard.Layout, control) is not { } parent)
        {
            Status = "The root of a dashboard cannot be deleted.";
            return;
        }
        DoDashboardEdit(entry, new EditGroup($"delete '{ControlCatalog.NameOf(control)}'", [ChildrenEdit(parent, [.. parent.Children.Where(child => child != control)])]));
        SelectedControl = null;
        Status = $"'{ControlCatalog.NameOf(control)}' deleted (Ctrl+Z brings it back). Save to keep it.";
    }

    /// <summary>Replaces a control by one of another kind with what it has (<see cref="ControlCatalog.WithKind"/>), as one edit.</summary>
    public void ChangeKind(DashboardControl control, Enum kind)
    {
        ArgumentNullException.ThrowIfNull(control);
        var entry = EntryOf(control);
        if (entry is null || GridLayout.ParentOf(entry.Dashboard.Layout, control) is not { } parent)
            return;
        if (kind is ParameterControlKind other && control is ParameterControl parameter && !Accepts(parameter, other))
        {
            Status = $"'{ControlCatalog.NameOf(control)}' cannot be a {kind}: it cannot show what it is bound to.";
            return;
        }
        var changed = ControlCatalog.WithKind(control, kind);
        var lost = control is ParameterControl before && changed is ParameterControl after ? before.Slots.Count - after.Slots.Count : 0;
        DoDashboardEdit(entry, new EditGroup($"kind of '{ControlCatalog.NameOf(control)}'",
            [ChildrenEdit(parent, [.. parent.Children.Select(child => child == control ? changed : child)])]));
        SelectedControl = changed;
        Status = $"'{ControlCatalog.NameOf(control)}' is a {kind}{(lost > 0 ? $"; {lost} binding(s) of slots a {kind} does not have are gone" : "")}. Save to keep it.";
    }

    /// <summary>True when the control is in a grid or section: it has a position.</summary>
    public bool IsInGrid(DashboardControl control)
        => EntryOf(control)?.Dashboard is { } dashboard && IsInGrid(dashboard.Layout, control);

    /// <summary>
    /// True when a control of the kind can show what the slots of the control are bound to (see
    /// <see cref="ControlCatalog.Accepts"/>); a slot whose binding is not resolved does not count.
    /// </summary>
    public bool Accepts(ParameterControl control, ParameterControlKind kind)
    {
        ArgumentNullException.ThrowIfNull(control);
        var session = EntryOf(control)?.Session;
        return control.Slots.Keys.All(slot => session?.ValueOf(control, slot)?.Info is not { } info || ControlCatalog.Accepts(kind, info));
    }

    /// <summary>True when the control is the root of its dashboard.</summary>
    public bool IsRoot(DashboardControl control) => Dashboards.Any(entry => entry.Dashboard.Layout == control);

    /// <summary>The device, base and variables a control inherits from the layout controls around it.</summary>
    public BindingScope ScopeAround(DashboardControl control)
        => EntryOf(control)?.Dashboard is { } dashboard && GridLayout.ParentOf(dashboard.Layout, control) is { } parent
            ? BindingScope.Of(dashboard.Layout, parent) ?? BindingScope.None
            : BindingScope.None;

    public bool IsDeviceOfRig(string alias) => _rig?.Find(alias) is not null;

    public DeviceInstance? FindDevice(string alias) => _rig?.Find(alias);

    /// <summary>
    /// Changes a property of a device of the rig as an edit (nothing when the value is the same); the rig is opened again
    /// (see <see cref="RigSession.Open"/>).
    /// </summary>
    public void EditDevice<T>(DeviceInstance device, string what, Func<T> get, Action<T> set, T value, string? shown = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        var edit = new ValueEdit<T>($"{what} of device '{device.Alias}'", get, set, value);
        if (!edit.Changes)
            return;
        DoRigEdit(edit);
        Status = $"{edit.Description}: {shown ?? value?.ToString() ?? "none"}. Save to keep it in the rig.";
    }

    /// <summary>Gives a device of the rig another alias; the open dashboards that use the old one are reported.</summary>
    public void RenameDevice(DeviceInstance device, string alias)
    {
        ArgumentNullException.ThrowIfNull(device);
        var old = device.Alias;
        EditDevice(device, "alias", () => device.Alias, value => device.Alias = value, alias);
        foreach (var dashboard in Dashboards.Select(entry => entry.Dashboard).Where(dashboard => DeviceAliases.Of(dashboard).Contains(old, StringComparer.OrdinalIgnoreCase)))
            Report($"'{dashboard.Name}'", $"it uses device '{old}', which the rig now calls '{alias}'; rename it in the properties of the dashboard.");
    }

    /// <summary>
    /// Renames a device alias wherever the dashboard uses it (its devices, controls and bindings), as one edit of the
    /// dashboard; e.g. after the device got another alias in the rig. The dashboard files that are not open are not changed.
    /// </summary>
    /// <exception cref="ArgumentException">The new alias cannot be taken (see <see cref="DeviceAliases.ProblemOf"/>).</exception>
    public void RenameDashboardAlias(Dashboard dashboard, string alias, string newAlias)
    {
        if (Dashboards.FirstOrDefault(entry => entry.Dashboard == dashboard) is not { } entry)
            return;
        var edits = DeviceAliases.Rename(dashboard, alias, newAlias);
        if (edits.Count == 0)
            return;
        DoDashboardEdit(entry, new EditGroup($"device alias '{alias}'", edits));
        Status = $"Device '{alias}' is renamed to '{newAlias}' in '{dashboard.Name}'"
            + (IsDeviceOfRig(newAlias) ? "" : $"; the rig has no device '{newAlias}'") + ". Save to keep it.";
    }

    /// <summary>
    /// Adds a device for an address map of the schema library to the rig, named after the map (<c>Gp8.AddressMap</c>:
    /// <c>gp8</c>); it is selected.
    /// </summary>
    [RelayCommand]
    private void AddDevice(string mapRoot)
    {
        if (_rig is null)
        {
            Status = "Open a rig first.";
            return;
        }
        AddDevice(new DeviceInstance { Alias = AliasFor(mapRoot), MapRoot = mapRoot }, rebuildsLibrary: false);
    }

    /// <summary>
    /// Adds a device to the rig for the address map of a schema file or an assembly the user picks: the file becomes a
    /// schema source of the device, and the library is built again with it (the values of the devices are reset).
    /// </summary>
    public async Task AddDeviceFrom(SchemaSourceKind kind)
    {
        if (_rig is null)
        {
            Status = "Open a rig first.";
            return;
        }
        if (await PickSource(kind) is not { } path)
            return;
        var device = new DeviceInstance { Alias = "device", MapRoot = String.Empty, Sources = SourcesWith(SchemaSources.None, kind, path) };
        // the root groups of the documents the file brings (address maps first; also those that replace a built-in
        // schema of the same name, which is told in the notifications)
        var library = BuildLibrary(device);
        var brought = (kind == SchemaSourceKind.Assembly ? library.RootsFromAssembly(AssemblyNameOf(path)) : library.RootsFromFile(path))
            .Select(name => name.ToString()).ToList();
        if (brought.Count == 0)
        {
            var problems = library.Problems.Where(problem => problem.IsError).Select(problem => $"{problem.Source}: {problem.Message}").ToList();
            Report($"'{Path.GetFileName(path)}'", problems.Count > 0
                ? $"no device is added: {String.Join(" ", problems)}"
                : "it has no root group: no device is added. Add it to a device in its panel when that device's schema needs it.");
            return;
        }
        device.MapRoot = brought[0];
        device.Alias = AliasFor(device.MapRoot);
        AddDevice(device, rebuildsLibrary: true);
        if (brought.Count > 1)
            Status += $" It brings {brought.Count} root groups; choose another as its root in its panel.";
    }

    // the name an assembly file has (its embedded schemas are known by it); the file name when it cannot be read
    private static string AssemblyNameOf(string path)
    {
        try
        {
            return System.Reflection.AssemblyName.GetAssemblyName(path).Name ?? Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or ArgumentException or System.Security.SecurityException)
        {
            return Path.GetFileNameWithoutExtension(path);
        }
    }

    /// <summary>Adds a schema file or an assembly the user picks to a device; the library is built again with it.</summary>
    public async Task AddDeviceSource(DeviceInstance device, SchemaSourceKind kind)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (await PickSource(kind) is not { } path)
            return;
        if (device.Sources.Schemas.Concat(device.Sources.Assemblies).Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            Status = $"Device '{device.Alias}' has '{Path.GetFileName(path)}' already.";
            return;
        }
        EditDeviceSources(device, SourcesWith(device.Sources, kind, path), $"'{Path.GetFileName(path)}' added");
    }

    /// <summary>Takes a schema file or an assembly out of the sources of a device; the library is built again without it.</summary>
    public void RemoveDeviceSource(DeviceInstance device, string path)
    {
        ArgumentNullException.ThrowIfNull(device);
        var sources = device.Sources with
        {
            Schemas = [.. device.Sources.Schemas.Where(schema => schema != path)],
            Assemblies = [.. device.Sources.Assemblies.Where(assembly => assembly != path)],
        };
        EditDeviceSources(device, sources, $"'{Path.GetFileName(path)}' removed");
    }

    private void EditDeviceSources(DeviceInstance device, SchemaSources sources, string shown)
    {
        DoRigEdit(new ValueEdit<SchemaSources>($"schemas of device '{device.Alias}'", () => device.Sources, value => device.Sources = value, sources), rebuildsLibrary: true);
        Status = $"Device '{device.Alias}': {shown}; the schemas were built again, so the values of the devices were reset. Save to keep it in the rig.";
    }

    private void AddDevice(DeviceInstance device, bool rebuildsLibrary)
    {
        var rig = _rig!;
        DoRigEdit(new ValueEdit<IReadOnlyList<DeviceInstance>>($"add device '{device.Alias}'", () => rig.Devices, value => rig.Devices = value, [.. rig.Devices, device]), rebuildsLibrary);
        SelectedRigItem = RigTree.FirstOrDefault(item => item.Instance == device);
        Status = $"Device '{device.Alias}' ({device.MapRoot}) added{(rebuildsLibrary ? "; the schemas were built again, so the values of the devices were reset" : "")}. Save to keep it in the rig.";
    }

    // the alias of a new device: the first part of its map root in lower case (Gp8.AddressMap: gp8), unique in the rig
    private string AliasFor(string mapRoot)
    {
        var name = mapRoot.Split('.')[0].ToLowerInvariant();
        if (!DeviceAliases.IsValid(name))
            name = "device";
        var alias = name;
        for (var number = 2; _rig?.Find(alias) is not null; number++)
            alias = $"{name}{number}";
        return alias;
    }

    private static SchemaSources SourcesWith(SchemaSources sources, SchemaSourceKind kind, string path)
        => kind == SchemaSourceKind.Assembly
            ? sources with { Assemblies = [.. sources.Assemblies, path] }
            : sources with { Schemas = [.. sources.Schemas, path] };

    // a schema file or assembly on this computer: they are loaded by their path (not in the browser)
    private async Task<string?> PickSource(SchemaSourceKind kind)
    {
        if (StorageProvider is null)
            return null;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = kind == SchemaSourceKind.Assembly ? "Add an assembly with schemas" : "Add a schema file",
            FileTypeFilter = kind == SchemaSourceKind.Assembly
                ? [new FilePickerFileType("Assembly") { Patterns = ["*.dll"] }]
                : [new FilePickerFileType("Schema") { Patterns = ["*.xml", "*.json"] }],
        });
        if (files.Count == 0)
            return null;
        if (files[0].TryGetLocalPath() is { } path)
            return path;
        Report($"'{files[0].Name}'", "schemas and assemblies are loaded from a file on this computer; here the file has no path.");
        return null;
    }

    /// <summary>Takes a device out of the rig; the open dashboards that use it are reported.</summary>
    public void RemoveDevice(DeviceInstance device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (_rig is not { } rig)
            return;
        DoRigEdit(new ValueEdit<IReadOnlyList<DeviceInstance>>($"remove device '{device.Alias}'", () => rig.Devices, value => rig.Devices = value, [.. rig.Devices.Where(other => other != device)]));
        DeviceProperties = null;
        Status = $"Device '{device.Alias}' removed (Ctrl+Z brings it back). Save to keep it in the rig.";
        foreach (var dashboard in Dashboards.Select(entry => entry.Dashboard).Where(dashboard => DeviceAliases.Of(dashboard).Contains(device.Alias, StringComparer.OrdinalIgnoreCase)))
            Report($"'{dashboard.Name}'", $"it uses device '{device.Alias}', which is no longer in the rig.");
    }

    private static bool IsInGrid(LayoutControl root, DashboardControl control)
        => GridLayout.ParentOf(root, control) is { } parent && GridLayout.IsGrid(parent);

    private DashboardEntry? EntryOf(DashboardControl control)
        => Dashboards.FirstOrDefault(entry => ControlMoves.Contains(entry.Dashboard.Layout, control));

    // the position and size edits of placing a control: its own and those of the controls it pushes aside and the
    // grids that grow (a control outside a grid keeps no position); and how many other controls change
    private static (List<IEdit> Edits, int Others) PlacementEdits(LayoutControl root, DashboardControl control, CellPosition at, CellSize size)
    {
        var changes = GridLayout.Place(root, control, at, size);
        var edits = new List<IEdit>();
        foreach (var (changed, placement) in changes)
        {
            if (IsInGrid(root, changed) && changed.At != placement.At)
                edits.Add(new ValueEdit<CellPosition?>("position", () => changed.At, value => changed.At = value, placement.At));
            if (changed.Size != placement.Size)
                edits.Add(new ValueEdit<CellSize?>("size", () => changed.Size, value => changed.Size = value, placement.Size));
        }
        return (edits, changes.Count - 1);
    }

    // runs a function on the dashboard as it will be with the children of some layout controls changed
    private static T WithChildren<T>(IReadOnlyList<(LayoutControl Layout, IReadOnlyList<DashboardControl> Children)> changes, Func<T> function)
    {
        var before = changes.Select(change => change.Layout.Children).ToList();
        try
        {
            foreach (var (layout, children) in changes)
                layout.Children = children;
            return function();
        }
        finally
        {
            for (var index = changes.Count - 1; index >= 0; index--)
                changes[index].Layout.Children = before[index];
        }
    }

    private static ValueEdit<IReadOnlyList<DashboardControl>> ChildrenEdit(LayoutControl layout, IReadOnlyList<DashboardControl> children)
        => new("children", () => layout.Children, value => layout.Children = value, children);

    private static ValueEdit<IReadOnlyDictionary<string, Binding>> SlotsEdit(ParameterControl control, IReadOnlyDictionary<string, Binding> slots)
        => new("bindings", () => control.Slots, value => control.Slots = value, slots);

    // the device and the variables a drop adds to the dashboard
    private static IEnumerable<IEdit> AdditionEdits(Dashboard dashboard, DropPlan plan)
    {
        if (plan.Device is { } device)
            yield return new ValueEdit<IReadOnlyList<DashboardDevice>>("devices", () => dashboard.Devices, value => dashboard.Devices = value, [.. dashboard.Devices, device]);
        foreach (var group in plan.Variables.GroupBy(variable => variable.Owner))
        {
            var owner = group.Key;
            yield return new ValueEdit<IReadOnlyList<IndexVariable>>("variables", () => owner.Variables, value => owner.Variables = value,
                [.. owner.Variables, .. group.Select(variable => variable.Variable)]);
        }
    }

    // the dashboard lists a device of the rig it is bound to; null when it does, or the rig does not have it
    private IEdit? DeviceEdit(Dashboard dashboard, string alias)
        => dashboard.FindDevice(alias) is null && _rig?.Find(alias) is { } device
            ? new ValueEdit<IReadOnlyList<DashboardDevice>>("devices", () => dashboard.Devices, value => dashboard.Devices = value,
                [.. dashboard.Devices, new DashboardDevice(device.Alias, device.MapRoot)])
            : null;

    private static string Declared(DropPlan plan)
        => plan.Variables.Count == 0 ? "" : $"; {String.Join(", ", plan.Variables.Select(variable => $"'{SchemaPaths.VariablePrefix}{variable.Variable.Name}'"))} declared on "
            + $"{String.Join(", ", plan.Variables.Select(variable => $"'{ControlCatalog.NameOf(variable.Owner)}'").Distinct())}: bind a number box to it to select the item";

    private static string Others(int moved) => moved > 0 ? $"; {moved} other control(s) moved or grown" : "";

    public void Dispose()
    {
        foreach (var entry in Dashboards)
            entry.Session?.Dispose();
        _ = Midi.DisposeAsync().AsTask();
    }

    // every rig session that is opened is connected, when MIDI is
    partial void OnRigChanged(RigSession? value) => Midi.SetRig(value);

    // Edit mode is always connected; Play mode as chosen
    partial void OnIsEditingChanged(bool value) => Midi.SetEditing(value);

    // the connections changed or the mode switched: the rig tree and the device panel show them
    private void OnMidiChanged()
    {
        foreach (var item in RigTree.Where(item => item.Instance is not null))
            item.Connection = Midi.ConnectionOf(item.Instance!.Alias);
        DeviceProperties?.RefreshConnection();
        LoadDumpCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDashboardChanged(DashboardEntry? value)
    {
        SelectedControl = null;
        RefreshNotifications();
        RefreshDashboardAliases();
        RefreshUsage();
    }

    // the selected dashboard has a new session: shown as a newly selected dashboard
    private void OnSelectedSessionChanged()
    {
        SelectedControl = null;
        OnPropertyChanged(nameof(SelectedSession));
    }

    // a panel shows another control or device with fresh fields (null first: the template is not reused), so that no
    // field of the previous one (a selection, a text being typed) is taken for the next
    partial void OnSelectedControlChanged(DashboardControl? value)
    {
        Properties = null;
        Properties = value is null ? null : new ControlPropertiesViewModel(this, value);
        if (value is not null)
            SelectedRigItem = null;
    }

    partial void OnSelectedRigItemChanged(RigTreeItem? value)
    {
        DeviceProperties = null;
        DeviceProperties = value?.Instance is { } device ? new DevicePropertiesViewModel(this, device) : null;
        if (DeviceProperties is not null)
            SelectedControl = null;
    }

    private void DoDashboardEdit(DashboardEntry entry, IEdit edit)
        => History.Do(new FileEdit(edit, entry.Origin, () =>
        {
            entry.Session?.Refresh();
            OnEdited();
        }));

    // an edit of the rig: the rig is opened again; when it changes the schemas, with a library built again
    private void DoRigEdit(IEdit edit, bool rebuildsLibrary = false)
        => History.Do(new FileEdit(edit, RigOrigin, () =>
        {
            if (rebuildsLibrary)
                RebuildRig();
            else
                ReopenRig();
            OnEdited();
        }));

    // an edit was done or undone: the dashboard is built again, the panels show the new values; a control that is no
    // longer in its dashboard is not selected
    private void OnEdited()
    {
        if (SelectedControl is { } control && EntryOf(control)?.Dashboard is null)
            SelectedControl = null;
        DashboardChanged?.Invoke(this, EventArgs.Empty);
        Properties?.Refresh();
        DeviceProperties?.Refresh();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DashboardName));
        OnPropertyChanged(nameof(DashboardZoom));
        RefreshDashboardAliases();
    }

    // the device aliases of the selected dashboard, each with whether the rig has it
    private void RefreshDashboardAliases()
    {
        DashboardAliases.Clear();
        if (SelectedDashboard is not { Dashboard: var dashboard })
            return;
        foreach (var alias in DeviceAliases.Of(dashboard))
            DashboardAliases.Add(new DashboardAliasViewModel(this, dashboard, alias));
    }

    // a dump is loaded into every device of the rig; each takes the messages that are its data
    private void LoadDump(string name, byte[] dump)
    {
        if (Rig is null)
            return;
        var loaded = new List<string>();
        foreach (var (alias, state) in Rig.Devices)
        {
            try
            {
                var result = state.LoadDump(dump);
                if (result.Decoded > 0)
                    loaded.Add($"{result.Decoded} of {result.Messages} messages into '{alias}'");
            }
            catch (InvalidOperationException exception)
            {
                Report($"'{name}' cannot be loaded into '{alias}'", exception.Message);
            }
        }
        if (loaded.Count == 0)
            Report($"'{name}'", "no message is data of a device of the rig.");
        else
            Status = $"'{name}': {String.Join("; ", loaded)}.";
    }

    // the rig session needs a rig (dashboards are optional); the library is built from the built-in assemblies and the
    // sources of the rig, its devices and all open dashboards; the dashboard sessions are opened on it again
    private void OpenSessions(DashboardEntry? select = null)
    {
        if (_rig is null)
        {
            Status = "Open a rig.";
            return;
        }

        OpenDashboards(RigSession.Open(_rig, BuildLibrary()), select);
        var problems = Notifications.Count(notification => notification.IsProblem);
        var with = problems > 0 ? $" with {problems} problem(s)" : "";
        Status = Dashboards.Count == 0
            ? $"'{RigOrigin.Name}' opened{with}: open its dashboards, or make a new one (+ in Edit mode)."
            : $"{Dashboards.Count} dashboard(s) opened{with}.";
    }

    // the schema library: the built-in assemblies and the sources of the rig, its devices (and one more device, to see
    // what its sources bring) and all open dashboards
    private SchemaLibrary BuildLibrary(DeviceInstance? extra = null)
    {
        var builder = new SchemaLibraryBuilder();
        foreach (var assembly in SchemaLibraryBuilder.CoreAssemblies)
            builder.AddAssembly(assembly);
        builder.AddAssembly(typeof(Jacobi.Midi.Roland.Processors.ProcessorFactory).Assembly);
        builder.AddAssembly(typeof(Jacobi.Midi.Yamaha.Processors.ProcessorFactory).Assembly);
        builder.Add(_rig!.Sources, RigOrigin.Directory);
        foreach (var device in extra is null ? _rig.Devices : _rig.Devices.Append(extra))
            builder.Add(device.Sources, RigOrigin.Directory);
        foreach (var (dashboard, origin) in Dashboards.Select(entry => (entry.Dashboard, entry.Origin)))
            builder.Add(dashboard.Sources, origin.Directory);
        return builder.Build();
    }

    // after an edit of the schemas of the rig: the library is built again and the rig opened on it (the values of
    // the devices are reset); the selected dashboard and control stay selected
    private void RebuildRig()
    {
        if (_rig is null || Rig is null)
            return;
        var control = SelectedControl;
        OpenDashboards(RigSession.Open(_rig, BuildLibrary()), select: null);
        if (control is not null && EntryOf(control)?.Dashboard is not null)
            SelectedControl = control;
    }

    // after an edit of the rig: the rig is opened again with the same library (its devices that stay the same keep
    // their states and values), the dashboards on it; the selected dashboard and control stay selected
    private void ReopenRig()
    {
        if (_rig is null || Rig is null)
            return;
        var control = SelectedControl;
        OpenDashboards(RigSession.Open(_rig, Rig.Library, Rig), select: null);
        if (control is not null && EntryOf(control)?.Dashboard is not null)
            SelectedControl = control;
    }

    // every open dashboard gets a new session on the rig; the selected one (or the one to select) is selected, which is
    // a new selection even when it stays the same dashboard
    private void OpenDashboards(RigSession rig, DashboardEntry? select)
    {
        // none without dashboards: the rig alone is open
        var selected = select ?? (SelectedDashboard is { } current && Dashboards.Contains(current) ? current : Dashboards.FirstOrDefault());
        foreach (var entry in Dashboards)
            CloseSession(entry);

        Rig = rig;
        foreach (var entry in Dashboards)
            OpenSession(entry, rig);
        if (ReferenceEquals(SelectedDashboard, selected))
            OnSelectedSessionChanged();
        else
            SelectedDashboard = selected;
        RefreshRigTree();
        // another rig may have other devices
        RefreshDashboardAliases();
        RefreshNotifications();
    }

    private void OpenSession(DashboardEntry entry, RigSession rig)
    {
        entry.Session = DashboardSession.Open(rig, entry.Dashboard);
        entry.Session.Resolved += OnResolved;
    }

    private void CloseSession(DashboardEntry entry)
    {
        if (entry.Session is not { } session)
            return;
        session.Resolved -= OnResolved;
        session.Dispose();
        entry.Session = null;
    }

    // the devices of the rig and their parameters; the device whose properties are shown stays selected
    private void RefreshRigTree()
    {
        var shown = DeviceProperties?.Device;
        RigTree.Clear();
        foreach (var device in _rig?.Devices ?? [])
            RigTree.Add(RigTreeItem.Of(device, Rig?.Devices.GetValueOrDefault(device.Alias), Midi.ConnectionOf(device.Alias)));
        OnPropertyChanged(nameof(MapRoots));
        if (shown is not null)
            SelectedRigItem = RigTree.FirstOrDefault(item => item.Instance == shown);
        RefreshUsage();
    }

    // the usage marks of the rig tree: what the controls of the selected dashboard are bound to
    private void RefreshUsage()
    {
        foreach (var item in RigTree.Where(item => item.Instance is not null))
            item.MarkUsage(SelectedSession?.UsedPaths(item.Instance!.Alias) ?? new HashSet<string>());
    }

    // an index variable changed or the dashboard was edited: the problems of the dashboard and the parameters its
    // controls are bound to may have changed
    private void OnResolved(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, SelectedSession))
        {
            RefreshNotifications();
            RefreshUsage();
        }
    }

    // the problems of the rig and of the selected dashboard, the information of the schema library (a schema that
    // replaces another), and what was told about files, dumps and edits
    private void RefreshNotifications()
    {
        Notifications.Clear();
        foreach (var problem in (Rig?.Problems ?? []).Concat(SelectedSession?.Problems ?? []))
            Notifications.Add(new Notification(problem, IsProblem: true));
        foreach (var information in Rig?.Library.Problems.Where(problem => !problem.IsError) ?? [])
            Notifications.Add(new Notification($"{information.Source}: {information.Message}", IsProblem: false));
        foreach (var report in _reports)
            Notifications.Add(report);
        OnPropertyChanged(nameof(NotificationsHeader));
    }

    // a problem: something did not work, or will not
    private void Report(string what, string message) => Notify(what, message, isProblem: true);

    // information the user should know, but not a problem
    private void Inform(string what, string message) => Notify(what, message, isProblem: false);

    private void Notify(string what, string message, bool isProblem)
    {
        _reports.Add(new Notification($"{what}: {message}", isProblem));
        RefreshNotifications();
        Status = $"{what}.";
    }

    private async Task<PickedFile?> PickFile(string title, string kind, params string[] patterns)
    {
        if (StorageProvider is null)
            return null;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            FileTypeFilter = [new FilePickerFileType(kind) { Patterns = patterns }],
        });
        if (files.Count == 0)
            return null;

        var file = files[0];
        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);
        return new PickedFile(file.Name, await reader.ReadToEndAsync(), FileOrigin.Of(file));
    }

    // asks the user to confirm discarding the unsaved changes of a file; true when it has none
    private async Task<bool> ConfirmDiscard(FileOrigin origin, string title, string message, string confirmText)
        => !origin.IsChanged || await Confirm("UNSAVED CHANGES", title, $"{message} Continue?", confirmText);

    // shows the confirmation dialog; true when the user confirmed
    private Task<bool> Confirm(string caption, string title, string message, string confirmText)
    {
        var answer = new TaskCompletionSource<bool>();
        ConfirmDialog = new ConfirmViewModel(caption, title, message, confirmText, confirmed =>
        {
            ConfirmDialog = null;
            answer.SetResult(confirmed);
        });
        return answer.Task;
    }

    private async Task<bool> WriteDashboard(bool saveAs)
    {
        if (SelectedDashboard is not { Dashboard: var dashboard, Origin: var origin })
            return false;
        return await SaveFile("Dashboard", origin, saveAs, [dashboard.Sources], format => DashboardFile.Write(dashboard, format));
    }

    private async Task<bool> WriteRig(bool saveAs)
    {
        if (_rig is not { } rig)
            return false;
        var sources = rig.Devices.Select(device => device.Sources).Prepend(rig.Sources);
        return await SaveFile("Rig", RigOrigin, saveAs, [.. sources], format => RigFile.Write(rig, format));
    }

    // writes a file to where it was read from, or where the user picks (Save as, or a file not saved before);
    // the format follows the extension
    private async Task<bool> SaveFile(string kind, FileOrigin origin, bool saveAs, IReadOnlyList<SchemaSources> sources, Func<FileFormat, string> write)
    {
        var file = saveAs || origin.File is null ? await PickSaveFile(kind, origin.Name) : origin.File;
        if (file is null)
            return false;
        try
        {
            var text = write(FileFormats.FromPath(file.Name));
            await using (var stream = await file.OpenWriteAsync())
            {
                if (stream.CanSeek)
                    stream.SetLength(0);
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(text);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Report($"'{file.Name}' cannot be saved", exception.Message);
            return false;
        }

        var saved = FileOrigin.Of(file);
        // relative schema paths are read relative to the file: in another folder they point elsewhere
        if (saved.Directory != origin.Directory && sources.Any(source => source.Schemas.Any(schema => !Path.IsPathRooted(schema))))
            Inform($"'{file.Name}'", $"it refers to schema files by a relative path, which is now relative to {saved.Directory ?? "the new location"}.");
        origin.File = file;
        origin.Name = saved.Name;
        origin.Directory = saved.Directory;
        origin.Changes = 0;
        return true;
    }

    private async Task<IStorageFile?> PickSaveFile(string kind, string name)
    {
        if (StorageProvider is null)
            return null;
        return await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Save {kind.ToLowerInvariant()} as",
            SuggestedFileName = name,
            DefaultExtension = Path.GetExtension(name) is { Length: > 0 } extension ? extension : ".xml",
            FileTypeChoices =
            [
                new FilePickerFileType($"{kind} (XML)") { Patterns = ["*.xml"] },
                new FilePickerFileType($"{kind} (JSON)") { Patterns = ["*.json"] },
            ],
        });
    }

    /// <summary>
    /// An edit of the dashboard or rig of a file: counts the changes of the file and lets the console follow it
    /// (<paramref name="changed"/>) when it is done or undone.
    /// </summary>
    private sealed class FileEdit(IEdit edit, FileOrigin origin, Action changed) : IEdit
    {
        public string Description => edit.Description;

        public void Apply()
        {
            edit.Apply();
            origin.Changes++;
            changed();
        }

        public void Revert()
        {
            edit.Revert();
            origin.Changes--;
            changed();
        }
    }

    /// <summary>A file the user picked: its name, its text and where it is.</summary>
    private sealed record PickedFile(string Name, string Text, FileOrigin Origin);
}
