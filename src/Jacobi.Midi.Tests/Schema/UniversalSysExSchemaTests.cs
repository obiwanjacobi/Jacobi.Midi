using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Messages;
using Jacobi.Midi.Tests.Addressing;
using Xunit;

namespace Jacobi.Midi.Tests.Schema;

/// <summary>
/// The universal System Exclusive messages of the base schemas (Midi and Midi.&lt;category&gt;, embedded in Jacobi.Midi.Device):
/// every root is a message that is a dump itself; a message is read and written back unchanged.
/// </summary>
public class UniversalSysExSchemaTests
{
    private static readonly Serializer Serializer = TestDevice.CreateSerializer();

    private static IEnumerable<SchemaName> UniversalRoots()
        => Serializer.Schemas.Documents
            .Where(document => document.Name.Equals("Midi", StringComparison.OrdinalIgnoreCase)
                || document.Name.StartsWith("Midi.", StringComparison.OrdinalIgnoreCase))
            .SelectMany(document => document.Roots.Select(root => new SchemaName($"{document.Name}.{root.Name}")));

    [Fact]
    public void EveryRoot_IsAMessageThatIsADump()
    {
        var maps = new MessageMaps(Serializer);
        var roots = UniversalRoots().ToList();

        Assert.True(roots.Count > 90, $"{roots.Count} roots");
        Assert.All(roots, root => Assert.IsType<DumpMap>(maps.Get(Serializer.GetPlan(root))));
    }

    [Theory]
    // Midi
    [InlineData("Midi.IdentityRequest", "F0 7E 7F 06 01 F7")]
    [InlineData("Midi.IdentityReply", "F0 7E 7F 06 02 41 01 02 03 04 01 02 03 04 F7")]
    [InlineData("Midi.IdentityReplyExtendedId", "F0 7E 7F 06 02 00 20 33 01 02 03 04 01 02 03 04 F7")]
    [InlineData("Midi.MasterVolume", "F0 7F 7F 04 01 01 02 F7")]
    [InlineData("Midi.MasterCoarseTuning", "F0 7F 7F 04 04 00 40 F7")]
    [InlineData("Midi.GlobalParameterControl", "F0 7F 7F 04 05 01 01 01 01 01 01 30 02 04 F7")]
    [InlineData("Midi.Ack", "F0 7E 7F 7F 05 F7")]
    // categories
    [InlineData("Midi.GM.GeneralMidi2SystemOn", "F0 7E 7F 09 03 F7")]
    [InlineData("Midi.SampleDump.Header", "F0 7E 7F 01 01 00 10 01 02 03 04 05 06 07 08 09 0A 0B 0C 00 F7")]
    [InlineData("Midi.SampleDump.SampleNameTransmission", "F0 7E 7F 05 03 01 00 00 03 41 42 43 F7")]
    [InlineData("Midi.SampleDump.ExtendedHeader", "F0 7E 7F 05 05 01 00 18 44 2C 00 00 00 00 00 00 01 00 00 00 00 02 00 00 00 00 03 00 00 00 00 00 01 F7")]
    [InlineData("Midi.TimeCode.FullMessage", "F0 7F 7F 01 01 61 02 03 04 F7")]
    [InlineData("Midi.TimeCode.Setup", "F0 7E 7F 04 05 61 02 03 04 05 01 00 0A 0B F7")]
    [InlineData("Midi.TimeCode.Cueing", "F0 7F 7F 05 05 01 00 F7")]
    [InlineData("Midi.FileDump.Request", "F0 7E 7F 07 03 05 4D 49 44 49 41 2E 6D 69 64 F7")]
    [InlineData("Midi.FileDump.Header", "F0 7E 7F 07 01 05 4D 49 44 49 10 00 00 00 41 2E 6D 69 64 F7")]
    [InlineData("Midi.FileDump.DataPacket", "F0 7E 7F 07 02 03 02 40 02 03 44 F7")]
    [InlineData("Midi.Tuning.SingleNoteTuningChange", "F0 7F 7F 08 02 00 02 40 40 00 00 41 41 01 02 F7")]
    [InlineData("Midi.Tuning.ScaleOctaveTuning1Byte", "F0 7E 7F 08 08 03 7F 7F 40 40 40 40 40 40 40 40 40 40 40 40 F7")]
    [InlineData("Midi.FileReference.OpenFile", "F0 7E 7F 0B 01 01 00 03 00 41 42 43 F7")]
    [InlineData("Midi.CapabilityInquiry.Discovery", "F0 7E 7F 0D 70 02 01 02 03 04 7F 7F 7F 7F 41 00 00 01 00 02 00 01 02 03 04 0E 00 02 00 00 00 F7")]
    [InlineData("Midi.CapabilityInquiry.ReplyToProfileInquiry", "F0 7E 7F 0D 21 02 01 02 03 04 05 06 07 08 01 00 7E 00 01 02 03 00 00 F7")]
    [InlineData("Midi.CapabilityInquiry.GetPropertyData", "F0 7E 7F 0D 34 02 01 02 03 04 05 06 07 08 01 02 00 7B 7D 01 00 01 00 00 00 F7")]
    [InlineData("Midi.Notation.TimeSignatureImmediate", "F0 7F 7F 03 02 06 04 02 18 08 03 03 F7")]
    [InlineData("Midi.ControllerDestination.ControlChange", "F0 7F 7F 09 03 00 10 00 40 02 40 F7")]
    [InlineData("Midi.MachineControl.Commands", "F0 7F 7F 06 02 F7")]
    public void Message_IsReadAndWrittenBackUnchanged(string root, string hex)
    {
        var message = Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal));
        var maps = new MessageMaps(Serializer);
        var device = new DeviceValues { ["deviceId"] = 0x7F };
        var values = new FieldValueStore();
        var warnings = new List<string>();

        Assert.Equal(1, maps.Load(new SchemaName(root), [message], device, values, warnings));
        Assert.Empty(warnings);

        // a part of a dump is widened to the whole message
        var first = values.Values.First().Path;
        var saved = maps.Save([Serializer.GetPlan(new SchemaName(root)).CreateRange(first, first)], values, device);
        Assert.Equal(message, Assert.Single(saved));
    }

    [Fact]
    public void FileDumpDataPacket_TheByteCountIsTheCountMinusOne()
    {
        var values = Load("Midi.FileDump.DataPacket", "F0 7E 7F 07 02 03 02 40 02 03 44 F7");

        Assert.Equal(3, Convert.ToInt32(Value(values, "DataPacket.ByteCount")));
        // the 3 packed bytes 40 02 03 are the 2 data bytes 82 03 (bit 6 of the high-bit byte: the first byte)
        Assert.Equal(new byte[] { 0x82, 0x03 }, Value(values, "DataPacket.Data"));
    }

    [Theory]
    [InlineData("Midi.SampleDump.DataPacket")]
    [InlineData("Midi.FileDump.DataPacket")]
    [InlineData("Midi.Tuning.BulkDump")]
    public void Checksum_IsTheXorOfTheWholeMessage_AndNotAField(string root)
    {
        var message = root switch
        {
            "Midi.SampleDump.DataPacket" => Message([0x7E, 0x7F, 0x02, 0x05, .. Enumerable.Range(0, 120).Select(i => (byte)i)]),
            "Midi.FileDump.DataPacket" => Message([0x7E, 0x7F, 0x07, 0x02, 0x03, 0x02, 0x40, 0x02, 0x03]),
            _ => Message([0x7E, 0x7F, 0x08, 0x01, 0x00, .. "Equal temperament"u8[..16],
                .. Enumerable.Range(0, 128).SelectMany(key => new byte[] { (byte)key, 0x00, 0x00 })]),
        };
        var values = Load(root, Convert.ToHexString(message));

        Assert.DoesNotContain(values.Values, value => value.Path.EndsWith(".Checksum", StringComparison.Ordinal));
        var first = values.Values.First().Path;
        var saved = new MessageMaps(Serializer).Save([Serializer.GetPlan(new SchemaName(root)).CreateRange(first, first)], values,
            new DeviceValues { ["deviceId"] = 0x7F });
        Assert.Equal(message, Assert.Single(saved));

        // a wrong checksum: the message is not read
        message[^2] ^= 0x01;
        var warnings = new List<string>();
        Assert.Equal(0, new MessageMaps(Serializer).Load(new SchemaName(root), [message], new DeviceValues { ["deviceId"] = 0x7F },
            new FieldValueStore(), warnings));
        Assert.NotEmpty(warnings);
    }

    // F0, the content, the XOR of the content (7 bits), F7
    private static byte[] Message(byte[] content)
        => [0xF0, .. content, (byte)(content.Aggregate(0, (xor, b) => xor ^ b) & 0x7F), 0xF7];

    [Fact]
    public void FileDumpName_IsTheTextUpToTheEndOfTheMessage()
    {
        var values = Load("Midi.FileDump.Request", "F0 7E 7F 07 03 05 4D 49 44 49 41 2E 6D 69 64 F7");

        Assert.Equal("MIDI", Value(values, "Request.Type"));
        Assert.Equal("A.mid", Value(values, "Request.Name"));
    }

    [Fact]
    public void LsbFirstValues_AreJoinedFromTheirSevenBitBytes()
    {
        var values = Load("Midi.MasterVolume", "F0 7F 7F 04 01 01 02 F7");

        Assert.Equal(0x0101, Convert.ToInt32(Value(values, "MasterVolume.Volume")));
    }

    [Fact]
    public void RepeatWithoutACount_EndsWithTheItemsTheStoreHolds()
    {
        var values = Load("Midi.ControllerDestination.ControlChange", "F0 7F 7F 09 03 00 10 00 40 02 40 F7");
        // one destination less: the repeat is written with the items the store has values of
        values.Remove("ControlChange.Destinations.Parameter", new InstancePath([1]));
        values.Remove("ControlChange.Destinations.Range", new InstancePath([1]));
        var maps = new MessageMaps(Serializer);
        var root = new SchemaName("Midi.ControllerDestination.ControlChange");

        var saved = maps.Save([Serializer.GetPlan(root).CreateRange("ControlChange.Channel", "ControlChange.Channel")], values,
            new DeviceValues { ["deviceId"] = 0x7F });

        Assert.Equal(Convert.FromHexString("F07F7F0903001000 40F7".Replace(" ", "", StringComparison.Ordinal)), Assert.Single(saved));
    }

    private static FieldValueStore Load(string root, string hex)
    {
        var values = new FieldValueStore();
        var loaded = new MessageMaps(Serializer).Load(new SchemaName(root), [Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal))],
            new DeviceValues { ["deviceId"] = 0x7F }, values, []);
        Assert.Equal(1, loaded);
        return values;
    }

    private static object? Value(FieldValueStore values, string path)
    {
        Assert.True(values.TryGet(path, InstancePath.Empty, out var value), path);
        return value.Value;
    }
}
