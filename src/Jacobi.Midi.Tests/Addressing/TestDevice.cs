using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Tests.Schema;
using Xunit;

namespace Jacobi.Midi.Tests.Addressing;

/// <summary>
/// The stand-alone schemas of the addressing tests, independent of any vendor schema.
/// The envelope (manufacturer id 7D, non-commercial) has a 2 x 7-bit address and a 7-bit CRC (CRC-7/MMC) over the address and data:
///   request  F0 7D dd mm 01 aa aa ss ss cc F7      data  F0 7D dd mm 02 aa aa xx… cc F7
/// The device map (model id 0x42) has an atomic patch nodeDef of 0x0F addresses
/// (0x00 bit-packed switches, 0x01-0x03 filter, 0x04-0x05 14-bit time, 0x06 level, 0x07-0x0E name):
///   0x00-00 Edit patch (0x20 addresses reserved), 0x00-40 Global (not atomic, 10 addresses),
///   0x01-00 Memory: 8 patches with a stride of 0x20.
/// </summary>
internal static class TestDevice
{
    public const int PatchSize = 0x0F;
    public const int Stride = 0x20;
    public const int GlobalStart = 0x40;      // wire 00 40
    public const int GlobalSize = 10;
    public const int MemoryStart = 0x80;      // wire 01 00
    public const int MemoryCount = 8;
    public const byte ModelId = 0x42;

    public static readonly SchemaName MapName = new("TestDevice.AddressMap");

    public const string EnvelopeXml = """
        <schema name="TestEnvelope">
          <includes>
            <include schema="midi" />
          </includes>
          <nodeDefs>
            <group name="body" />
            <field name="address" datatype="sys.uint16" byteLength="2">
              <processor name="midi.sysexbyte" />
            </field>
          </nodeDefs>
          <members>
            <group name="Message">
              <processors>
                <processor name="midi.sysex" />
              </processors>
              <members>
                <group name="Request" midi.message="request">
                  <members>
                    <field name="Id" datatype="sys.uint8" value="0x7D" />
                    <field name="DeviceId" datatype="sys.uint8" midi.role="device.deviceId" />
                    <field name="ModelId" datatype="sys.uint8" midi.role="device.modelId" />
                    <field name="Command" datatype="sys.uint8" value="0x01" />
                    <group name="CrcScope">
                      <processors>
                        <processor name="sys.crc" width="7" poly="0x09" init="0" refin="false" refout="false" xorout="0" />
                      </processors>
                      <members>
                        <field name="Address" nodeDef="address" midi.role="addressStart" />
                        <field name="Size" nodeDef="address" midi.role="addressSize" />
                      </members>
                    </group>
                  </members>
                </group>
                <group name="Data" midi.message="data" midi.messageInterval="20">
                  <members>
                    <field name="Id" datatype="sys.uint8" value="0x7D" />
                    <field name="DeviceId" datatype="sys.uint8" midi.role="device.deviceId" />
                    <field name="ModelId" datatype="sys.uint8" midi.role="device.modelId" />
                    <field name="Command" datatype="sys.uint8" value="0x02" />
                    <group name="CrcScope">
                      <processors>
                        <processor name="sys.crc" width="7" poly="0x09" init="0" refin="false" refout="false" xorout="0" />
                      </processors>
                      <members>
                        <field name="Address" nodeDef="address" midi.role="addressStart" />
                        <choice name="Body" memberNodeDef="body" midi.role="data" />
                      </members>
                    </group>
                  </members>
                </group>
              </members>
            </group>
          </members>
        </schema>
        """;

    public const string DeviceXml = """
        <schema name="TestDevice">
          <includes>
            <include schema="midi" />
            <include schema="TestEnvelope" />
          </includes>
          <nodeDefs>
            <group name="patch" midi.atomic="true">
              <members>
                <group name="Switches" byteLength="1">
                  <processors>
                    <processor name="sys.bitpacker" />
                  </processors>
                  <members>
                    <field name="A" datatype="sys.bool" bits="1" />
                    <field name="B" datatype="sys.bool" bits="1" />
                    <field name="C" datatype="sys.bool" bits="1" />
                    <field name="D" datatype="sys.bool" bits="1" />
                  </members>
                </group>
                <group name="Filter">
                  <members>
                    <field name="Cutoff" datatype="sys.uint8" />
                    <field name="Resonance" datatype="sys.uint8" />
                    <field name="Mode" datatype="sys.uint8" />
                  </members>
                </group>
                <field name="Time" datatype="sys.uint16" byteLength="2">
                  <processor name="midi.sysexbyte" />
                </field>
                <field name="Level" datatype="sys.uint8" />
                <field name="Name" datatype="sys.string" byteLength="8">
                  <processor name="midi.string" />
                </field>
              </members>
            </group>
          </nodeDefs>
          <members>
            <group name="AddressMap" nodeDef="TestEnvelope.body" midi.device.modelId="0x42">
              <members>
                <group name="Edit" nodeDef="patch" midi.addressSize="0x00-20" />
                <group name="Global" midi.addressStart="0x00-40">
                  <members>
                    <field name="Channel" datatype="sys.uint8" />
                    <field name="Text" datatype="sys.string" byteLength="8">
                      <processor name="midi.string" />
                    </field>
                    <field name="Volume" datatype="sys.uint8" />
                  </members>
                </group>
                <repeat name="Memory" count="8" midi.addressStart="0x01-00" midi.addressSize="0x00-20">
                  <members>
                    <group name="Patch" nodeDef="patch" />
                  </members>
                </repeat>
              </members>
            </group>
          </members>
        </schema>
        """;

    public static SchemaDocument Parse(string xml)
        => Jacobi.BinarySerializer.Schema.Xml.XmlSerializer.Deserialize(xml);

    /// <summary>Only the base 'midi' schema, the test envelope and device, and the extra documents: no vendor schemas.</summary>
    public static SchemaSet CreateSchemas(params SchemaDocument[] documents)
    {
        var builder = new SchemaSetBuilder();
        builder.LoadFromAssembly(TestHelpers.AssemblyPathOf("Jacobi.Midi.Device"), "Jacobi.Midi.Device");
        builder.AddDocument(Parse(EnvelopeXml));
        builder.AddDocument(Parse(DeviceXml));
        foreach (var document in documents)
        {
            builder.AddDocument(document);
        }
        return builder.Compile();
    }

    /// <summary>Only the 'sys' and 'midi' processors.</summary>
    public static ProcessorManager CreateProcessors()
    {
        var processors = new ProcessorManager();
        processors.Register(new Jacobi.BinarySerializer.Processors.ProcessorFactory());
        processors.Register(new Jacobi.Midi.Device.Processors.ProcessorFactory());
        return processors;
    }

    public static Serializer CreateSerializer(params SchemaDocument[] documents)
        => new SerializerBuilder()
            .AddSchemas(CreateSchemas(documents))
            .AddProcessors(CreateProcessors())
            .Build();

    public static AddressMap CreateMap(params SchemaDocument[] documents)
        => AddressMap.Create(CreateSerializer(documents), MapName);

    public static long PatchAddress(int item) => MemoryStart + item * Stride;

    /// <summary>The data of one patch; the switches are all off.</summary>
    public static byte[] Patch(string name, byte cutoff = 10, ushort time = 300, byte level = 100)
    {
        Assert.Equal(8, name.Length);
        return [0x00, cutoff, 20, 1, (byte)(time >> 7), (byte)(time & 0x7F), level, .. System.Text.Encoding.ASCII.GetBytes(name)];
    }

    /// <summary>The name of the patch the <see cref="FakeDevice"/> holds at a memory item.</summary>
    public static string PatchName(int item) => $"Patch {item,2}";

    public static byte[] Request(long address, long size, byte deviceId = 0)
        => Message(0x01, deviceId, ModelId, [.. Wire(address), .. Wire(size)]);

    public static byte[] DataMessage(long address, ReadOnlySpan<byte> data, byte deviceId = 0, byte modelId = ModelId)
        => Message(0x02, deviceId, modelId, [.. Wire(address), .. data]);

    private static byte[] Message(byte command, byte deviceId, byte modelId, byte[] scope)
        => [0xF0, 0x7D, deviceId, modelId, command, .. scope, Crc7(scope), 0xF7];

    private static byte[] Wire(long address) => [(byte)((address >> 7) & 0x7F), (byte)(address & 0x7F)];

    /// <summary>CRC-7/MMC: poly 0x09, init 0, not reflected, no final xor ('123456789' gives 0x75).</summary>
    public static byte Crc7(ReadOnlySpan<byte> bytes)
    {
        var crc = 0;
        foreach (var b in bytes)
        {
            for (var bit = 7; bit >= 0; bit--)
            {
                var top = ((crc >> 6) ^ (b >> bit)) & 1;
                crc = (crc << 1) & 0x7F;
                if (top != 0)
                {
                    crc ^= 0x09;
                }
            }
        }
        return (byte)crc;
    }

    /// <summary>Recalculates the CRC of a message made by <see cref="Request"/> or <see cref="DataMessage"/>.</summary>
    public static void UpdateCrc(byte[] message)
        => message[^2] = Crc7(message.AsSpan(5, message.Length - 7));
}
