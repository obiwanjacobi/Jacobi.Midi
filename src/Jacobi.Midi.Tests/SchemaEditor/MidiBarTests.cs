using Jacobi.Midi.Core;
using Jacobi.Midi.SchemaEditor.Midi;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

/// <summary>
/// The send and receive groups of the MIDI bar: listed per direction from the endpoint's blocks, and the receive group
/// following the send group as long as they are the same.
/// </summary>
public class MidiBarTests
{
    // one-way function blocks: send to the device on group 1 or 3, receive from it on group 2
    private static readonly MidiEndpointInfo OneWay = new("id", "One way", new MidiEndpointCapabilities
    {
        FunctionBlocks =
        [
            new(0, "To device", true, MidiDirections.Send, new MidiGroup(0), 1),
            new(1, "From device", true, MidiDirections.Receive, new MidiGroup(1), 1),
            new(2, "To device 2", true, MidiDirections.Send, new MidiGroup(2), 1),
        ]
    });

    [Fact]
    public void SelectedEndpoint_ListsTheGroupsPerDirection()
    {
        var midi = new MidiBarViewModel(null) { SelectedEndpoint = OneWay };

        Assert.Equal([new MidiGroup(0), new MidiGroup(2)], midi.SendGroups.Select(item => item.Group));
        Assert.Equal([new MidiGroup(1)], midi.ReceiveGroups.Select(item => item.Group));
        Assert.Equal(new MidiGroupPair(new MidiGroup(0), new MidiGroup(1)), midi.Groups);
    }

    [Fact]
    public void SelectedSendGroup_TheSameReceiveGroup_Follows()
    {
        var midi = new MidiBarViewModel(null);

        midi.SelectedSendGroup = midi.SendGroups[3];

        Assert.Equal(MidiGroupPair.Same(new MidiGroup(3)), midi.Groups);
    }

    [Fact]
    public void SelectedSendGroup_AnotherReceiveGroup_Stays()
    {
        var midi = new MidiBarViewModel(null);
        midi.SelectedReceiveGroup = midi.ReceiveGroups[1];

        midi.SelectedSendGroup = midi.SendGroups[3];

        Assert.Equal(new MidiGroupPair(new MidiGroup(3), new MidiGroup(1)), midi.Groups);
    }
}
