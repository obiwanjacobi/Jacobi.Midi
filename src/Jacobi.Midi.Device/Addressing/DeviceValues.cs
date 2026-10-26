using System.Collections;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// The device values (<c>device.&lt;name&gt;</c> roles) by name, e.g. <c>deviceId</c> or <c>modelId</c>: the values a
/// map root fixes for the device type (<c>midi.device.&lt;name&gt;</c>), plus the values the client supplies for the
/// device instance. Names are case-insensitive.
/// </summary>
public sealed class DeviceValues : IEnumerable<KeyValuePair<string, long>>
{
    private readonly Dictionary<string, long> _values = new(StringComparer.OrdinalIgnoreCase);

    public long this[string name]
    {
        get => _values.TryGetValue(name, out var value)
            ? value
            : throw new KeyNotFoundException($"There is no device value '{name}'.");
        set => _values[name] = value;
    }

    public int Count => _values.Count;

    public bool TryGet(string name, out long value)
        => _values.TryGetValue(name, out value);

    /// <summary>These values with the other's values added or overriding them (client over map root).</summary>
    public DeviceValues With(DeviceValues other)
    {
        var result = new DeviceValues();
        foreach (var (name, value) in this)
        {
            result[name] = value;
        }
        foreach (var (name, value) in other)
        {
            result[name] = value;
        }
        return result;
    }

    /// <summary>Reads the <c>midi.device.&lt;name&gt;</c> properties of a schema node (the map root).</summary>
    internal static DeviceValues FromProperties(SchemaNode node, string document, SchemaPath path, AddressMapProblems problems)
    {
        var values = new DeviceValues();
        foreach (var property in node.Properties)
        {
            if (!property.Name.StartsWith(MidiProperties.DevicePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = property.Name[MidiProperties.DevicePrefix.Length..];
            if (AddressSpace.TryParseNumber(property.Value, out var value) && value <= Int64.MaxValue)
            {
                values[name] = (long)value;
            }
            else
            {
                problems.Error(document, path, $"'{property.Name}' has no valid number: '{property.Value}'.");
            }
        }
        return values;
    }

    public IEnumerator<KeyValuePair<string, long>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
