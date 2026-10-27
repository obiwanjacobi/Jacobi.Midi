using System.Collections.ObjectModel;
using Avalonia.Platform;
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

namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// The view model of the main view: the open rig (a <see cref="RigSession"/> with the device states), the dashboards
/// open on it (one <see cref="DashboardSession"/> each, all sharing the device states), the selected dashboard, the
/// Edit/Play mode and the look. Until the device synchronisation exists, the values come from dumps.
/// In Edit mode the user selects a control (<see cref="SelectedControl"/>, its <see cref="Properties"/>) and changes
/// it or its colours (<see cref="IColorEditor"/>): every change is an edit in the <see cref="History"/>, so it can be
/// undone, and marks the dashboard or the rig as changed; Save writes them to their files. Every problem (files,
/// schemas, devices, bindings, dumps) is listed in <see cref="Problems"/>.
/// </summary>
internal sealed partial class MainViewModel : ViewModelBase, IDashboardEditor, IDisposable
{
    private const string DemoFolder = "avares://Jacobi.Midi.Console/Assets/Demo/";

    private readonly List<string> _reports = [];
    private Rig? _rig;
    private FileOrigin _rigOrigin = new();
    // the dashboards in the order they were opened (the order of Dashboards), with where they were read from
    private readonly List<(Dashboard Dashboard, FileOrigin Origin)> _dashboards = [];

    public MainViewModel()
    {
        History.Changed += (_, _) =>
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(UndoTip));
            OnPropertyChanged(nameof(RedoTip));
        };
    }

    /// <summary>The edits of the open dashboards and rig.</summary>
    public EditHistory History { get; } = new();

    public string UndoTip => History.NextUndo is { } edit ? $"Undo {edit.Description} (Ctrl+Z)" : "Nothing to undo";

    public string RedoTip => History.NextRedo is { } edit ? $"Redo {edit.Description} (Ctrl+Y)" : "Nothing to redo";

    /// <summary>The storage provider of the window or browser, for the file pickers; set by the view.</summary>
    public IStorageProvider? StorageProvider { get; set; }

    public IReadOnlyList<Look> Looks => ConsoleLooks.All;

    [ObservableProperty]
    public partial Look SelectedLook { get; set; } = ConsoleLooks.Foundry;

    /// <summary>The open rig; null until a rig and a dashboard are open.</summary>
    [ObservableProperty]
    public partial RigSession? Rig { get; private set; }

    /// <summary>The open dashboards, all on <see cref="Rig"/>.</summary>
    public ObservableCollection<DashboardSession> Dashboards { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyCanExecuteChangedFor(nameof(CloseDashboardCommand))]
    public partial DashboardSession? SelectedDashboard { get; set; }

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

    public ObservableCollection<string> Problems { get; } = [];

    public string Title => SelectedDashboard?.Dashboard.Name ?? "MIDI Console";

    /// <summary>The control selected in Edit mode; null when none is.</summary>
    [ObservableProperty]
    public partial DashboardControl? SelectedControl { get; set; }

    /// <summary>The properties of <see cref="SelectedControl"/>, for the property panel; null when none is selected.</summary>
    [ObservableProperty]
    public partial ControlPropertiesViewModel? Properties { get; private set; }

    Look IColorEditor.Look => SelectedLook;

    void IDashboardEditor.Select(DashboardControl? control) => SelectedControl = control;

    /// <summary>The selected dashboard must be built again: an edit was done or undone.</summary>
    public event EventHandler? DashboardChanged;

    /// <summary>
    /// Opens the GP-8 demo: its rig, two dashboards (one patch; two patches side by side) and the bulk dump of 128
    /// patches.
    /// </summary>
    public void OpenDemo()
    {
        try
        {
            // the demo files are part of the application: they are saved with Save as
            _rig = RigFile.Read(ReadAsset("Gp8.rig.xml"), FileFormat.Xml);
            _rigOrigin = new FileOrigin { Name = "Gp8.rig.xml" };
            History.Clear();
            _reports.Clear();
            _dashboards.Clear();
            foreach (var name in new[] { "Gp8.dashboard.xml", "Gp8Compare.dashboard.xml" })
            {
                var origin = new FileOrigin { Name = name };
                _dashboards.Add((Complete(DashboardFile.Read(ReadAsset(name), FileFormat.Xml), origin), origin));
            }
            OpenSessions();
            using var dump = AssetLoader.Open(new Uri(DemoFolder + "gp8-sysex.syx"));
            using var bytes = new MemoryStream();
            dump.CopyTo(bytes);
            LoadDump("gp8-sysex.syx", bytes.ToArray());
        }
        catch (FileFormatException exception)
        {
            Report("The demo cannot be opened", exception.Message);
        }
    }

    [RelayCommand]
    private async Task OpenRig()
    {
        if (await PickFile("Open rig", "Rig", "*.xml", "*.json") is not { } file)
            return;
        try
        {
            _rig = RigFile.Read(file.Text, FileFormats.FromPath(file.Name));
            _rigOrigin = file.Origin;
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

        _dashboards.Add((Complete(dashboard, file.Origin), file.Origin));
        if (Rig is not null && dashboard.Sources.IsEmpty)
        {
            AddSession(DashboardSession.Open(Rig, dashboard), select: true);
            Status = $"'{dashboard.Name}' opened.";
        }
        else
        {
            // the dashboard brings its own schemas: the library is built again, and with it the device states
            OpenSessions(select: _dashboards.Count - 1);
            if (Rig is not null && !dashboard.Sources.IsEmpty)
                Status = $"'{dashboard.Name}' opened; its schemas were added, so the values of the devices were reset.";
        }
        RefreshProblems();
    }

    [RelayCommand(CanExecute = nameof(CanCloseDashboard))]
    private void CloseDashboard()
    {
        if (SelectedDashboard is not { } session)
            return;
        var index = Dashboards.IndexOf(session);
        _dashboards.RemoveAt(index);
        Dashboards.RemoveAt(index);
        // the edits may be of the closed dashboard
        History.Clear();
        session.Resolved -= OnResolved;
        session.Dispose();
        SelectedDashboard = Dashboards.Count == 0 ? null : Dashboards[Math.Min(index, Dashboards.Count - 1)];
        Status = $"'{session.Dashboard.Name}' closed.";
    }

    private bool CanCloseDashboard() => SelectedDashboard is not null;

    [RelayCommand]
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
        if (_rig is not null && _rigOrigin.IsChanged && await SaveRig(saveAs: false))
            saved.Add(_rigOrigin.Name);
        if (SelectedDashboard is not null && _dashboards[Dashboards.IndexOf(SelectedDashboard)] is { Origin.IsChanged: true } && await SaveDashboard(saveAs: false))
            saved.Add(_dashboards[Dashboards.IndexOf(SelectedDashboard)].Origin.Name);
        if (saved.Count > 0)
            Status = $"Saved {String.Join(" and ", saved.Select(name => $"'{name}'"))}.";
        else if (!_rigOrigin.IsChanged && _dashboards.All(entry => !entry.Origin.IsChanged))
            Status = "Nothing changed.";
    }

    [RelayCommand]
    private async Task SaveDashboardAs()
    {
        if (await SaveDashboard(saveAs: true))
            Status = $"Saved '{_dashboards[Dashboards.IndexOf(SelectedDashboard!)].Origin.Name}'.";
    }

    [RelayCommand]
    private async Task SaveRigAs()
    {
        if (await SaveRig(saveAs: true))
            Status = $"Saved '{_rigOrigin.Name}'.";
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
        History.Do(new FileEdit(edit, _rigOrigin, this));
        Status = $"{edit.Description}: {color?.ToString() ?? "the look's"}. Save to keep it in the rig.";
        // the dashboard's colour of the device wins: say so, the change does not show here
        if (SelectedDashboard?.Dashboard.FindDevice(alias)?.Colors[area] is { } own)
            Report($"Device '{alias}'", $"the dashboard gives it its own {name} colour ({own}), so the rig's colour does not show on it.");
    }

    /// <summary>
    /// Changes a property of a control of the selected dashboard as an edit (nothing when the value is the same):
    /// <paramref name="what"/> names the property for the history and the status, <paramref name="shown"/> the new
    /// value for the status (by default the value itself).
    /// </summary>
    public void EditControl<T>(DashboardControl control, string what, Func<T> get, Action<T> set, T value, string? shown = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        var entry = EntryOf(control);
        if (entry.Dashboard is null)
            return;
        var edit = new ValueEdit<T>($"{what} of '{ControlPropertiesViewModel.NameOf(control)}'", get, set, value);
        if (!edit.Changes)
            return;
        History.Do(new FileEdit(edit, entry.Origin, this));
        Status = $"{edit.Description}: {shown ?? value?.ToString() ?? "default"}. Save to keep it.";
    }

    // the layout in cells needs every size and position: those the file leaves out are given, which changes it
    private Dashboard Complete(Dashboard dashboard, FileOrigin origin)
    {
        var given = GridLayout.Complete(dashboard.Layout);
        if (given > 0)
        {
            origin.Changes++;
            Report($"'{origin.Name}'", $"{given} sizes and positions were missing and are given; save it to keep them.");
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
        if (entry.Dashboard is null)
            return;
        var root = entry.Dashboard.Layout;
        var changes = GridLayout.Place(root, control, at, size);
        var edits = new List<IEdit>();
        foreach (var (changed, placement) in changes)
        {
            if (IsInGrid(root, changed) && changed.At != placement.At)
                edits.Add(new ValueEdit<CellPosition?>("position", () => changed.At, value => changed.At = value, placement.At));
            if (changed.Size != placement.Size)
                edits.Add(new ValueEdit<CellSize?>("size", () => changed.Size, value => changed.Size = value, placement.Size));
        }
        if (edits.Count == 0)
            return;

        var what = changes[control].Size != control.Size ? "size" : "position";
        var edit = new EditGroup($"{what} of '{ControlPropertiesViewModel.NameOf(control)}'", edits);
        History.Do(new FileEdit(edit, entry.Origin, this));
        var others = changes.Count - 1;
        Status = $"{edit.Description}: {at.Column},{at.Row} {size.Columns}×{size.Rows}{(others > 0 ? $"; {others} other control(s) moved or grown" : "")}. Save to keep it.";
    }

    /// <summary>True when the control is in a grid or section: it has a position.</summary>
    public bool IsInGrid(DashboardControl control)
        => EntryOf(control).Dashboard is { } dashboard && IsInGrid(dashboard.Layout, control);

    private static bool IsInGrid(LayoutControl root, DashboardControl control)
        => GridLayout.ParentOf(root, control) is { } parent && GridLayout.IsGrid(parent);

    private (Dashboard Dashboard, FileOrigin Origin) EntryOf(DashboardControl control)
        => _dashboards.FirstOrDefault(entry => Contains(entry.Dashboard.Layout, control));

    private static bool Contains(DashboardControl root, DashboardControl control)
        => root == control || (root is LayoutControl layout && layout.Children.Any(child => Contains(child, control)));

    public void Dispose()
    {
        foreach (var session in Dashboards)
            session.Dispose();
    }

    partial void OnSelectedDashboardChanged(DashboardSession? value)
    {
        SelectedControl = null;
        RefreshProblems();
    }

    partial void OnSelectedControlChanged(DashboardControl? value)
        => Properties = value is null ? null : new ControlPropertiesViewModel(this, value);

    // an edit was done or undone: the dashboard is built again, the properties show the new values
    private void OnEdited()
    {
        DashboardChanged?.Invoke(this, EventArgs.Empty);
        Properties?.Refresh();
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

    // the rig session needs a rig and a dashboard; the library is built from the built-in assemblies and the sources
    // of the rig, its devices and all open dashboards; the dashboard sessions are opened on it again
    private void OpenSessions(int? select = null)
    {
        if (_rig is null || _dashboards.Count == 0)
        {
            Status = _rig is null ? "Open a rig." : "Open a dashboard.";
            return;
        }

        var builder = new SchemaLibraryBuilder();
        foreach (var assembly in SchemaLibraryBuilder.CoreAssemblies)
            builder.AddAssembly(assembly);
        builder.AddAssembly(typeof(Jacobi.Midi.Roland.Processors.ProcessorFactory).Assembly);
        builder.AddAssembly(typeof(Jacobi.Midi.Yamaha.Processors.ProcessorFactory).Assembly);
        builder.Add(_rig.Sources, _rigOrigin.Directory);
        foreach (var device in _rig.Devices)
            builder.Add(device.Sources, _rigOrigin.Directory);
        foreach (var (dashboard, origin) in _dashboards)
            builder.Add(dashboard.Sources, origin.Directory);

        var selected = select ?? Math.Max(0, SelectedDashboard is null ? 0 : Dashboards.IndexOf(SelectedDashboard));
        foreach (var session in Dashboards)
        {
            session.Resolved -= OnResolved;
            session.Dispose();
        }
        Dashboards.Clear();

        Rig = RigSession.Open(_rig, builder.Build());
        foreach (var (dashboard, _) in _dashboards)
            AddSession(DashboardSession.Open(Rig, dashboard), select: false);
        SelectedDashboard = Dashboards[Math.Min(selected, Dashboards.Count - 1)];
        RefreshProblems();
        Status = $"{Dashboards.Count} dashboard(s) opened{(Problems.Count > 0 ? $" with {Problems.Count} problem(s)" : "")}.";
    }

    private void AddSession(DashboardSession session, bool select)
    {
        session.Resolved += OnResolved;
        Dashboards.Add(session);
        if (select)
            SelectedDashboard = session;
    }

    // an index variable changed: the problems of the dashboard may have changed
    private void OnResolved(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, SelectedDashboard))
            RefreshProblems();
    }

    // the problems of the rig, of the selected dashboard and of the files and dumps
    private void RefreshProblems()
    {
        Problems.Clear();
        foreach (var problem in (Rig?.Problems ?? []).Concat(SelectedDashboard?.Problems ?? []).Concat(_reports))
            Problems.Add(problem);
    }

    private void Report(string what, string message)
    {
        _reports.Add($"{what}: {message}");
        RefreshProblems();
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

    private async Task<bool> SaveDashboard(bool saveAs)
    {
        if (SelectedDashboard is not { } session)
            return false;
        var (dashboard, origin) = _dashboards[Dashboards.IndexOf(session)];
        return await SaveFile("Dashboard", origin, saveAs, [dashboard.Sources], format => DashboardFile.Write(dashboard, format));
    }

    private async Task<bool> SaveRig(bool saveAs)
    {
        if (_rig is not { } rig)
            return false;
        var sources = rig.Devices.Select(device => device.Sources).Prepend(rig.Sources);
        return await SaveFile("Rig", _rigOrigin, saveAs, [.. sources], format => RigFile.Write(rig, format));
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
            Report($"'{file.Name}'", $"it refers to schema files by a relative path, which is now relative to {saved.Directory ?? "the new location"}.");
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

    private static string ReadAsset(string name)
    {
        using var stream = AssetLoader.Open(new Uri(DemoFolder + name));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// An edit of the dashboard or rig of a file: counts the changes of the file and builds the dashboard again when
    /// it is done or undone.
    /// </summary>
    private sealed class FileEdit(IEdit edit, FileOrigin origin, MainViewModel owner) : IEdit
    {
        public string Description => edit.Description;

        public void Apply()
        {
            edit.Apply();
            origin.Changes++;
            owner.OnEdited();
        }

        public void Revert()
        {
            edit.Revert();
            origin.Changes--;
            owner.OnEdited();
        }
    }

    /// <summary>A file the user picked: its name, its text and where it is.</summary>
    private sealed record PickedFile(string Name, string Text, FileOrigin Origin);

    /// <summary>
    /// Where an open rig or dashboard was read from: the file to save it to (null when it has none, as the demo), its
    /// folder (the relative paths in it are relative to; null in the browser), and whether it changed since.
    /// </summary>
    private sealed class FileOrigin
    {
        public string Name { get; set; } = "untitled.xml";

        public IStorageFile? File { get; set; }

        public string? Directory { get; set; }

        /// <summary>The edits done since it was read or saved, less those undone; undone edits of before count as well.</summary>
        public int Changes { get; set; }

        public bool IsChanged => Changes != 0;

        public static FileOrigin Of(IStorageFile file)
            => new()
            {
                Name = file.Name,
                File = file,
                Directory = file.TryGetLocalPath() is { } path ? Path.GetDirectoryName(path) : null,
            };
    }
}
