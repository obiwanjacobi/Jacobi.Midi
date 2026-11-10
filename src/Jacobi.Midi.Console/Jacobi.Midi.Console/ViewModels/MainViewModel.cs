using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Devices;
using Jacobi.Midi.Console.Core.Editing;
using Jacobi.Midi.Console.Core.Files;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Core.Parameters;
using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Console.Core.Schemas;
using Jacobi.Midi.Console.Dashboards;
using Jacobi.Midi.Console.Looks;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Exchange;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// The view model of the main view: the open rig (a <see cref="RigSession"/> with the device states), the dashboards
/// open on it (one <see cref="DashboardSession"/> each, all sharing the device states), the selected dashboard, the
/// Edit/Play mode and the look. The values come from dump files (Offline) or, in Play mode with MIDI, are received from
/// the devices with the exchange the dashboard names for each (<see cref="DashboardDevice.Receive"/>).
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
    // the devices of the dashboard shown whose values were received while online (Play mode with MIDI), for that session
    private readonly HashSet<string> _received = new(StringComparer.OrdinalIgnoreCase);
    private DashboardSession? _receivedFor;
    // a rig is always open: the console starts with a new, empty one (see UseRig)
    private Rig _rig = new();
    /// <summary>Where the rig was read from (no file for a new rig), and whether it changed since.</summary>
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
        // the console starts with a new rig, without dashboards
        UseRig(new Rig(), NewRigOrigin());
        Status = "Add devices to the new rig (+ of the rig in Edit mode) or open a rig (☰ › Open › Rig…); then open its dashboards or make a new one (+ in Edit mode).";
    }

    /// <summary>The edits of the open dashboards and rig.</summary>
    public EditHistory History { get; } = new();

    /// <summary>The mode (Offline or MIDI) and the MIDI connections of the devices of the rig.</summary>
    public MidiViewModel Midi { get; }

    public string UndoTip => History.NextUndo is { } edit ? $"Undo {edit.Description} (Ctrl+Z)" : "Nothing to undo";

    public string RedoTip => History.NextRedo is { } edit ? $"Redo {edit.Description} (Ctrl+Y)" : "Nothing to redo";

    /// <summary>The storage provider of the window or browser, for the file pickers; set by the view.</summary>
    public IStorageProvider? StorageProvider { get; set; }

    /// <summary>The looks to choose the look of the dashboard from (<see cref="DashboardLook"/>: in its properties in Edit mode, the toolbar in Play mode).</summary>
    public IReadOnlyList<Look> Looks => ConsoleLooks.All;

    /// <summary>The look the console shows: that of the selected dashboard (the default look for one without).</summary>
    [ObservableProperty]
    public partial Look ShownLook { get; private set; } = ConsoleLooks.Default;

    /// <summary>The open rig; null until a rig is open.</summary>
    [ObservableProperty]
    public partial RigSession? Rig { get; private set; }

    /// <summary>The open dashboards in the order they were opened, each with where it was read from and its session on <see cref="Rig"/>.</summary>
    public ObservableCollection<DashboardEntry> Dashboards { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(DashboardName), nameof(DashboardZoom), nameof(DashboardLook), nameof(SelectedSession))]
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
            if (!DoDashboardEdit(entry, edit))
                return;
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
            if (!DoDashboardEdit(entry, edit))
                return;
            Status = $"The dashboard is shown at {DashboardZoom:0}% (cells of {cellSize} pixels). Save to keep it.";
        }
    }

    /// <summary>
    /// The look of the selected dashboard (<see cref="Dashboard.Look"/>): changing it is an edit, and shows it. Null
    /// when the dashboard has none, or one the console does not know.
    /// </summary>
    public Look? DashboardLook
    {
        get => ConsoleLooks.Find(SelectedDashboard?.Dashboard.Look);
        set
        {
            if (SelectedDashboard is not { Dashboard: var dashboard } entry || value is null)
                return;
            // done and undone, the look is shown
            var edit = new ValueEdit<string?>("dashboard look", () => dashboard.Look, look =>
            {
                dashboard.Look = look;
                ShownLook = ConsoleLooks.Find(look) ?? ConsoleLooks.Default;
            }, value.Name);
            if (!edit.Changes)
                return;
            if (!DoDashboardEdit(entry, edit))
                return;
            Status = $"The dashboard has the look '{value.Name}'. Save to keep it.";
        }
    }

    // the selected dashboard is shown with its look; one without a look with the default look, which it gets in Edit
    // mode (it changes)
    private void ShowDashboardLook()
    {
        if (SelectedDashboard is not { Dashboard: var dashboard })
            return;
        GiveLook();
        ShownLook = ConsoleLooks.Find(dashboard.Look) ?? ConsoleLooks.Default;
    }

    // Edit mode: the selected dashboard without a look gets the default look
    private void GiveLook()
    {
        if (!IsEditing || SelectedDashboard is not { Dashboard: { Look: null } dashboard, Origin: var origin })
            return;
        dashboard.Look = ConsoleLooks.Default.Name;
        origin.Changes++;
        Inform($"'{origin.Name}'", $"it had no look and is given '{dashboard.Look}'; save it to keep it.");
        OnPropertyChanged(nameof(DashboardLook));
    }

    // a look the console does not know is told once, when the dashboard is opened
    private void CheckLook(Dashboard dashboard, FileOrigin origin)
    {
        if (dashboard.Look is { } look && ConsoleLooks.Find(look) is null)
            Inform($"'{origin.Name}'", $"its look '{look}' is not known; it is shown with '{ConsoleLooks.Default.Name}'.");
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

    /// <summary>The root groups of the schema library (device data first), for the root of a device.</summary>
    public IReadOnlyList<string> MapRoots => Rig?.Library.Roots.Select(name => name.ToString()).ToList() ?? [];

    /// <summary>
    /// True when a root of the schema library is device data: it has a message map of any kind (address map or dump,
    /// see <see cref="SchemaLibrary.KindOf"/>).
    /// </summary>
    public bool IsDeviceData(string root) => Rig?.Library.KindOf(new BinarySerializer.Schema.SchemaName(root)) is not null;

    /// <summary>
    /// The message map of a root of the schema library, any kind (address map or dump): what a device of the root
    /// needs, e.g. the device values to supply. Null when there is none, with the reason in <paramref name="problem"/>.
    /// </summary>
    public MessageMapInfo? MessageMapOf(string root, out string? problem)
    {
        var name = new BinarySerializer.Schema.SchemaName(root);
        problem = null;
        if (Rig?.Library is not { } library)
            problem = "No rig is open.";
        else if (library.MessageMaps is not { } maps)
            problem = "The schemas do not compile; see the notifications.";
        else if (!library.HasRoot(name))
            problem = $"'{root}' is not a root group of the schemas.";
        else
        {
            try
            {
                return maps.Info(name);
            }
            catch (Exception exception) when (exception is MessageMapException or InvalidOperationException)
            {
                problem = exception.Message;
            }
        }
        return null;
    }

    /// <summary>The control selected in Edit mode; null when none is.</summary>
    [ObservableProperty]
    public partial DashboardControl? SelectedControl { get; set; }

    /// <summary>The properties of <see cref="SelectedControl"/>, for the control panel; null when none is selected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSelection))]
    public partial ControlPropertiesViewModel? Properties { get; private set; }

    Look IColorEditor.Look => ShownLook;

    void IDashboardEditor.Select(DashboardControl? control) => SelectedControl = control;

    /// <summary>The selected dashboard must be built again: an edit was done or undone.</summary>
    public event EventHandler? DashboardChanged;

    /// <summary>Opens another rig in place of the open one; when that changed since it was saved, after the user confirmed discarding the changes.</summary>
    [RelayCommand]
    private async Task OpenRig()
    {
        if (!await ConfirmDiscard(RigOrigin, "Open another rig", $"The rig '{RigOrigin.Name}' has unsaved changes. Opening another rig discards them.", "Open"))
            return;
        if (await PickFile("Open rig", "Rig", "*.xml", "*.json") is { } file)
            LoadRig(file);
    }

    /// <summary>Starts a new, empty rig (not saved yet) in place of the open one; when that changed since it was saved, after the user confirmed discarding the changes.</summary>
    [RelayCommand]
    private async Task NewRig()
    {
        if (!await ConfirmDiscard(RigOrigin, "New rig", $"The rig '{RigOrigin.Name}' has unsaved changes. A new rig discards them.", "New rig"))
            return;
        UseRig(new Rig(), NewRigOrigin());
    }

    // a new rig has no file yet: Save asks where
    private static FileOrigin NewRigOrigin() => new() { Name = "New rig.rig.xml" };

    // opens the rig of the file in place of the open one, the open dashboards on it; false when it cannot be read (reported)
    private bool LoadRig(PickedFile file)
    {
        try
        {
            UseRig(RigFile.Read(file.Text, FileFormats.FromPath(file.Name)), file.Origin);
            return true;
        }
        catch (Exception exception) when (exception is FileFormatException or ArgumentException)
        {
            Report($"'{file.Name}' cannot be opened", exception.Message);
            return false;
        }
    }

    // the rig in place of the open one, the open dashboards on it
    private void UseRig(Rig rig, FileOrigin origin)
    {
        _rig = rig;
        RigOrigin = origin;
        // the edits and reports may be of the rig that was open
        History.Clear();
        _reports.Clear();
        ClearReportsCommand.NotifyCanExecuteChanged();
        OpenSessions();
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

        CheckLook(dashboard, file.Origin);
        await OpenRigOf(dashboard, file.Origin);
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

    /// <summary>
    /// Makes sure the rig a dashboard is opened on has its devices: when the open rig does not (or no rig is open), the
    /// rig the dashboard was made with (<see cref="Dashboard.RigPath"/>) is opened, after the user confirmed replacing
    /// the open one. A rig that is not found is located by the user; the dashboard then refers to it there (a change of
    /// the dashboard). Without its rig the dashboard is opened as it is and its problems are reported.
    /// </summary>
    private async Task OpenRigOf(Dashboard dashboard, FileOrigin origin)
    {
        var referred = dashboard.RigPath is { } path ? FilePaths.Resolve(path, origin.Directory) : null;
        if (dashboard.DeviceProblems(_rig).Count == 0)
        {
            if (referred is not null && Path.IsPathRooted(referred) && LocalPath(RigOrigin) is { } open && !FilePaths.AreSame(referred, open))
                Inform($"'{dashboard.Name}'", $"it was made with the rig '{dashboard.RigPath}'; it is opened on '{RigOrigin.Name}', which has its devices.");
            return;
        }
        if (dashboard.RigPath is not { } rigPath)
            return;

        var rigName = Path.GetFileName(rigPath);
        if (!await Confirm("ANOTHER RIG", $"Open the rig '{rigName}'", $"'{dashboard.Name}' was made with the rig '{rigPath}'; the open rig '{RigOrigin.Name}' does not have its devices. Opening its rig replaces the open one.", "Replace")
            || !await ConfirmDiscard(RigOrigin, "Replace the rig", $"The rig '{RigOrigin.Name}' has unsaved changes. Opening another rig discards them.", "Replace"))
            return;

        var file = await ReadFile(referred!);
        var located = false;
        if (file is null)
        {
            if (!await Confirm("RIG NOT FOUND", $"Locate the rig '{rigName}'", $"'{dashboard.Name}' was made with the rig '{rigPath}', which cannot be found. Locate it, or open the dashboard without it.", "Locate")
                || await PickFile($"Locate the rig '{rigName}'", "Rig", "*.xml", "*.json") is not { } picked)
            {
                Report($"'{dashboard.Name}'", $"its rig '{rigPath}' was not found; the dashboard is opened without it.");
                return;
            }
            file = picked;
            located = true;
        }
        if (!LoadRig(file) || !located)
            return;
        // the dashboard refers to the rig where the user found it
        if (LocalPath(file.Origin) is { } found)
        {
            dashboard.RigPath = origin.Directory is { } directory ? FilePaths.RelativeTo(found, directory) : found;
            origin.Changes++;
            Inform($"'{dashboard.Name}'", $"it now refers to the rig '{dashboard.RigPath}'. Save the dashboard to keep it.");
        }
    }

    /// <summary>Opens a new, empty dashboard on the rig, in Edit mode: a grid to drop controls on.</summary>
    [RelayCommand]
    private void NewDashboard()
    {
        var name = "New dashboard";
        for (var number = 2; Dashboards.Any(entry => entry.Dashboard.Name == name); number++)
            name = $"New dashboard {number}";
        var dashboard = new Dashboard { Name = name, Look = ConsoleLooks.Default.Name, Layout = new LayoutControl { Kind = LayoutControlKind.Grid, Size = NewDashboardSize } };
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
        if (RigOrigin.IsChanged && await WriteRig(saveAs: false))
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
        IEdit? edit = null;
        if (History.NextUndo is { } next && Safely($"undo of {next.Description}", () => edit = History.Undo()) && edit is not null)
            Status = $"Undone: {edit.Description}.";
    }

    private bool CanUndo() => History.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        IEdit? edit = null;
        if (History.NextRedo is { } next && Safely($"redo of {next.Description}", () => edit = History.Redo()) && edit is not null)
            Status = $"Redone: {edit.Description}.";
    }

    private bool CanRedo() => History.CanRedo;

    public void SetControlColor(DashboardControl control, ColorArea area, RgbColor? color)
    {
        ArgumentNullException.ThrowIfNull(control);
        EditControl(control, $"{AreaColors.NameOf(area).ToLowerInvariant()} color", () => control.Colors[area],
            value => control.Colors = control.Colors.With(area, value), color?.ToString());
    }

    public void SetDeviceColor(string alias, ColorArea area, RgbColor? color)
    {
        if (_rig.Find(alias) is not { } device)
            return;
        var name = AreaColors.NameOf(area).ToLowerInvariant();
        var edit = new ValueEdit<string?>($"{name} color of device '{alias}'", () => device.Colors[area],
            value => device.Colors = device.Colors.With(area, value), color?.ToString());
        if (!edit.Changes)
            return;
        if (!DoRigEdit(edit))
            return;
        Status = $"{edit.Description}: {color?.ToString() ?? "the look's"}. Save to keep it in the rig.";
        // the dashboard's color of the device wins: say so, the change does not show here
        if (SelectedDashboard?.Dashboard.FindDevice(alias)?.Colors[area] is { } own)
            Inform($"Device '{alias}'", $"the dashboard gives it its own {name} color ({own}), so the rig's color does not show on it.");
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
        if (!DoDashboardEdit(entry, edit))
            return;
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
        var pages = BindingEdits(entry, control, slots, edits);
        if (!DoDashboardEdit(entry, new EditGroup($"binding of '{ControlCatalog.NameOf(control)}'", edits)))
            return;
        Status = $"'{ControlCatalog.NameOf(control)}'.{slot}: {binding?.ToString() ?? "not bound"}{PagesOf(pages)}. Save to keep it.";
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
        if (!DoDashboardEdit(entry, new EditGroup($"device of '{ControlCatalog.NameOf(control)}'", edits)))
            return;
        Status = $"The device of '{ControlCatalog.NameOf(control)}': {alias ?? "inherited"}. Save to keep it.";
    }

    // the layout in cells needs every size and position: those the file leaves out are given, which changes it
    private Dashboard Complete(Dashboard dashboard, FileOrigin origin)
    {
        var given = GridLayout.Complete(dashboard.Layout);
        if (given > 0)
        {
            origin.Changes++;
            Inform($"'{origin.Name}'", $"{given} sizes and positions were missing (or pages did not fit their tabs) and are given; save it to keep them.");
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
        if (size != control.Size && !CanResize(control))
        {
            Status = $"'{ControlCatalog.NameOf(control)}' is a page: it has the size of '{ControlCatalog.NameOf(ParentOf(control)!)}', which is sized itself.";
            return;
        }
        // not smaller than its controls need
        var minimum = GridLayout.MinimumSize(control);
        size = new CellSize(Math.Max(size.Columns, minimum.Columns), Math.Max(size.Rows, minimum.Rows));
        var (edits, moved) = PlacementEdits(entry.Dashboard.Layout, control, at, size);
        if (edits.Count == 0)
            return;

        var what = size != control.Size ? "size" : "position";
        var edit = new EditGroup($"{what} of '{ControlCatalog.NameOf(control)}'", edits);
        if (!DoDashboardEdit(entry, edit))
            return;
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
        if (!DoDashboardEdit(entry, new EditGroup($"add '{ControlCatalog.NameOf(control)}'", edits)))
            return;
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
        var edits = new List<IEdit>(AdditionEdits(entry.Dashboard, plan));
        var pages = BindingEdits(entry, control, slots, edits);
        if (!DoDashboardEdit(entry, new EditGroup($"binding of '{ControlCatalog.NameOf(control)}'", edits)))
            return;
        SelectedControl = control;
        Status = $"'{ControlCatalog.NameOf(control)}'.{plan.Slot}: {plan.Binding}{Declared(plan)}{PagesOf(pages)}. Save to keep it.";
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
        if (parent is SelectorTabsControl)
        {
            Status = $"'{ControlCatalog.NameOf(control)}' is a page of '{ControlCatalog.NameOf(parent)}' and stays there: its pages are chosen in its properties.";
            return;
        }
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
        if (!DoDashboardEdit(entry, new EditGroup($"move of '{ControlCatalog.NameOf(control)}'", edits)))
            return;
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
        if (parent is SelectorTabsControl tabs && !SelectorPages.CanRemove(tabs, control))
        {
            Status = $"'{ControlCatalog.NameOf(control)}' is the only page of '{ControlCatalog.NameOf(tabs)}' and stays: delete '{ControlCatalog.NameOf(tabs)}' itself to remove both.";
            return;
        }
        if (!DoDashboardEdit(entry, new EditGroup($"delete '{ControlCatalog.NameOf(control)}'", [ChildrenEdit(parent, [.. parent.Children.Where(child => child != control)])])))
            return;
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
        // selector tabs get a page per option of the parameter the control is bound to
        var options = control is ParameterControl bound ? entry.Session?.ValueOf(bound, SlotNames.Value)?.Info.Options : null;
        if (!ControlCatalog.KindsOf(control, parent).Contains(kind))
        {
            Status = $"'{ControlCatalog.NameOf(control)}' cannot be a {kind}{(parent is SelectorTabsControl ? $": it is a page of '{ControlCatalog.NameOf(parent)}', and a page is a grid" : "")}.";
            return;
        }
        var changed = ControlCatalog.WithKind(control, kind, options, parent);
        var lost = control is ParameterControl before && changed is ParameterControl after ? before.Slots.Count - after.Slots.Count : 0;
        var lostPages = control is SelectorTabsControl ? control.Children.Count - changed.Children.Count : 0;
        var children = parent.Children.Select(child => child == control ? changed : child).ToList();
        List<IEdit> edits = [ChildrenEdit(parent, children)];
        // selector tabs get at least their default size, for room on their pages (pushing others aside, the pages follow)
        var others = 0;
        if (changed is SelectorTabsControl && changed.Size is { } size)
        {
            var least = GridLayout.DefaultSize(ParameterControlKind.SelectorTabs);
            var grown = new CellSize(Math.Max(size.Columns, least.Columns), Math.Max(size.Rows, least.Rows));
            if (grown != size)
            {
                var (placements, moved) = WithChildren([(parent, children)], () => PlacementEdits(entry.Dashboard.Layout, changed, changed.At ?? default, grown));
                edits.AddRange(placements);
                // its pages grow along: they are no others
                others = Math.Max(0, moved - changed.Children.Count);
            }
        }
        if (!DoDashboardEdit(entry, new EditGroup($"kind of '{ControlCatalog.NameOf(control)}'", edits)))
            return;
        SelectedControl = changed;
        Status = $"'{ControlCatalog.NameOf(control)}' is a {kind}{Others(others)}"
            + (lost > 0 ? $"; {lost} binding(s) of slots a {kind} does not have are gone" : "")
            + (lostPages > 0 ? $"; its {lostPages} page(s) and the controls on them are gone (Ctrl+Z brings them back)" : "")
            + (changed is SelectorTabsControl { Children.Count: > 0 } ? PagesOf(changed.Children) : "")
            + ". Save to keep it.";
    }

    /// <summary>The control that holds the control; null for the root of its dashboard (or a control in none).</summary>
    public DashboardControl? ParentOf(DashboardControl control)
        => EntryOf(control)?.Dashboard is { } dashboard ? GridLayout.ParentOf(dashboard.Layout, control) : null;

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

    /// <summary>The options of the parameter the control's <c>value</c> slot is bound to; null when it has none (yet).</summary>
    public IReadOnlyList<string>? OptionsOf(ParameterControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return EntryOf(control)?.Session?.ValueOf(control, SlotNames.Value)?.Info.Options;
    }

    /// <summary>
    /// Gives selector tabs a page for an option (in the order of the options) or takes the page of an option (or a page
    /// of no option) away with the controls on it, as one edit.
    /// </summary>
    public void SetPage(SelectorTabsControl tabs, string name, bool hasPage)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(name);
        var entry = EntryOf(tabs);
        if (entry is null)
            return;
        var pages = hasPage
            ? OptionsOf(tabs) is { } options ? SelectorPages.WithPage(tabs, options, name) : null
            : SelectorPages.WithoutPage(tabs, name);
        if (pages is null)
            return;
        if (!DoDashboardEdit(entry, new EditGroup($"pages of '{ControlCatalog.NameOf(tabs)}'", [ChildrenEdit(tabs, pages)])))
            return;
        Status = hasPage
            ? $"'{ControlCatalog.NameOf(tabs)}' has a page for '{name}'. Save to keep it."
            : $"The page '{name}' of '{ControlCatalog.NameOf(tabs)}' is gone with its controls (Ctrl+Z brings it back). Save to keep it.";
    }

    /// <summary>True when the control is the root of its dashboard.</summary>
    public bool IsRoot(DashboardControl control) => Dashboards.Any(entry => entry.Dashboard.Layout == control);

    /// <summary>True when the control can be sized: not a page of tabs or selector tabs (it has the size of its tabs).</summary>
    public bool CanResize(DashboardControl control)
        => ParentOf(control) is not { } parent || !GridLayout.HasPages(parent);

    /// <summary>True when the control can be deleted: not the root of its dashboard, nor the only page of selector tabs.</summary>
    public bool CanDelete(DashboardControl control)
        => !IsRoot(control) && (ParentOf(control) is not SelectorTabsControl tabs || SelectorPages.CanRemove(tabs, control));

    /// <summary>The device, base and variables a control inherits from the layout controls around it.</summary>
    public BindingScope ScopeAround(DashboardControl control)
        => EntryOf(control)?.Dashboard is { } dashboard && GridLayout.ParentOf(dashboard.Layout, control) is { } parent
            ? BindingScope.Of(dashboard.Layout, parent) ?? BindingScope.None
            : BindingScope.None;

    public bool IsDeviceOfRig(string alias) => _rig.Find(alias) is not null;

    public DeviceInstance? FindDevice(string alias) => _rig.Find(alias);

    /// <summary>
    /// The names of the exchanges in the exchange file of the map root of the rig's device with the alias, to receive
    /// and send with (<see cref="DashboardDevice.Receive"/>, <see cref="DashboardDevice.Send"/>); empty when the rig has
    /// no such device or there is no exchange file for its map root.
    /// </summary>
    public IReadOnlyList<string> ExchangeNamesOf(string alias)
        => _rig.Find(alias) is { } device && Rig?.Library.Exchanges.Find(new BinarySerializer.Schema.SchemaName(device.MapRoot)) is { } set
            ? [.. set.Exchanges.Select(exchange => exchange.Name)]
            : [];

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
        if (!DoRigEdit(edit))
            return;
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
        if (!DoDashboardEdit(entry, new EditGroup($"device alias '{alias}'", edits)))
            return;
        Status = $"Device '{alias}' is renamed to '{newAlias}' in '{dashboard.Name}'"
            + (IsDeviceOfRig(newAlias) ? "" : $"; the rig has no device '{newAlias}'") + ". Save to keep it.";
    }

    /// <summary>
    /// Sets the exchange the dashboard receives the values of a device with (<see cref="DashboardDevice.Receive"/>), as an
    /// edit of the dashboard; null or empty: none. A device the dashboard does not list yet is added, with the map root of
    /// the rig's device.
    /// </summary>
    /// <exception cref="ArgumentException">The dashboard does not list the device and the rig has no device with the alias.</exception>
    public void SetDashboardReceive(Dashboard dashboard, string alias, string? exchange)
    {
        exchange = String.IsNullOrWhiteSpace(exchange) ? null : exchange.Trim();
        if (EditDashboardDevice(dashboard, alias, $"receive of '{alias}'", device => device with { Receive = exchange }))
        {
            Status = (exchange is null ? $"'{alias}' is not received from on '{dashboard.Name}'" : $"'{alias}' is received with '{exchange}' on '{dashboard.Name}'")
                + ". Save to keep it.";
        }
    }

    /// <summary>
    /// Sets the exchange the dashboard sends the changes of a device's values with (<see cref="DashboardDevice.Send"/>), as
    /// an edit of the dashboard; null or empty: none. A device the dashboard does not list yet is added, with the map root
    /// of the rig's device.
    /// </summary>
    /// <exception cref="ArgumentException">The dashboard does not list the device and the rig has no device with the alias.</exception>
    public void SetDashboardSend(Dashboard dashboard, string alias, string? exchange)
    {
        exchange = String.IsNullOrWhiteSpace(exchange) ? null : exchange.Trim();
        if (EditDashboardDevice(dashboard, alias, $"send of '{alias}'", device => device with { Send = exchange }))
        {
            Status = (exchange is null ? $"'{alias}' is not sent to on '{dashboard.Name}'" : $"'{alias}' is sent to with '{exchange}' on '{dashboard.Name}'")
                + ". Save to keep it.";
        }
    }

    // changes a device of the dashboard as one edit; one the dashboard does not list yet is added with the rig's map root;
    // false when nothing changed (or the edit failed)
    private bool EditDashboardDevice(Dashboard dashboard, string alias, string what, Func<DashboardDevice, DashboardDevice> change)
    {
        if (Dashboards.FirstOrDefault(entry => entry.Dashboard == dashboard) is not { } entry)
            return false;
        var listed = dashboard.FindDevice(alias);
        var device = listed
            ?? (_rig.Find(alias) is { } instance ? new DashboardDevice(instance.Alias, instance.MapRoot) : null)
            ?? throw new ArgumentException($"The dashboard does not list '{alias}' and the rig has no device with this alias.", nameof(alias));
        var changed = change(device);
        if (listed is not null && changed == listed)
            return false;
        IReadOnlyList<DashboardDevice> devices = listed is not null
            ? [.. dashboard.Devices.Select(other => other == listed ? changed : other)]
            : [.. dashboard.Devices, changed];
        return DoDashboardEdit(entry, new ValueEdit<IReadOnlyList<DashboardDevice>>(what, () => dashboard.Devices, value => dashboard.Devices = value, devices));
    }

    /// <summary>
    /// Adds a device for an address map of the schema library to the rig, named after the map (<c>Gp8.AddressMap</c>:
    /// <c>gp8</c>); it is selected.
    /// </summary>
    [RelayCommand]
    private void AddDevice(string mapRoot)
    {
        var rig = _rig;
        var device = new DeviceInstance { Alias = AliasFor(mapRoot), MapRoot = mapRoot };
        if (!DoRigEdit(new ValueEdit<IReadOnlyList<DeviceInstance>>($"add device '{device.Alias}'", () => rig.Devices, value => rig.Devices = value, [.. rig.Devices, device])))
            return;
        SelectedRigItem = RigTree.FirstOrDefault(item => item.Instance == device);
        Status = $"Device '{device.Alias}' ({device.MapRoot}) added. Save to keep it in the rig.";
    }

    /// <summary>
    /// Adds a schema file or an assembly the user picks to the schemas of the rig (<see cref="Rig.Sources"/>): the
    /// library is built again with it (the values of the devices are reset) and the root groups it brings are offered
    /// by the + of the rig; no device is added.
    /// </summary>
    public async Task AddRigSource(SchemaSourceKind kind)
    {
        if (await PickSource(kind) is not { } path)
            return;
        var rig = _rig;
        var name = Path.GetFileName(path);
        if (rig.Sources.Schemas.Concat(rig.Sources.Assemblies).Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            Status = $"The rig has '{name}' already.";
            return;
        }
        var sources = SourcesWith(rig.Sources, kind, path);
        // the root groups of the documents the file brings (also those that replace a built-in schema of the same
        // name, which is told in the notifications); a file that brings none is not added
        var library = BuildLibrary(sources);
        var brought = kind == SchemaSourceKind.Assembly ? library.RootsFromAssembly(AssemblyNameOf(path)) : library.RootsFromFile(path);
        if (brought.Count == 0)
        {
            var problems = library.Problems.Where(problem => problem.IsError).Select(problem => $"{problem.Source}: {problem.Message}").ToList();
            Report($"'{name}'", problems.Count > 0
                ? $"it is not added to the rig: {String.Join(" ", problems)}"
                : "it has no root group: it is not added to the rig. Add it to a device in its panel when that device's schema needs it.");
            return;
        }
        if (!DoRigEdit(new ValueEdit<SchemaSources>("schemas of the rig", () => rig.Sources, value => rig.Sources = value, sources), rebuildsLibrary: true))
            return;
        Status = $"'{name}' added to the rig: its {brought.Count} root group(s) can be added as a device with the + of the rig; the schemas were built again, so the values of the devices were reset. Save to keep it in the rig.";
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
        if (!DoRigEdit(new ValueEdit<SchemaSources>($"schemas of device '{device.Alias}'", () => device.Sources, value => device.Sources = value, sources), rebuildsLibrary: true))
            return;
        Status = $"Device '{device.Alias}': {shown}; the schemas were built again, so the values of the devices were reset. Save to keep it in the rig.";
    }

    // the alias of a new device: the first part of its map root in lower case (Gp8.AddressMap: gp8), unique in the rig
    private string AliasFor(string mapRoot)
    {
        var name = mapRoot.Split('.')[0].ToLowerInvariant();
        if (!DeviceAliases.IsValid(name))
            name = "device";
        var alias = name;
        for (var number = 2; _rig.Find(alias) is not null; number++)
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
        var rig = _rig;
        if (!DoRigEdit(new ValueEdit<IReadOnlyList<DeviceInstance>>($"remove device '{device.Alias}'", () => rig.Devices, value => rig.Devices = value, [.. rig.Devices.Where(other => other != device)])))
            return;
        DeviceProperties = null;
        Status = $"Device '{device.Alias}' removed (Ctrl+Z brings it back). Save to keep it in the rig.";
        foreach (var dashboard in Dashboards.Select(entry => entry.Dashboard).Where(dashboard => DeviceAliases.Of(dashboard).Contains(device.Alias, StringComparer.OrdinalIgnoreCase)))
            Report($"'{dashboard.Name}'", $"it uses device '{device.Alias}', which is no longer in the rig.");
    }

    private static bool IsInGrid(LayoutControl root, DashboardControl control)
        => GridLayout.ParentOf(root, control) is LayoutControl parent && GridLayout.IsGrid(parent);

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

    // runs a function on the dashboard as it will be with the children of some controls changed
    private static T WithChildren<T>(IReadOnlyList<(DashboardControl Holder, IReadOnlyList<DashboardControl> Children)> changes, Func<T> function)
    {
        var before = changes.Select(change => change.Holder.Children).ToList();
        try
        {
            foreach (var (holder, children) in changes)
                holder.Children = children;
            return function();
        }
        finally
        {
            for (var index = changes.Count - 1; index >= 0; index--)
                changes[index].Holder.Children = before[index];
        }
    }

    private static ValueEdit<IReadOnlyList<DashboardControl>> ChildrenEdit(DashboardControl holder, IReadOnlyList<DashboardControl> children)
        => new("children", () => holder.Children, value => holder.Children = value, children);

    // the edits of binding the slots of a control anew, added to the edits: selector tabs bound to another field get a
    // page per option of it (SelectorPages.Rebound); returns those pages, null when the pages stay
    private static IReadOnlyList<DashboardControl>? BindingEdits(DashboardEntry entry, ParameterControl control, IReadOnlyDictionary<string, Binding> slots, List<IEdit> edits)
    {
        edits.Add(SlotsEdit(control, slots));
        if (control is not SelectorTabsControl tabs || entry.Session is not { } session)
            return null;
        var before = control.Slots.GetValueOrDefault(SlotNames.Value) is { } old ? session.FieldOf(control, old) : null;
        var after = slots.GetValueOrDefault(SlotNames.Value) is { } binding ? session.FieldOf(control, binding) : null;
        if (SelectorPages.Rebound(tabs, before, after) is not { } pages)
            return null;
        edits.Add(ChildrenEdit(tabs, pages));
        return pages;
    }

    private static string PagesOf(IReadOnlyList<DashboardControl>? pages)
        => pages is null ? "" : $"; the pages are those of its {pages.Count} options";

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
        => dashboard.FindDevice(alias) is null && _rig.Find(alias) is { } device
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

    // the changes the user makes to the values of the rig's devices are sent to them
    partial void OnRigChanged(RigSession? oldValue, RigSession? newValue)
    {
        foreach (var state in oldValue?.Devices.Values ?? [])
            state.ValueChanged -= OnDeviceValueChanged;
        foreach (var state in newValue?.Devices.Values ?? [])
            state.ValueChanged += OnDeviceValueChanged;
    }

    // Play mode with MIDI: a value the user changed is sent to the device with the exchange the dashboard shown names
    // for it; values from the device or a dump file are not sent back
    private void OnDeviceValueChanged(object? sender, ValueChangedEventArgs change)
    {
        if (change.Origin != ValueOrigin.User || sender is not DeviceState state)
            return;
        if (IsEditing || !Midi.IsMidi || SelectedSession?.Dashboard.FindDevice(state.Alias)?.Send is not { } exchange)
            return;
        Midi.Send(state.Alias, exchange, new ParameterReference(state.Alias, change.Path, change.Instance));
    }

    // Edit mode is always connected; Play mode as chosen
    // a dashboard without a look gets one in Edit mode
    partial void OnIsEditingChanged(bool value)
    {
        Midi.SetEditing(value);
        GiveLook();
    }

    // the connections changed or the mode switched: the rig tree and the device panel show them
    private void OnMidiChanged()
    {
        foreach (var item in RigTree.Where(item => item.Instance is not null))
            item.Connection = Midi.ConnectionOf(item.Instance!.Alias);
        DeviceProperties?.RefreshConnection();
        LoadDumpCommand.NotifyCanExecuteChanged();
        RequestDataCommand.NotifyCanExecuteChanged();
        ReceiveFromDevices();
    }

    partial void OnSelectedDashboardChanged(DashboardEntry? value)
    {
        SelectedControl = null;
        RefreshNotifications();
        RefreshDashboardAliases();
        RefreshUsage();
        ShowDashboardLook();
        RequestDataCommand.NotifyCanExecuteChanged();
        ReceiveFromDevices();
    }

    // the selected dashboard has a new session: shown as a newly selected dashboard
    private void OnSelectedSessionChanged()
    {
        SelectedControl = null;
        OnPropertyChanged(nameof(SelectedSession));
        RequestDataCommand.NotifyCanExecuteChanged();
        ReceiveFromDevices();
    }

    // Play mode with MIDI: the values of a device of the dashboard shown are received when it comes online, and those of
    // all its online devices when another dashboard (or session) is shown; one dashboard at a time
    private void ReceiveFromDevices()
    {
        if (IsEditing || !Midi.IsMidi || SelectedSession is not { } session)
        {
            _received.Clear();
            _receivedFor = null;
            return;
        }
        if (!ReferenceEquals(session, _receivedFor))
        {
            _received.Clear();
            _receivedFor = session;
        }
        foreach (var device in session.Dashboard.Devices)
        {
            if (Midi.ConnectionOf(device.Alias)?.IsOnline != true)
            {
                // received again when it comes back online
                _received.Remove(device.Alias);
                continue;
            }
            if (_received.Add(device.Alias))
                _ = Receive(session, device, data: null);
        }
    }

    private bool CanRequestData() => !IsEditing && Midi.IsMidi && SelectedSession is not null;

    // the toolbar: the values of the dashboard shown requested again from its devices, fresh from the device; a device
    // that is not online is reported in the notifications
    [RelayCommand(CanExecute = nameof(CanRequestData))]
    private async Task RequestData()
    {
        if (SelectedSession is not { } session)
            return;
        foreach (var device in session.Dashboard.Devices)
            await Receive(session, device, SessionData.Device);
    }

    // receives the values of the parameters of the dashboard bound to the device, with the exchange the dashboard names
    private async Task Receive(DashboardSession session, DashboardDevice device, SessionData? data)
    {
        var (alias, exchange) = (device.Alias, device.Receive);
        if (exchange is null)
        {
            Inform($"Device '{alias}'", "the dashboard names no exchange to receive its values with: they are not received.");
            return;
        }
        var parameters = session.ParametersOf(alias);
        if (parameters.Count == 0)
            return;
        Status = $"Receiving '{alias}'…";
        // a failure that keeps the exchange from running is reported by the MIDI view model
        if (await Midi.Receive(alias, exchange, parameters, data) is not { } result)
            return;
        if (result.Failed is { } failed)
        {
            var notRequested = result.NotRequested;
            Report($"Receiving '{alias}' with '{exchange}'",
                $"{failed.Parameter.Path}{failed.Parameter.Instance}: {MidiViewModel.Describe(failed.Result)}"
                + (notRequested > 0 ? $"; {notRequested} parameter{(notRequested == 1 ? " was" : "s were")} not requested after it." : "."));
        }
        else
        {
            Status = $"Received {parameters.Count} parameter{(parameters.Count == 1 ? "" : "s")} from '{alias}'.";
        }
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

    // false when the edit failed: it is reported and not done
    private bool DoDashboardEdit(DashboardEntry entry, IEdit edit)
        => Safely(edit.Description, () => History.Do(new FileEdit(edit, entry.Origin, () =>
        {
            entry.Session?.Refresh();
            OnEdited();
        })));

    // an edit of the rig: the rig is opened again; when it changes the schemas, with a library built again
    private bool DoRigEdit(IEdit edit, bool rebuildsLibrary = false)
        => Safely(edit.Description, () => History.Do(new FileEdit(edit, RigOrigin, () =>
        {
            if (rebuildsLibrary)
                RebuildRig();
            else
                ReopenRig();
            OnEdited();
        })));

    // an edit (or undo, redo) that fails is not done: it is reported, the app goes on; true when it was done
    private bool Safely(string what, Action edit)
    {
        try
        {
            edit();
            return true;
        }
        catch (Exception exception)
        {
            Report($"'{what}' failed and is not done", $"{exception.Message} ({exception.GetType().Name})");
            // the sessions and views show the dashboards and rig as they are again (the failure may have been there)
            try
            {
                foreach (var entry in Dashboards)
                    entry.Session?.Refresh();
                OnEdited();
            }
            catch (Exception again)
            {
                Report("The dashboard could not be shown again", $"{again.Message} ({again.GetType().Name})");
            }
            return false;
        }
    }

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
        OnPropertyChanged(nameof(DashboardLook));
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

    // the rig session (dashboards are optional); the library is built from the built-in assemblies and the sources of
    // the rig, its devices and all open dashboards; the dashboard sessions are opened on it again
    private void OpenSessions(DashboardEntry? select = null)
    {
        OpenDashboards(RigSession.Open(_rig, BuildLibrary()), select);
        var problems = Notifications.Count(notification => notification.IsProblem);
        var with = problems > 0 ? $" with {problems} problem(s)" : "";
        Status = Dashboards.Count == 0
            ? $"'{RigOrigin.Name}' opened{with}: open its dashboards, or make a new one (+ in Edit mode)."
            : $"{Dashboards.Count} dashboard(s) opened{with}.";
    }

    // the schema library: the built-in assemblies and the sources of the rig (or other sources of the rig, to see what
    // they bring), its devices and all open dashboards
    private SchemaLibrary BuildLibrary(SchemaSources? rigSources = null)
    {
        var builder = new SchemaLibraryBuilder();
        foreach (var assembly in SchemaLibraryBuilder.CoreAssemblies)
            builder.AddAssembly(assembly);
        builder.AddAssembly(typeof(Jacobi.Midi.Roland.Processors.ProcessorFactory).Assembly);
        builder.AddAssembly(typeof(Jacobi.Midi.Yamaha.Processors.ProcessorFactory).Assembly);
        builder.Add(rigSources ?? _rig.Sources, RigOrigin.Directory);
        foreach (var device in _rig.Devices)
            builder.Add(device.Sources, RigOrigin.Directory);
        foreach (var (dashboard, origin) in Dashboards.Select(entry => (entry.Dashboard, entry.Origin)))
            builder.Add(dashboard.Sources, origin.Directory);
        return builder.Build();
    }

    // after an edit of the schemas of the rig: the library is built again and the rig opened on it (the values of
    // the devices are reset); the selected dashboard and control stay selected
    private void RebuildRig()
    {
        if (Rig is null)
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
        if (Rig is null)
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
        foreach (var device in _rig.Devices)
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
        _reports.Add(new Notification($"{what}: {message}", isProblem, IsReport: true));
        RefreshNotifications();
        ClearReportsCommand.NotifyCanExecuteChanged();
        Status = $"{what}.";
    }

    /// <summary>Removes a report from the notifications; the problems of the current state cannot be dismissed.</summary>
    [RelayCommand]
    private void DismissReport(Notification report)
    {
        if (!_reports.Remove(report))
            return;
        RefreshNotifications();
        ClearReportsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Removes all reports from the notifications; the problems of the current state stay.</summary>
    [RelayCommand(CanExecute = nameof(HasReports))]
    private void ClearReports()
    {
        _reports.Clear();
        RefreshNotifications();
        ClearReportsCommand.NotifyCanExecuteChanged();
    }

    private bool HasReports() => _reports.Count > 0;

    private async Task<PickedFile?> PickFile(string title, string kind, params string[] patterns)
    {
        if (StorageProvider is null)
            return null;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            FileTypeFilter = [new FilePickerFileType(kind) { Patterns = patterns }],
        });
        return files.Count == 0 ? null : await ReadFile(files[0]);
    }

    // a file by its path; null when it is not found or the host has no file paths (the browser)
    private async Task<PickedFile?> ReadFile(string path)
    {
        if (StorageProvider is null || !Path.IsPathRooted(path))
            return null;
        try
        {
            return await StorageProvider.TryGetFileFromPathAsync(path) is { } file ? await ReadFile(file) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Report($"'{path}' cannot be read", exception.Message);
            return null;
        }
    }

    private static async Task<PickedFile> ReadFile(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);
        return new PickedFile(file.Name, await reader.ReadToEndAsync(), FileOrigin.Of(file));
    }

    // the full path of a file; null when it has none (not saved, or the browser)
    private static string? LocalPath(FileOrigin origin) => origin.File?.TryGetLocalPath();

    /// <summary>
    /// Asks the user to confirm quitting the console when the rig or an open dashboard has unsaved changes, naming
    /// them; true when there are none or the user confirmed discarding them. False also while another confirmation is
    /// shown: that one is answered first.
    /// </summary>
    public async Task<bool> ConfirmQuit()
    {
        if (ConfirmDialog is not null)
            return false;
        var changed = Dashboards.Select(entry => entry.Origin).Prepend(RigOrigin)
            .Where(origin => origin.IsChanged)
            .Select(origin => $"'{origin.Name}'")
            .ToList();
        return changed.Count == 0
            || await Confirm("UNSAVED CHANGES", "Quit the console", $"{String.Join(", ", changed)} {(changed.Count == 1 ? "has" : "have")} unsaved changes. Quitting discards them. Continue?", "Quit");
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
        return await SaveFile("Dashboard", origin, saveAs, [dashboard.Sources], (format, directory) =>
        {
            // the dashboard is made with the open rig: it refers to it, when both have a place
            if (LocalPath(RigOrigin) is { } rig && directory is not null)
                dashboard.RigPath = FilePaths.RelativeTo(rig, directory);
            return DashboardFile.Write(dashboard, format);
        });
    }

    private async Task<bool> WriteRig(bool saveAs)
    {
        var rig = _rig;
        var sources = rig.Devices.Select(device => device.Sources).Prepend(rig.Sources);
        return await SaveFile("Rig", RigOrigin, saveAs, [.. sources], (format, _) => RigFile.Write(rig, format));
    }

    // writes a file to where it was read from, or where the user picks (Save as, or a file not saved before);
    // the format follows the extension
    private async Task<bool> SaveFile(string kind, FileOrigin origin, bool saveAs, IReadOnlyList<SchemaSources> sources, Func<FileFormat, string?, string> write)
    {
        var file = saveAs || origin.File is null ? await PickSaveFile(kind, origin.Name) : origin.File;
        if (file is null)
            return false;
        try
        {
            var text = write(FileFormats.FromPath(file.Name), FileOrigin.Of(file).Directory);
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

        public void Apply() => Change(edit.Apply, edit.Revert, +1);

        public void Revert() => Change(edit.Revert, edit.Apply, -1);

        // the views follow the change; when that fails the change is taken back, so the file and the history agree
        private void Change(Action change, Action back, int count)
        {
            change();
            try
            {
                changed();
            }
            catch
            {
                back();
                throw;
            }
            origin.Changes += count;
        }
    }

    /// <summary>A file the user picked: its name, its text and where it is.</summary>
    private sealed record PickedFile(string Name, string Text, FileOrigin Origin);
}
