using System.Globalization;
using Avalonia.Data;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.Console.Core.Connections;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Core;
using Binding = Jacobi.Midi.Console.Core.Dashboards.Binding;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Console.Looks;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// The properties of the device selected in the rig tree, for the panel of Edit mode: its alias, name, map root (one of
/// the address maps of the schema library), device values, endpoint and colours. The user changes them, each change an
/// edit of the rig (see <see cref="MainViewModel.EditDevice"/>). A value that cannot be taken is a validation error of
/// the field. It shows the device's connection (Edit mode is always connected), and tests it with an Identity Request or
/// a request of data; a test changes no values.
/// </summary>
internal sealed partial class DevicePropertiesViewModel(MainViewModel owner, DeviceInstance device) : ViewModelBase
{
    public DeviceInstance Device => device;

    /// <summary>The name dashboards use for the device; unique in the rig.</summary>
    public string Alias
    {
        get => device.Alias;
        set
        {
            var alias = value?.Trim() ?? String.Empty;
            if (alias.Length == 0 || alias.Contains(Binding.DeviceSeparator, StringComparison.Ordinal))
                throw new DataValidationException($"An alias is not empty and has no '{Binding.DeviceSeparator}'.");
            if (owner.FindDevice(alias) is { } other && other != device)
                throw new DataValidationException($"The rig has a device '{alias}' already.");
            owner.RenameDevice(device, alias);
        }
    }

    public string? Name
    {
        get => device.Name;
        set => owner.EditDevice(device, "name", () => device.Name, name => device.Name = name, String.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    /// <summary>The address maps of the schemas, to choose the map root from.</summary>
    public IReadOnlyList<string> MapRoots
        => owner.MapRoots.Contains(device.MapRoot, StringComparer.OrdinalIgnoreCase) ? owner.MapRoots : [device.MapRoot, .. owner.MapRoots];

    public string MapRoot
    {
        get => device.MapRoot;
        set
        {
            if (!String.IsNullOrEmpty(value))
                owner.EditDevice(device, "map root", () => device.MapRoot, mapRoot => device.MapRoot = mapRoot, value);
        }
    }

    /// <summary>The device values as <c>name=value</c>, separated by commas (decimal, <c>0x..</c> or <c>..H</c>).</summary>
    public string DeviceValues
    {
        get => String.Join(", ", device.DeviceValues.Select(value => $"{value.Key}=0x{value.Value.ToString("X2", CultureInfo.InvariantCulture)}"));
        set
        {
            var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in (value ?? String.Empty).Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var equals = part.IndexOf('=');
                var name = equals < 0 ? String.Empty : part[..equals].Trim();
                if (name.Length == 0)
                    throw new DataValidationException($"'{part}' is not 'name=value'.");
                if (!AddressSpace.TryParseNumber(part[(equals + 1)..].Trim(), out var number) || number > Int64.MaxValue)
                    throw new DataValidationException($"'{part}': the value is not a number (decimal, 0x.. or ..H).");
                if (!values.TryAdd(name, (long)number))
                    throw new DataValidationException($"'{name}' is given more than once.");
            }
            if (values.Count != device.DeviceValues.Count || values.Any(entry => device.DeviceValues.GetValueOrDefault(entry.Key, -1) != entry.Value))
                owner.EditDevice(device, "device values", () => device.DeviceValues, deviceValues => device.DeviceValues = deviceValues, values, DeviceValuesText(values));
        }
    }

    /// <summary>
    /// The names to choose the endpoint from: the endpoints MIDI reaches (in MIDI mode), and the device's own endpoint
    /// when it is not one of them.
    /// </summary>
    public IReadOnlyList<string> EndpointNames
    {
        get
        {
            var names = owner.Midi.Endpoints.Select(endpoint => endpoint.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (device.Endpoint is { } endpoint && !names.Contains(endpoint.Name, StringComparer.OrdinalIgnoreCase))
                names.Insert(0, endpoint.Name);
            return names;
        }
    }

    /// <summary>The name of the endpoint the device is connected to; null when it has none (see <see cref="ClearEndpoint"/>).</summary>
    public string? EndpointName
    {
        get => device.Endpoint?.Name;
        set
        {
            // the list is refreshed when the endpoints change: the selection is lost then, the endpoint is not
            if (!String.IsNullOrWhiteSpace(value))
                SetEndpoint(new EndpointReference(value.Trim(), device.Endpoint?.Groups ?? default));
        }
    }

    /// <summary>The device is not connected to MIDI any more.</summary>
    [RelayCommand]
    private void ClearEndpoint() => SetEndpoint(null);

    /// <summary>The 16 UMP groups, one-based, with the names the device's endpoint gives them for sending.</summary>
    public IReadOnlyList<string> SendGroupNames => GroupNames(MidiDirections.Send);

    /// <summary>The 16 UMP groups, one-based, with the names the device's endpoint gives them for receiving.</summary>
    public IReadOnlyList<string> ReceiveGroupNames => GroupNames(MidiDirections.Receive);

    private IReadOnlyList<string> GroupNames(MidiDirections direction)
    {
        var capabilities = owner.Midi.Endpoints.FirstOrDefault(endpoint => String.Equals(endpoint.Name, device.Endpoint?.Name, StringComparison.OrdinalIgnoreCase))?.Capabilities;
        return [.. Enumerable.Range(0, 16).Select(group => capabilities?.GetGroupName(new MidiGroup((byte)group), direction) is { } name ? $"{group + 1} · {name}" : $"{group + 1}")];
    }

    /// <summary>
    /// The UMP group to send to the device on (0-15), the index in <see cref="SendGroupNames"/>; -1 without an endpoint.
    /// The receive group follows as long as it is the same.
    /// </summary>
    public int EndpointSendGroup
    {
        get => device.Endpoint?.Groups.Send.Value ?? -1;
        set
        {
            if (value is >= 0 and < 16 && device.Endpoint is { } endpoint)
            {
                var send = new MidiGroup((byte)value);
                var receive = endpoint.Groups.IsSplit ? endpoint.Groups.Receive : send;
                SetEndpoint(endpoint with { Groups = new MidiGroupPair(send, receive) });
            }
        }
    }

    /// <summary>The UMP group to receive from the device on (0-15), the index in <see cref="ReceiveGroupNames"/>; -1 without an endpoint.</summary>
    public int EndpointReceiveGroup
    {
        get => device.Endpoint?.Groups.Receive.Value ?? -1;
        set
        {
            if (value is >= 0 and < 16 && device.Endpoint is { } endpoint)
                SetEndpoint(endpoint with { Groups = endpoint.Groups with { Receive = new MidiGroup((byte)value) } });
        }
    }

    public bool HasEndpoint => device.Endpoint is not null;

    /// <summary>MIDI is connected (Edit mode is): the endpoints are listed, the device's connection is shown and can be tested.</summary>
    public bool IsConnected => owner.Midi.IsConnected;

    /// <summary>The connection of the device; null when MIDI is not connected.</summary>
    public DeviceConnection? Connection => owner.Midi.ConnectionOf(device.Alias);

    public string ConnectionText => Connection?.Description ?? (owner.Midi.IsAvailable ? "MIDI is not started (see the notifications)." : "MIDI is not available on this platform.");

    public IBrush ConnectionBrush => MidiViewModel.BrushOf(Connection);

    /// <summary>The outcome of the last test of the connection.</summary>
    public string? TestResult
    {
        get;
        private set => SetProperty(ref field, value);
    }

    private bool CanRequestIdentity() => Connection?.EndpointInfo is not null;

    /// <summary>Asks the devices on the endpoint and groups to identify themselves (universal Identity Request); changes nothing.</summary>
    [RelayCommand(CanExecute = nameof(CanRequestIdentity))]
    private async Task RequestIdentity()
    {
        TestResult = "Identity Request sent; waiting for replies…";
        TestResult = await owner.Midi.RequestIdentity(device.Alias);
    }

    private bool CanProbeData() => Connection?.IsOnline == true;

    /// <summary>Checks that the device answers a request of the first parameter of its map; the values are not kept.</summary>
    [RelayCommand(CanExecute = nameof(CanProbeData))]
    private async Task ProbeData()
    {
        TestResult = "Data requested; waiting for the device…";
        TestResult = await owner.Midi.ProbeData(device.Alias);
    }

    /// <summary>Shows the connection as it is now: MIDI started or stopped, the endpoints or the connection changed.</summary>
    public void RefreshConnection()
    {
        OnPropertyChanged(nameof(EndpointNames));
        OnPropertyChanged(nameof(EndpointName));
        OnPropertyChanged(nameof(SendGroupNames));
        OnPropertyChanged(nameof(ReceiveGroupNames));
        OnPropertyChanged(nameof(EndpointSendGroup));
        OnPropertyChanged(nameof(EndpointReceiveGroup));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(Connection));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(ConnectionBrush));
        RequestIdentityCommand.NotifyCanExecuteChanged();
        ProbeDataCommand.NotifyCanExecuteChanged();
    }

    public string AccentColor => ColorText(ColorArea.Accent);

    public string PlateColor => ColorText(ColorArea.Plate);

    public string PanelColor => ColorText(ColorArea.Panel);

    public IBrush? AccentBrush => BrushOf(ColorArea.Accent);

    public IBrush? PlateBrush => BrushOf(ColorArea.Plate);

    public IBrush? PanelBrush => BrushOf(ColorArea.Panel);

    /// <summary>The device has no colour for the area any more: its controls have the look's.</summary>
    [RelayCommand]
    private void ClearColor(ColorArea area) => owner.SetDeviceColor(device.Alias, area, null);

    [RelayCommand]
    private void Remove() => owner.RemoveDevice(device);

    /// <summary>The schema files and assemblies of this device only (its schema, or customisations of it).</summary>
    public IReadOnlyList<SourceViewModel> Sources
        => [.. device.Sources.Schemas.Select(path => new SourceViewModel(path, "schema", () => owner.RemoveDeviceSource(device, path))),
            .. device.Sources.Assemblies.Select(path => new SourceViewModel(path, "assembly", () => owner.RemoveDeviceSource(device, path)))];

    [RelayCommand]
    private Task AddSchemaFile() => owner.AddDeviceSource(device, SchemaSourceKind.Schema);

    [RelayCommand]
    private Task AddAssembly() => owner.AddDeviceSource(device, SchemaSourceKind.Assembly);

    /// <summary>Shows the values as they are now: after an edit or undo.</summary>
    public void Refresh() => OnPropertyChanged(String.Empty);

    private void SetEndpoint(EndpointReference? endpoint)
        => owner.EditDevice(device, "endpoint", () => device.Endpoint, value => device.Endpoint = value, endpoint, endpoint is null ? "not connected" : $"{endpoint.Name} ({endpoint.GroupsText})");

    private static string DeviceValuesText(IReadOnlyDictionary<string, long> values)
        => values.Count == 0 ? "none" : String.Join(", ", values.Select(value => $"{value.Key}={value.Value}"));

    private string ColorText(ColorArea area) => device.Colors[area] ?? "the look's";

    private IBrush? BrushOf(ColorArea area)
        => RgbColor.TryParse(device.Colors[area], out var color) ? new SolidColorBrush(PaletteBrushes.ToColor(color.Value)) : null;
}

/// <summary>A schema file or assembly of the selected device, in the device panel: its name, its path and kind, and a command to remove it.</summary>
internal sealed partial class SourceViewModel(string path, string kind, Action remove)
{
    public string Name => Path.GetFileName(path);

    public string Tip => $"{kind}: {path}";

    [RelayCommand]
    private void Remove() => remove();
}
