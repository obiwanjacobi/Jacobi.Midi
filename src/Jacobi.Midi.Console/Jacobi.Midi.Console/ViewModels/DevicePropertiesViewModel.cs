using System.Globalization;
using Avalonia.Data;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.Console.Core.Dashboards;
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
/// the field.
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

    /// <summary>The name of the endpoint the device is connected to; empty when it is not connected.</summary>
    public string? EndpointName
    {
        get => device.Endpoint?.Name;
        set => SetEndpoint(String.IsNullOrWhiteSpace(value) ? null : new EndpointReference(value.Trim(), device.Endpoint?.Group ?? 0));
    }

    /// <summary>The UMP group of the endpoint (0-15).</summary>
    public decimal? EndpointGroup
    {
        get => device.Endpoint?.Group;
        set
        {
            if (value is not null && device.Endpoint is { } endpoint)
                SetEndpoint(endpoint with { Group = Math.Clamp((int)Math.Round(value.Value), 0, 15) });
        }
    }

    public bool HasEndpoint => device.Endpoint is not null;

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

    /// <summary>Shows the values as they are now: after an edit or undo.</summary>
    public void Refresh() => OnPropertyChanged(String.Empty);

    private void SetEndpoint(EndpointReference? endpoint)
        => owner.EditDevice(device, "endpoint", () => device.Endpoint, value => device.Endpoint = value, endpoint, endpoint is null ? "not connected" : $"{endpoint.Name} (group {endpoint.Group})");

    private static string DeviceValuesText(IReadOnlyDictionary<string, long> values)
        => values.Count == 0 ? "none" : String.Join(", ", values.Select(value => $"{value.Key}={value.Value}"));

    private string ColorText(ColorArea area) => device.Colors[area] ?? "the look's";

    private IBrush? BrushOf(ColorArea area)
        => RgbColor.TryParse(device.Colors[area], out var color) ? new SolidColorBrush(PaletteBrushes.ToColor(color.Value)) : null;
}
