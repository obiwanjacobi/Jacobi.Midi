using Jacobi.Midi.Core;
using Xunit;

namespace Jacobi.Midi.Tests.Core;

/// <summary>
/// Verifies how <see cref="MidiEndpointCapabilities.GetGroupName"/> resolves a group name from function blocks and group terminal blocks.
/// </summary>
public class MidiEndpointCapabilitiesTests
{
    // like a USB MIDI 1.0 interface with two ports: a source and a destination block per port
    private static readonly MidiEndpointCapabilities TwoPorts = new()
    {
        GroupTerminalBlocks =
        [
            new(1, "MIDIMATE II", MidiDirections.Receive, new MidiGroup(0), 1),
            new(2, "MIDIMATE II 2", MidiDirections.Receive, new MidiGroup(1), 1),
            new(3, "MIDIMATE II", MidiDirections.Send, new MidiGroup(0), 1),
            new(4, "MIDIMATE II 2", MidiDirections.Send, new MidiGroup(1), 1),
        ]
    };

    [Fact]
    public void GetGroupName_GroupTerminalBlocks_SameNameOnce()
    {
        Assert.Equal("MIDIMATE II", TwoPorts.GetGroupName(new MidiGroup(0)));
        Assert.Equal("MIDIMATE II 2", TwoPorts.GetGroupName(new MidiGroup(1)));
    }

    [Fact]
    public void GetGroupName_NoBlockForGroup_Null()
    {
        Assert.Null(TwoPorts.GetGroupName(new MidiGroup(2)));
    }

    [Fact]
    public void GetGroupName_DifferentNamesPerDirection_Joined()
    {
        var capabilities = new MidiEndpointCapabilities
        {
            GroupTerminalBlocks =
            [
                new(1, "In A", MidiDirections.Receive, new MidiGroup(0), 1),
                new(2, "Out A", MidiDirections.Send, new MidiGroup(0), 1),
            ]
        };

        Assert.Equal("In A / Out A", capabilities.GetGroupName(new MidiGroup(0)));
    }

    [Fact]
    public void GetDeclaredGroups_GroupTerminalBlocks_EachGroupOnce()
    {
        Assert.Equal([new MidiGroup(0), new MidiGroup(1)], TwoPorts.GetDeclaredGroups());
    }

    [Fact]
    public void GetDeclaredGroups_NoBlocks_Empty()
    {
        Assert.Empty(new MidiEndpointCapabilities().GetDeclaredGroups());
    }

    [Fact]
    public void GetDeclaredGroups_FunctionBlocks_OnlyActive()
    {
        var capabilities = TwoPorts with
        {
            FunctionBlocks =
            [
                new(0, "Synth", true, MidiDirections.Duplex, new MidiGroup(2), 2),
                new(1, "Inactive", false, MidiDirections.Duplex, new MidiGroup(5), 1),
            ]
        };

        Assert.Equal([new MidiGroup(2), new MidiGroup(3)], capabilities.GetDeclaredGroups());
    }

    [Fact]
    public void GetGroupName_FunctionBlocks_SupersedeGroupTerminalBlocks()
    {
        var capabilities = TwoPorts with
        {
            FunctionBlocks =
            [
                new(0, "Synth", true, MidiDirections.Duplex, new MidiGroup(0), 2),
                new(1, "Inactive", false, MidiDirections.Duplex, new MidiGroup(2), 1),
            ]
        };

        Assert.Equal("Synth", capabilities.GetGroupName(new MidiGroup(1)));
        Assert.Null(capabilities.GetGroupName(new MidiGroup(2)));
    }
}
