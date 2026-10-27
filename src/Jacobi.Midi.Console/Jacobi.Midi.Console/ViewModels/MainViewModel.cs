using System.Collections.ObjectModel;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Files;
using Jacobi.Midi.Console.Core.Parameters;
using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Console.Core.Schemas;
using Jacobi.Midi.Console.Looks;

namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// The view model of the main view: the open rig (a <see cref="RigSession"/> with the device states), the dashboards
/// open on it (one <see cref="DashboardSession"/> each, all sharing the device states), the selected dashboard, the
/// Edit/Play mode and the look. Until the device synchronisation exists, the values come from dumps.
/// Every problem (files, schemas, devices, bindings, dumps) is listed in <see cref="Problems"/>.
/// </summary>
internal sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private const string DemoFolder = "avares://Jacobi.Midi.Console/Assets/Demo/";

    private readonly List<string> _reports = [];
    private Rig? _rig;
    private string? _rigDirectory;
    // the dashboards in the order they were opened, with the folder their relative paths are relative to
    private readonly List<(Dashboard Dashboard, string? Directory)> _dashboards = [];

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

    /// <summary>Edit mode: the layout and bindings are changed (not available yet: the values are locked); otherwise Play.</summary>
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

    /// <summary>
    /// Opens the GP-8 demo: its rig, two dashboards (one patch; two patches side by side) and the bulk dump of 128
    /// patches.
    /// </summary>
    public void OpenDemo()
    {
        try
        {
            _rig = RigFile.Read(ReadAsset("Gp8.rig.xml"), FileFormat.Xml);
            _rigDirectory = null;
            _dashboards.Clear();
            _dashboards.Add((DashboardFile.Read(ReadAsset("Gp8.dashboard.xml"), FileFormat.Xml), null));
            _dashboards.Add((DashboardFile.Read(ReadAsset("Gp8Compare.dashboard.xml"), FileFormat.Xml), null));
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
            _rigDirectory = file.Directory;
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

        _dashboards.Add((dashboard, file.Directory));
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

    public void Dispose()
    {
        foreach (var session in Dashboards)
            session.Dispose();
    }

    partial void OnSelectedDashboardChanged(DashboardSession? value) => RefreshProblems();

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
        builder.Add(_rig.Sources, _rigDirectory);
        foreach (var device in _rig.Devices)
            builder.Add(device.Sources, _rigDirectory);
        foreach (var (dashboard, directory) in _dashboards)
            builder.Add(dashboard.Sources, directory);

        var selected = select ?? Math.Max(0, SelectedDashboard is null ? 0 : Dashboards.IndexOf(SelectedDashboard));
        foreach (var session in Dashboards)
        {
            session.Resolved -= OnResolved;
            session.Dispose();
        }
        Dashboards.Clear();
        _reports.Clear();

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
        var directory = file.TryGetLocalPath() is { } path ? Path.GetDirectoryName(path) : null;
        return new PickedFile(file.Name, await reader.ReadToEndAsync(), directory);
    }

    private static string ReadAsset(string name)
    {
        using var stream = AssetLoader.Open(new Uri(DemoFolder + name));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>A file the user picked: its name, its text and its folder (null in the browser).</summary>
    private sealed record PickedFile(string Name, string Text, string? Directory);
}
