using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// A read-only view of the message map of one root (see <see cref="MessageMaps.Info"/>): what a client needs to know
/// about the root, whatever the kind of map (address map or dump). The map's operations stay with the Device API.
/// </summary>
public sealed class MessageMapInfo
{
    private readonly IMessageMap _map;

    internal MessageMapInfo(IMessageMap map)
    {
        _map = map;
    }

    /// <summary>The root the map is for.</summary>
    public SchemaName Root => _map.Root;

    /// <summary>How the data of the root is carried in messages.</summary>
    public MessageMapKind Kind => _map.Kind;

    /// <summary>
    /// The number of items of a repeat of the root as the map transfers it: the items of the address space for an
    /// address map (<c>midi.repeat</c>), the constant count of the schema for a dump; null for a node that is not a
    /// repeat, or a repeat whose count the data gives.
    /// </summary>
    public int? ItemCount(SchemaPath repeat) => _map.ItemCount(repeat);

    /// <summary>
    /// How the value of an envelope node with <paramref name="role"/> (<c>midi.role</c>) is supplied when a data message
    /// carries the root: a short description (e.g. <c>address: calculated from the map</c>, or a device value the root
    /// fixes); null when the client supplies it (a device value the root does not fix, or not a role of the map).
    /// </summary>
    public string? DescribeRole(string role)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (DeviceValues.NameOfRole(role) is { } name && _map.Device.TryGet(name, out var value))
        {
            return $"{role}: 0x{value:X2}, fixed by '{Root}'";
        }
        return _map.DescribeRole(role);
    }

    /// <summary>The device values the root fixes (<c>midi.device.*</c>).</summary>
    public DeviceValues Device => _map.Device;

    /// <summary>
    /// The names of the device values the data messages need that the root does not fix (e.g. <c>deviceId</c>): the
    /// client supplies them for the device instance.
    /// </summary>
    public IReadOnlyList<string> RequiredDeviceValues => DeviceValues.Missing(_map.DataMessages, _map.Device);
}
