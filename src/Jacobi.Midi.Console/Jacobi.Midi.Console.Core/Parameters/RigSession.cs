using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Console.Core.Devices;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Console.Core.Schemas;

namespace Jacobi.Midi.Console.Core.Parameters;

/// <summary>
/// An open rig: its schema library and a device state per device. The open dashboards share it, so a parameter has
/// one value, whichever dashboard shows it. Devices whose state cannot be created (a map root that is not an address
/// map, schemas that do not compile) are reported in <see cref="Problems"/>. After an edit of the rig it is opened
/// again on the previous session: the devices that are still the same (alias, map root and device values) keep their
/// state, and with it their values.
/// </summary>
public sealed class RigSession
{
    private RigSession(Rig rig, SchemaLibrary library, IReadOnlyDictionary<string, DeviceState> devices, IReadOnlyList<string> problems)
    {
        Rig = rig;
        Library = library;
        Devices = devices;
        Problems = problems;
    }

    public Rig Rig { get; }

    public SchemaLibrary Library { get; }

    /// <summary>The device states by alias (case-insensitive).</summary>
    public IReadOnlyDictionary<string, DeviceState> Devices { get; }

    /// <summary>The problems of the library and the devices.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <param name="previous">The session of the rig before it was edited, with the same library; null to start anew.</param>
    public static RigSession Open(Rig rig, SchemaLibrary library, RigSession? previous = null)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(library);

        var problems = library.Problems.Where(problem => problem.IsError).Select(problem => $"{problem.Source}: {problem.Message}").ToList();
        var devices = new Dictionary<string, DeviceState>(StringComparer.OrdinalIgnoreCase);
        if (library.IsCompiled)
        {
            foreach (var device in rig.Devices)
            {
                if (previous is not null && previous.Library == library && previous.Devices.GetValueOrDefault(device.Alias) is { } kept && IsSame(kept, device))
                {
                    devices[device.Alias] = kept;
                    continue;
                }
                try
                {
                    devices[device.Alias] = new DeviceState(device, library);
                }
                catch (ArgumentException exception)
                {
                    problems.Add($"Device '{device.Alias}': {exception.Message}");
                }
            }
        }
        return new RigSession(rig, library, devices, problems);
    }

    private static bool IsSame(DeviceState state, DeviceInstance device)
        => state.Alias == device.Alias
            && state.MapRoot.Equals(new SchemaName(device.MapRoot))
            && state.DeviceValues.Count == device.DeviceValues.Count
            && device.DeviceValues.All(value => state.DeviceValues.TryGet(value.Key, out var known) && known == value.Value);

    /// <summary>The colours of a device in the rig; none for a device that is not in it.</summary>
    public AreaColors ColorsOf(string alias) => Rig.Find(alias)?.Colors ?? AreaColors.None;
}
