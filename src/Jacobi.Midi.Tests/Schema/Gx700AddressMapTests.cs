using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Xunit;

namespace Jacobi.Midi.Tests.Schema;

/// <summary>
/// Tests the Boss GX-700 address map (Gx700.xml with the 4 byte address messages of Roland.xml)
/// against the bulk dumps of one patch and of the system.
/// </summary>
public class Gx700AddressMapTests
{
    private const int SectionStride = 0x80;                  // wire 00 00 01 00
    private const int PatchStride = 0x4000;                  // wire 00 01 00 00
    private const int PresetStart = 0x200000;                // wire 01 00 00 00
    private const int SystemStart = 0x400000;                // wire 02 00 00 00
    private const int TemporaryStart = 0x800000;             // wire 04 00 00 00
    private const int SoundChangeRequest = 0x9FFFFF;        // wire 04 7F 7F 7F
    private const int IndividualStart = 0x1000000;           // wire 08 00 00 00
    private const int DisplayStart = 0x2000000;              // wire 10 00 00 00

    private static readonly DeviceValues Device = new() { ["deviceId"] = 0 };

    private static AddressMap CreateMap()
    {
        var serializer = new SerializerBuilder()
            .AddSchemas(TestHelpers.CreateSchemas())
            .AddProcessors(TestHelpers.CreateProcessors())
            .Build();
        return AddressMap.Create(serializer, new SchemaName("Gx700.AddressMap"));
    }

    // the dump files hold one DT1 per section
    private static List<byte[]> DumpMessages(string fileName)
    {
        var file = File.ReadAllBytes(TestHelpers.PathOf("Schema/" + fileName));
        var messages = new List<byte[]>();
        var start = 0;
        for (var i = 0; i < file.Length; i++)
        {
            if (file[i] == 0xF7)
            {
                messages.Add(file[start..(i + 1)]);
                start = i + 1;
            }
        }
        return messages;
    }

    private static FieldValueStore Decode(AddressMap map, byte[] message)
    {
        Assert.True(map.TryReadData(message, Device, out var data));
        var store = new FieldValueStore();
        data.Decode(store);
        return store;
    }

    private static object? Value(FieldValueStore store, string path, params int[] instance)
    {
        Assert.True(store.TryGet(path, new InstancePath(instance), out var value), path);
        return value.Value;
    }

    [Fact]
    public void Create_FindsTheRolandMessages()
    {
        var map = CreateMap();

        Assert.Equal(new AddressSpace(4, 7), map.Protocol.AddressSpace);
        Assert.True(map.Message(new SchemaName("Roland.Message32.RQ1")).IsRequest);
        Assert.Equal("Message32.DT1", Assert.Single(map.Protocol.DataMessages).Name);
    }

    [Fact]
    public void Range_CoversAllAreas()
    {
        var map = CreateMap();

        Assert.Equal(new AddressRange(0, DisplayStart + 0x20), map.Range);
    }

    [Fact]
    public void GetAddressRange_Areas()
    {
        var map = CreateMap();

        var memory = map.CreateRange("AddressMap.Memory.Patch.Common", new InstancePath([5]));
        Assert.Equal(new AddressRange(5 * PatchStride, 0x42), map.GetAddressRange(memory));

        var preset = map.CreateRange("AddressMap.Preset.Patch.Delay", new InstancePath([2]));
        Assert.Equal(new AddressRange(PresetStart + 2 * PatchStride + 0x0A * SectionStride, 0x15), map.GetAddressRange(preset));

        var system = map.CreateRange("AddressMap.System.ProgramChangeMap2");
        Assert.Equal(new AddressRange(SystemStart + 2 * SectionStride, 0x80), map.GetAddressRange(system));

        var temporary = map.CreateRange("AddressMap.Temporary.Reverb");
        Assert.Equal(new AddressRange(TemporaryStart + 0x0D * SectionStride, 0x09), map.GetAddressRange(temporary));

        var scr = map.CreateRange("AddressMap.SoundChangeRequest");
        Assert.Equal(new AddressRange(SoundChangeRequest, 1), map.GetAddressRange(scr));

        var individual = map.CreateRange("AddressMap.IndividualTemporary.Modulation");
        Assert.Equal(new AddressRange(IndividualStart + 0x09 * SectionStride, 0x4D), map.GetAddressRange(individual));

        var display = map.CreateRange("AddressMap.Display");
        Assert.Equal(new AddressRange(DisplayStart, 0x20), map.GetAddressRange(display));
    }

    [Theory]
    [InlineData("gx700-sysex-patch1.syx", 14)]
    [InlineData("gx700-sysex-system.syx", 3)]
    public void TryReadData_EverySection_RoundTrips(string fileName, int sectionCount)
    {
        var map = CreateMap();
        var messages = DumpMessages(fileName);
        Assert.Equal(sectionCount, messages.Count);

        foreach (var message in messages)
        {
            Assert.True(map.TryReadData(message, Device, out var data), Convert.ToHexString(message));
            var store = new FieldValueStore();
            data.Decode(store);

            var written = map.CreateData(data.Message, data.Block, store, data.Device);
            Assert.Equal(Convert.ToHexString(message), Convert.ToHexString(written));
        }
    }

    [Fact]
    public void TryReadData_Patch_Decode()
    {
        var map = CreateMap();
        var messages = DumpMessages("gx700-sysex-patch1.syx");

        var common = Decode(map, messages[0x00]);
        Assert.Equal((byte)78, Value(common, "AddressMap.Memory.Patch.Common.OutputLevel", 0));
        Assert.Equal("Compressor", Value(common, "AddressMap.Memory.Patch.Common.Chain.EffectType", 0, 0));
        Assert.Equal("Reverb", Value(common, "AddressMap.Memory.Patch.Common.Chain.EffectType", 0, 12));
        Assert.Equal("VINTAGE TWIN", Value(common, "AddressMap.Memory.Patch.Common.Name", 0));
        // assign #2: target 03 07 = 0x37, max 00 64 = 100, source 0B = 11
        Assert.Equal("NS:LEVEL", Value(common, "AddressMap.Memory.Patch.Common.Assign.Target", 0, 1));
        Assert.Equal((ushort)100, Value(common, "AddressMap.Memory.Patch.Common.Assign.Max", 0, 1));
        Assert.Equal("MIDI CC#7", Value(common, "AddressMap.Memory.Patch.Common.Assign.Source", 0, 1));
        Assert.Equal((byte)127, Value(common, "AddressMap.Memory.Patch.Common.Assign.ActHi", 0, 1));

        var preamp = Decode(map, messages[0x04]);
        Assert.Equal(true, Value(preamp, "AddressMap.Memory.Patch.Preamp.On", 0));
        Assert.Equal("Clean Twin", Value(preamp, "AddressMap.Memory.Patch.Preamp.Type", 0));

        // flanger resonance 07 08 = 7 * 16 + 8 = 120 (= +20)
        var modulation = Decode(map, messages[0x09]);
        Assert.Equal(20m, Value(modulation, "AddressMap.Memory.Patch.Modulation.Resonance", 0));

        var delay = Decode(map, messages[0x0A]);
        // tempo 04 06 = 4 * 16 + 6 = 70 (= 120 BPM)
        Assert.Equal(120m, Value(delay, "AddressMap.Memory.Patch.Delay.Tempo", 0));
        // time 02 31 = 2 * 128 + 49
        Assert.Equal((ushort)305, Value(delay, "AddressMap.Memory.Patch.Delay.TimeCenter", 0));
    }

    [Fact]
    public void TryReadData_System_Decode()
    {
        var map = CreateMap();
        var messages = DumpMessages("gx700-sysex-system.syx");

        var common = Decode(map, messages[0]);
        // tuner pitch 05 = 440 Hz
        Assert.Equal(440m, Value(common, "AddressMap.System.Common.TunerPitch"));
        // reverb level 06 04 = 6 * 16 + 4
        Assert.Equal((byte)100, Value(common, "AddressMap.System.Common.ReverbLevel"));
        Assert.Equal("Assignable", Value(common, "AddressMap.System.Common.Control1Jack"));
        // number up/down max 0C 08 = 12 * 16 + 8
        Assert.Equal((byte)200, Value(common, "AddressMap.System.Common.NumberUpDownMax"));
        Assert.Equal(1m, Value(common, "AddressMap.System.Common.MidiChannel"));
        Assert.Equal(true, Value(common, "AddressMap.System.Common.OmniMode"));

        var map1 = Decode(map, messages[1]);
        Assert.Equal((byte)1, Value(map1, "AddressMap.System.ProgramChangeMap1.Program.Patch", 0));
        Assert.Equal((byte)64, Value(map1, "AddressMap.System.ProgramChangeMap1.Program.Patch", 63));
        var map2 = Decode(map, messages[2]);
        Assert.Equal((byte)128, Value(map2, "AddressMap.System.ProgramChangeMap2.Program.Patch", 63));
    }

    [Fact]
    public void IndividualTemporary_OneParameter_RoundTrips()
    {
        var map = CreateMap();
        // DT1 of the reverb time (0x02 of the reverb section 0x0D) in the individual temporary area: 2.5 s
        byte[] scope = [0x08, 0x00, 0x0D, 0x02, 25];
        byte[] message = [0xF0, 0x41, 0x00, 0x79, 0x12, .. scope, (byte)((128 - scope.Sum(b => b) % 128) % 128), 0xF7];

        Assert.True(map.TryReadData(message, Device, out var data));
        Assert.Equal(new AddressRange(IndividualStart + 0x0D * SectionStride + 2, 1), data.Block.Addresses);
        Assert.Equal("AddressMap.IndividualTemporary.Reverb.Time", data.Block.Nodes.First.Path.Value);
        var store = new FieldValueStore();
        data.Decode(store);
        Assert.Equal(1, store.Count);

        var bytes = map.CreateData(data.Message, data.Block, store, map.Device.With(Device));
        Assert.Equal(Convert.ToHexString(message), Convert.ToHexString(bytes));
    }
}
