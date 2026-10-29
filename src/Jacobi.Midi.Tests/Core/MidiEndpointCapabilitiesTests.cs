using Jacobi.Midi.Core;
using Xunit;

namespace Jacobi.Midi.Tests.Core;

/// <summary>
/// Verifies how <see cref="MidiEndpointCapabilities"/> resolves the names, groups and directions of groups from function blocks
/// and group terminal blocks.
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

    // one-way function blocks on different groups: send to the device on group 1, receive from it on group 2
    private static readonly MidiEndpointCapabilities OneWay = new()
    {
        FunctionBlocks =
        [
            new(0, "To device", true, MidiDirections.Send, new MidiGroup(0), 1),
            new(1, "From device", true, MidiDirections.Receive, new MidiGroup(1), 1),
        ]
    };

    [Fact]
    public void GetGroupDirections_GroupTerminalBlocks_BothDirectionsPerGroup()
    {
        Assert.Equal(MidiDirections.Duplex, TwoPorts.GetGroupDirections(new MidiGroup(0)));
        Assert.Equal(MidiDirections.Duplex, TwoPorts.GetGroupDirections(new MidiGroup(1)));
        Assert.Equal(MidiDirections.None, TwoPorts.GetGroupDirections(new MidiGroup(2)));
    }

    [Fact]
    public void GetGroupDirections_OneWayFunctionBlocks_OneDirectionPerGroup()
    {
        Assert.Equal(MidiDirections.Send, OneWay.GetGroupDirections(new MidiGroup(0)));
        Assert.Equal(MidiDirections.Receive, OneWay.GetGroupDirections(new MidiGroup(1)));
    }

    [Fact]
    public void GetGroupDirections_NoBlocks_Unknown()
    {
        Assert.Null(new MidiEndpointCapabilities().GetGroupDirections(new MidiGroup(0)));
    }

    [Fact]
    public void GetDeclaredGroups_PerDirection_OnlyTheGroupsOfThatDirection()
    {
        Assert.Equal([new MidiGroup(0)], OneWay.GetDeclaredGroups(MidiDirections.Send));
        Assert.Equal([new MidiGroup(1)], OneWay.GetDeclaredGroups(MidiDirections.Receive));
        Assert.Equal([new MidiGroup(0), new MidiGroup(1)], OneWay.GetDeclaredGroups());
    }

    [Fact]
    public void GetGroupName_PerDirection_OnlyTheNamesOfThatDirection()
    {
        var capabilities = new MidiEndpointCapabilities
        {
            GroupTerminalBlocks =
            [
                new(1, "In A", MidiDirections.Receive, new MidiGroup(0), 1),
                new(2, "Out A", MidiDirections.Send, new MidiGroup(0), 1),
            ]
        };

        Assert.Equal("Out A", capabilities.GetGroupName(new MidiGroup(0), MidiDirections.Send));
        Assert.Equal("In A", capabilities.GetGroupName(new MidiGroup(0), MidiDirections.Receive));
        Assert.Null(OneWay.GetGroupName(new MidiGroup(0), MidiDirections.Receive));
    }
}
