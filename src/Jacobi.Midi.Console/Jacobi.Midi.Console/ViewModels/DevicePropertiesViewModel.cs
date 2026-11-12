using System.Globalization;
using Avalonia.Data;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.Console.Core.Connections;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Editing;
using Jacobi.Midi.Core;
using Binding = Jacobi.Midi.Console.Core.Dashboards.Binding;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Console.Looks;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// The properties of the device selected in the rig tree, for the panel of Edit mode: its alias, name, map root (one of
/// the address maps of the schema library), device values, endpoint and colors. The user changes them, each change an
/// edit of the rig (see <see cref="MainViewModel.EditDevice"/>). A value that cannot be taken is a validation error of
/// the field. It shows the device's connection (Edit mode is always connected), and tests it with an Identity Request or
/// a request of data; a test changes no values.
/// </summary>
internal sealed partial class DevicePropertiesViewModel(MainViewModel owner, DeviceInstance device) : ViewModelBase
{
    // the message map of the map root (see MapInfo)
    private MessageMapInfo? _mapInfo;
    private bool _mapInfoKnown;
    private string? _mapProblem;

    public DeviceInstance Device => device;

    /// <summary>The name dashboards use for the device; unique in the rig.</summary>
    public string Alias
    {
        get => device.Alias;
        set
        {
            var alias = value?.Trim() ?? String.Empty;
            if (!DeviceAliases.IsValid(alias))
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

    /// <summary>
    /// The device values of the device, one row each, in the order they were given; a row that has no name or value
    /// yet is kept here until it has both (it is not part of the rig before that).
    /// </summary>
    public IReadOnlyList<DeviceValueViewModel> DeviceValues
    {
        get => field ??= [.. device.DeviceValues.Select(value => new DeviceValueViewModel(this, value.Key, FormatValue(value.Value)))];
        private set => SetProperty(ref field, value);
    }

    /// <summary>The names of the device values the map root needs from the device instance (e.g. <c>deviceId</c>), for the droplist.</summary>
    public IReadOnlyList<string> RequiredDeviceValues => MapInfo?.RequiredDeviceValues ?? [];

    /// <summary>
    /// The required device values the device has no value for yet, or why the map root's device values are not known
    /// (its message map is not valid); null when nothing is missing.
    /// </summary>
    public string? DeviceValuesProblem
    {
        get
        {
            var info = MapInfo;
            if (info is null)
                return $"The device values of the map root are not known: {_mapProblem}";
            var missing = info.RequiredDeviceValues.Where(name => !device.DeviceValues.ContainsKey(name)).ToList();
            return missing.Count == 0 ? null : $"The map root needs a value for {String.Join(", ", missing)}.";
        }
    }

    /// <summary>Adds a row for a device value: the first one the map root needs that the device has no row for.</summary>
    [RelayCommand]
    private void AddDeviceValue()
    {
        var name = RequiredDeviceValues.FirstOrDefault(required => !DeviceValues.Any(row => String.Equals(row.Name, required, StringComparison.OrdinalIgnoreCase)));
        DeviceValues = [.. DeviceValues, new DeviceValueViewModel(this, name, null)];
    }

    /// <summary>How the map root uses a device value name: nothing to mark when it needs it; null without a known map.</summary>
    internal string? MarkOf(string name)
    {
        if (MapInfo is not { } info || info.RequiredDeviceValues.Contains(name, StringComparer.OrdinalIgnoreCase))
            return null;
        return info.Device.TryGet(name, out var fixedValue)
            ? $"overrides the map root's {FormatValue(fixedValue)}"
            : "not used by the map root";
    }

    /// <summary>True when another row has the name already.</summary>
    internal bool IsTaken(DeviceValueViewModel row, string name)
        => DeviceValues.Any(other => other != row && String.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A row was changed: the rows that have a name and a value are the device values of the device (one edit of the rig).</summary>
    internal void CommitDeviceValues()
    {
        var values = DeviceValues.Where(row => row.IsComplete).ToDictionary(row => row.Name!, row => row.Number!.Value, StringComparer.OrdinalIgnoreCase);
        if (values.Count != device.DeviceValues.Count || values.Any(entry => device.DeviceValues.GetValueOrDefault(entry.Key, -1) != entry.Value))
            owner.EditDevice(device, "device values", () => device.DeviceValues, deviceValues => device.DeviceValues = deviceValues, values, DeviceValuesText(values));
        else
            OnDeviceValuesChanged();
    }

    /// <summary>Takes a row out; when it was a device value, the device does not have it any more.</summary>
    internal void RemoveDeviceValue(DeviceValueViewModel row)
    {
        var wasComplete = row.IsComplete;
        DeviceValues = [.. DeviceValues.Where(other => other != row)];
        if (wasComplete)
            CommitDeviceValues();
    }

    // the message map of the map root, asked once per refresh (the map root or the schemas may have changed)
    private MessageMapInfo? MapInfo
    {
        get
        {
            if (!_mapInfoKnown)
            {
                _mapInfo = owner.MessageMapOf(device.MapRoot, out _mapProblem);
                _mapInfoKnown = true;
            }
            return _mapInfo;
        }
    }

    // the device values were edited, undone or the map root changed: the rows show the device again, the rows still
    // without a name or value stay
    private void RefreshDeviceValues()
    {
        _mapInfoKnown = false;
        DeviceValues = [.. device.DeviceValues.Select(value => new DeviceValueViewModel(this, value.Key, FormatValue(value.Value))),
            .. DeviceValues.Where(row => !row.IsComplete)];
    }

    private void OnDeviceValuesChanged()
    {
        foreach (var row in DeviceValues)
            row.RefreshMark();
        OnPropertyChanged(nameof(DeviceValuesProblem));
    }

    internal static string FormatValue(long value) => $"0x{value.ToString("X2", CultureInfo.InvariantCulture)}";

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

    /// <summary>The device has no color for the area any more: its controls have the look's.</summary>
    [RelayCommand]
    private void ClearColor(ColorArea area) => owner.SetDeviceColor(device.Alias, area, null);

    [RelayCommand]
    private void Remove() => owner.RemoveDevice(device);

    /// <summary>Shows the values as they are now: after an edit or undo.</summary>
    public void Refresh()
    {
        RefreshDeviceValues();
        OnPropertyChanged(String.Empty);
    }

    private void SetEndpoint(EndpointReference? endpoint)
        => owner.EditDevice(device, "endpoint", () => device.Endpoint, value => device.Endpoint = value, endpoint, endpoint is null ? "not connected" : $"{endpoint.Name} ({endpoint.GroupsText})");

    private static string DeviceValuesText(IReadOnlyDictionary<string, long> values)
        => values.Count == 0 ? "none" : String.Join(", ", values.Select(value => $"{value.Key}={value.Value}"));

    private string ColorText(ColorArea area) => device.Colors[area] ?? "the look's";

    private IBrush? BrushOf(ColorArea area)
        => RgbColor.TryParse(device.Colors[area], out var color) ? new SolidColorBrush(PaletteBrushes.ToColor(color.Value)) : null;
}

/// <summary>
/// One device value of the selected device, in the device panel: its name (one of the map root's required names, or
/// any other) and its value (decimal, <c>0x..</c> or <c>..H</c>), marked when the map root does not need it. A row
/// with a name and a value is part of the device's values; a value that cannot be taken is a validation error.
/// </summary>
internal sealed partial class DeviceValueViewModel(DevicePropertiesViewModel owner, string? name, string? text) : ViewModelBase
{
    private string? _name = name;
    private string? _value = text;

    public string? Name
    {
        get => _name;
        set
        {
            var trimmed = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (String.Equals(trimmed, _name, StringComparison.Ordinal))
                return;
            if (trimmed is not null && owner.IsTaken(this, trimmed))
                throw new DataValidationException($"'{trimmed}' is given already.");
            _name = trimmed;
            RefreshMark();
            owner.CommitDeviceValues();
        }
    }

    public string? Value
    {
        get => _value;
        set
        {
            var trimmed = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (String.Equals(trimmed, _value, StringComparison.Ordinal))
                return;
            long? number = null;
            if (trimmed is not null)
            {
                if (!AddressSpace.TryParseNumber(trimmed, out var parsed) || parsed > Int64.MaxValue)
                    throw new DataValidationException($"'{trimmed}' is not a number (decimal, 0x.. or ..H).");
                number = (long)parsed;
            }
            _value = trimmed;
            Number = number;
            owner.CommitDeviceValues();
        }
    }

    /// <summary>The value as a number; null when there is none.</summary>
    public long? Number { get; private set; } = Parse(text);

    /// <summary>True when the row has a name and a value: it is one of the device's values.</summary>
    public bool IsComplete => _name is not null && Number is not null;

    /// <summary>The names the map root needs, to choose from; any other name can be typed.</summary>
    public IReadOnlyList<string> Names => owner.RequiredDeviceValues;

    /// <summary>How the map root uses the name when it does not need it (e.g. "not used by the map root"); null otherwise.</summary>
    public string? Mark => _name is null ? null : owner.MarkOf(_name);

    public bool HasMark => Mark is not null;

    [RelayCommand]
    private void Remove() => owner.RemoveDeviceValue(this);

    internal void RefreshMark()
    {
        OnPropertyChanged(nameof(Mark));
        OnPropertyChanged(nameof(HasMark));
    }

    private static long? Parse(string? text)
        => text is not null && AddressSpace.TryParseNumber(text, out var parsed) && parsed <= Int64.MaxValue ? (long)parsed : null;
}
