using Jacobi.Midi.Console.Core.Devices;
using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Console.Core.Schemas;

namespace Jacobi.Midi.Console.Core.Parameters;

/// <summary>
/// An open rig: its schema library and a device state per device. The open dashboards share it, so a parameter has
/// one value, whichever dashboard shows it. Devices whose state cannot be created (a map root that is not an address
/// map, schemas that do not compile) are reported in <see cref="Problems"/>.
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

    public static RigSession Open(Rig rig, SchemaLibrary library)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(library);

        var problems = library.Problems.Where(problem => problem.IsError).Select(problem => $"{problem.Source}: {problem.Message}").ToList();
        var devices = new Dictionary<string, DeviceState>(StringComparer.OrdinalIgnoreCase);
        if (library.IsCompiled)
        {
            foreach (var device in rig.Devices)
            {
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

    /// <summary>The colour of a device in the rig; null when it has none.</summary>
    public string? ColorOf(string alias) => Rig.Find(alias)?.Color;
}
