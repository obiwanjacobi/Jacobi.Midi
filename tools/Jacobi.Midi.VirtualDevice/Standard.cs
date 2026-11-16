using Windows.Devices.Midi2.CapabilityInquiry;

namespace Jacobi.Midi.VirtualDevice;

/// <summary>
/// The standard Property Exchange resources of the virtual device (M2-105): a channel list whose channels link to the
/// one program list (<c>main</c>), and their entries in the <c>ResourceList</c>. Read-only, as the specifications
/// define them.
/// </summary>
internal static class Standard
{
    private static readonly string[] ProgramTitles =
    [
        "Grand Piano", "Electric Piano", "Drawbar Organ", "Nylon Guitar",
        "Fretless Bass", "Violin", "String Ensemble", "Warm Pad",
    ];

    public static MidiProgramList ProgramList()
    {
        var programs = new MidiProgramList();
        byte program = 0;
        foreach (var title in ProgramTitles)
        {
            programs.Entries.Add(new MidiProgramListEntry { Title = title, BankMsb = 0, BankLsb = 0, ProgramChange = program++ });
        }
        return programs;
    }

    public static MidiChannelList ChannelList()
    {
        var channels = new MidiChannelList();
        for (ushort channel = 1; channel <= 4; channel++)
        {
            var entry = new MidiChannelListEntry { Channel = channel, Title = $"Part {channel}", ProgramTitle = ProgramTitles[0] };
            entry.Links.Add(new MidiResourceLink { Resource = "ProgramList", ResourceId = "main", Title = "Programs" });
            channels.Entries.Add(entry);
        }
        return channels;
    }

    // the standard resources, with the defaults of their specifications (a ProgramList needs a resId)
    public static IEnumerable<MidiResourceListEntry> ResourceListEntries()
        => [new("DeviceInfo"), new("ChannelList") { CanSubscribe = true }, new("ProgramList")];
}
