using System.Collections.Immutable;
using Jacobi.Midi.Core;
using Native = Windows.Devices.Midi2.Enumeration;

namespace Jacobi.Midi.Windows;

internal static class WindowsMidiEndpointMapper
{
    internal static MidiEndpointInfo Map(Native.MidiEndpointDeviceInformation endpoint)
    {
        var transport = endpoint.GetTransportSuppliedInfo();
        var declared = endpoint.GetDeclaredEndpointInfo();
        var blocks = endpoint.GetDeclaredFunctionBlocks()
            .Select(block => new MidiFunctionBlockInfo(
                block.Number, block.Name, block.IsActive, MapDirection(block.Direction),
                new MidiGroup(block.FirstGroup.Index), block.GroupCount))
            .ToImmutableArray();
        var terminalBlocks = endpoint.GetGroupTerminalBlocks();
        MidiDirections? directions = null;
        if (blocks.Length > 0)
            directions = blocks.Where(block => block.IsActive)
                .Aggregate(MidiDirections.None, (value, block) => value | block.Directions);
        else if (terminalBlocks.Count > 0)
            directions = terminalBlocks.Aggregate(MidiDirections.None, (value, block) => value | MapDirection(block.Direction));

        var hasDeclaredInfo = endpoint.DeclaredEndpointInfoLastUpdateTime != default;
        var isMidi1Transport = transport.NativeDataFormat == Native.MidiEndpointNativeDataFormat.Midi1ByteFormat;
        MidiProtocol? protocol = endpoint.GetDeclaredStreamConfiguration().Protocol switch
        {
            Native.MidiProtocol.Midi1 => MidiProtocol.Midi1,
            Native.MidiProtocol.Midi2 => MidiProtocol.Midi2,
            _ => isMidi1Transport ? MidiProtocol.Midi1 : null
        };

        return new MidiEndpointInfo(endpoint.EndpointDeviceId, endpoint.Name, new MidiEndpointCapabilities
        {
            Directions = directions,
            SupportsMidi1 = hasDeclaredInfo ? declared.SupportsMidi10Protocol : isMidi1Transport ? true : null,
            SupportsMidi2 = hasDeclaredInfo ? declared.SupportsMidi20Protocol : isMidi1Transport ? false : null,
            ActiveProtocol = protocol,
            IsDiscoveryComplete = endpoint.IsEndpointDiscoveryComplete,
            FunctionBlocks = blocks
        })
        {
            Transport = transport.TransportCode
        };
    }

    private static MidiDirections MapDirection(Native.MidiFunctionBlockDirection direction) => direction switch
    {
        Native.MidiFunctionBlockDirection.BlockInput => MidiDirections.Send,
        Native.MidiFunctionBlockDirection.BlockOutput => MidiDirections.Receive,
        Native.MidiFunctionBlockDirection.Bidirectional => MidiDirections.Duplex,
        _ => MidiDirections.None
    };

    private static MidiDirections MapDirection(Native.MidiGroupTerminalBlockDirection direction) => direction switch
    {
        Native.MidiGroupTerminalBlockDirection.BlockInput => MidiDirections.Send,
        Native.MidiGroupTerminalBlockDirection.BlockOutput => MidiDirections.Receive,
        Native.MidiGroupTerminalBlockDirection.Bidirectional => MidiDirections.Duplex,
        _ => MidiDirections.None
    };

    internal static bool Equivalent(MidiEndpointInfo left, MidiEndpointInfo right)
    {
        var a = left.Capabilities;
        var b = right.Capabilities;
        return String.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase)
            && left.Name == right.Name && left.Transport == right.Transport
            && a.Directions == b.Directions && a.SupportsMidi1 == b.SupportsMidi1
            && a.SupportsMidi2 == b.SupportsMidi2 && a.ActiveProtocol == b.ActiveProtocol
            && a.IsDiscoveryComplete == b.IsDiscoveryComplete
            && a.FunctionBlocks.SequenceEqual(b.FunctionBlocks);
    }
}
