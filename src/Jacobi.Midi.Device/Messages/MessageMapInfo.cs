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

    /// <summary>The device values the root fixes (<c>midi.device.*</c>).</summary>
    public DeviceValues Device => _map.Device;

    /// <summary>
    /// The names of the device values the data messages need that the root does not fix (e.g. <c>deviceId</c>): the
    /// client supplies them for the device instance.
    /// </summary>
    public IReadOnlyList<string> RequiredDeviceValues => DeviceValues.Missing(_map.DataMessages, _map.Device);
}
