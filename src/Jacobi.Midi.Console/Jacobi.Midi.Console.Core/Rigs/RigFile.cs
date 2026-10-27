using System.Globalization;
using Jacobi.Midi.Console.Core.Files;
using Jacobi.Midi.Console.Core.Schemas;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Console.Core.Rigs;

/// <summary>
/// Reads and writes rigs as XML or JSON. Device values are written in hex (<c>0x10</c>) and read in the notation of
/// the schemas (<c>0x10</c>, <c>10H</c> or decimal). Content that does not fit (unknown attributes or elements,
/// invalid values) is reported with its location, never ignored.
/// </summary>
public static class RigFile
{
    /// <summary>The kind and version of the file, in its <c>format</c> attribute.</summary>
    public const string Format = "jacobi.midi.rig/1";

    private const string RootName = "rig";
    private const string DeviceName = "device";

    public static Rig Read(string text, FileFormat format)
        => FromElement(ConsoleFile.Read(text, format, RootName));

    public static string Write(Rig rig, FileFormat format)
    {
        ArgumentNullException.ThrowIfNull(rig);
        return ConsoleFile.Write(ToElement(rig), format);
    }

    // writing

    private static FileElement ToElement(Rig rig)
    {
        var root = new FileElement(RootName).Set(ConsoleFile.FormatAttribute, Format);
        ConsoleFile.AddSources(root, rig.Sources);

        foreach (var device in rig.Devices)
        {
            var element = root.Add(ConsoleFile.SetColors(new FileElement(DeviceName, FileRoles.Devices)
                .Set("alias", device.Alias)
                .Set("name", device.Name)
                .Set("mapRoot", device.MapRoot), device.Colors));
            ConsoleFile.AddSources(element, device.Sources);
            foreach (var (name, value) in device.DeviceValues)
            {
                element.Add(new FileElement(FileRoles.DeviceValues.ElementName!, FileRoles.DeviceValues)
                    .Set(FileRoles.DeviceValues.KeyAttribute!, name)
                    .Set(FileRoles.DeviceValues.ValueAttribute!, $"0x{value.ToString("X2", CultureInfo.InvariantCulture)}"));
            }
            if (device.Endpoint is { } endpoint)
            {
                element.Add(new FileElement(FileRoles.Endpoint.ElementName!, FileRoles.Endpoint)
                    .Set("name", endpoint.Name)
                    .Set("group", endpoint.Group));
            }
        }
        return root;
    }

    // reading

    private static Rig FromElement(FileElement root)
    {
        ConsoleFile.CheckFormat(root, Format);

        var assemblies = new List<string>();
        var schemas = new List<string>();
        var devices = new List<DeviceInstance>();
        foreach (var child in root.TakeChildren())
        {
            if (ConsoleFile.TryReadSource(child, assemblies, schemas))
            {
                continue;
            }
            if (child.Name == DeviceName)
            {
                var device = ReadDevice(child);
                if (devices.Any(other => String.Equals(other.Alias, device.Alias, StringComparison.OrdinalIgnoreCase)))
                    throw child.Error($"the device alias '{device.Alias}' is used more than once.");
                devices.Add(device);
            }
            else
            {
                throw child.Error($"a rig cannot contain '{child.Name}'.");
            }
        }

        root.EnsureAllRead();
        return new Rig
        {
            Sources = new SchemaSources { Assemblies = assemblies, Schemas = schemas },
            Devices = devices,
        };
    }

    private static DeviceInstance ReadDevice(FileElement element)
    {
        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var assemblies = new List<string>();
        var schemas = new List<string>();
        EndpointReference? endpoint = null;
        foreach (var child in element.TakeChildren())
        {
            if (ConsoleFile.TryReadSource(child, assemblies, schemas))
            {
                continue;
            }
            if (child.Name == FileRoles.DeviceValues.ElementName)
            {
                var name = child.GetRequired(FileRoles.DeviceValues.KeyAttribute!);
                if (values.ContainsKey(name))
                    throw child.Error($"the device value '{name}' is given more than once.");
                values[name] = ReadNumber(child, FileRoles.DeviceValues.ValueAttribute!);
            }
            else if (child.Name == FileRoles.Endpoint.ElementName)
            {
                if (endpoint is not null)
                    throw child.Error("a device has one endpoint.");
                var group = ConsoleFile.GetInt(child, "group", minimum: 0) ?? 0;
                if (group > 15)
                    throw child.Error($"'group': {group} is not a UMP group (0-15).");
                endpoint = new EndpointReference(child.GetRequired("name"), group);
            }
            else
            {
                throw child.Error($"a device cannot contain '{child.Name}'.");
            }
            child.EnsureAllRead();
        }

        var device = new DeviceInstance
        {
            Alias = element.GetRequired("alias"),
            Name = element.Get("name"),
            MapRoot = element.GetRequired("mapRoot"),
            Colors = ConsoleFile.GetColors(element),
            DeviceValues = values,
            Endpoint = endpoint,
            Sources = new SchemaSources { Assemblies = assemblies, Schemas = schemas },
        };
        element.EnsureAllRead();
        return device;
    }

    private static long ReadNumber(FileElement element, string attribute)
    {
        var text = element.GetRequired(attribute);
        return AddressSpace.TryParseNumber(text, out var value) && value <= Int64.MaxValue
            ? (long)value
            : throw element.Error($"'{attribute}': '{text}' is not a number (decimal, 0x.. or ..H).");
    }
}
