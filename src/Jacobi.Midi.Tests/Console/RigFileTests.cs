using Jacobi.Midi.Console.Core.Files;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Core.Rigs;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// Reading and writing rigs as XML and JSON: device values in the schema notation, endpoints by name and group,
/// and content that does not fit reported with its location.
/// </summary>
public class RigFileTests
{
    private const string Xml = """
        <rig format="jacobi.midi.rig/1">
          <assembly path="Acme.Midi.dll" />
          <schema path="C:/Schemas/Acme.xml" />
          <device alias="synth" name="Acme S-1" mapRoot="Acme.AddressMap" color="#3D8BFD">
            <schema path="Schemas/AcmeCustom.xml" />
            <deviceValue name="deviceId" value="0x10" />
            <deviceValue name="groupId" value="7F1CH" />
            <endpoint name="USB MIDI Interface" group="2" />
          </device>
          <device alias="pedalboard" mapRoot="Gx700.AddressMap" />
        </rig>
        """;

    private const string Json = """
        {
          "format": "jacobi.midi.rig/1",
          "assemblies": [ "Acme.Midi.dll" ],
          "schemas": [ "C:/Schemas/Acme.xml" ],
          "devices": [
            {
              "alias": "synth", "name": "Acme S-1", "mapRoot": "Acme.AddressMap", "color": "#3D8BFD",
              "schemas": [ "Schemas/AcmeCustom.xml" ],
              "deviceValues": { "deviceId": "0x10", "groupId": 32540 },
              "endpoint": { "name": "USB MIDI Interface", "group": 2 }
            },
            { "alias": "pedalboard", "mapRoot": "Gx700.AddressMap" }
          ]
        }
        """;

    [Fact]
    public void Read_Xml_GivesTheRig()
    {
        var rig = RigFile.Read(Xml, FileFormat.Xml);

        Assert.Equal(["Acme.Midi.dll"], rig.Sources.Assemblies);
        Assert.Equal(["C:/Schemas/Acme.xml"], rig.Sources.Schemas);
        Assert.Equal(2, rig.Devices.Count);

        var synth = rig.Find("SYNTH")!;
        Assert.Equal(["Schemas/AcmeCustom.xml"], synth.Sources.Schemas);
        Assert.Empty(synth.Sources.Assemblies);
        Assert.Equal(("Acme S-1", "Acme.AddressMap", "#3D8BFD"), (synth.Name, synth.MapRoot, synth.Colors.Accent));
        Assert.Equal(0x10, synth.DeviceValues["deviceId"]);
        Assert.Equal(0x7F1C, synth.DeviceValues["GROUPID"]);
        Assert.Equal(new EndpointReference("USB MIDI Interface", 2), synth.Endpoint);

        var pedalboard = rig.Find("pedalboard")!;
        Assert.Null(pedalboard.Endpoint);
        Assert.Empty(pedalboard.DeviceValues);
    }

    [Fact]
    public void Read_Json_GivesTheSameRigAsXml()
    {
        Assert.Equal(
            RigFile.Write(RigFile.Read(Xml, FileFormat.Xml), FileFormat.Xml),
            RigFile.Write(RigFile.Read(Json, FileFormat.Json), FileFormat.Xml));
    }

    [Theory]
    [InlineData(FileFormat.Xml)]
    [InlineData(FileFormat.Json)]
    public void Write_OfWhatWasRead_GivesTheSameFile(FileFormat format)
    {
        var written = RigFile.Write(RigFile.Read(Xml, FileFormat.Xml), format);

        Assert.Equal(written, RigFile.Write(RigFile.Read(written, format), format));
    }

    [Theory]
    [InlineData(FileFormat.Xml)]
    [InlineData(FileFormat.Json)]
    public void Write_HasTheColourTheUserChanged(FileFormat format)
    {
        var rig = RigFile.Read(Xml, FileFormat.Xml);
        rig.Find("synth")!.Colors = new AreaColors("#A8423A", Panel: "#1F2A3A");
        rig.Find("pedalboard")!.Colors = AreaColors.None;

        var read = RigFile.Read(RigFile.Write(rig, format), format);

        Assert.Equal(new AreaColors("#A8423A", Panel: "#1F2A3A"), read.Find("synth")!.Colors);
        Assert.Equal(AreaColors.None, read.Find("pedalboard")!.Colors);
    }

    [Fact]
    public void Write_DeviceValues_InHex()
    {
        var json = RigFile.Write(RigFile.Read(Xml, FileFormat.Xml), FileFormat.Json);

        Assert.Contains("\"deviceId\": \"0x10\"", json);
        Assert.Contains("\"groupId\": \"0x7F1C\"", json);
        Assert.Contains("\"assemblies\": [", json);
        Assert.Contains("\"schemas\": [", json);
    }

    [Fact]
    public void Read_ASourceWithoutPath_IsReported()
    {
        var xml = """<rig format="jacobi.midi.rig/1"><assembly file="Acme.dll" /></rig>""";

        var exception = Assert.Throws<FileFormatException>(() => RigFile.Read(xml, FileFormat.Xml));

        Assert.Contains("rig/assembly[0]: 'path' is required", exception.Message);
    }

    [Theory]
    [InlineData("""<deviceValue name="deviceId" value="ten" />""", "'value': 'ten' is not a number")]
    [InlineData("""<deviceValue name="deviceId" value="1" /><deviceValue name="DeviceId" value="2" />""", "given more than once")]
    [InlineData("""<endpoint name="Port" group="16" />""", "is not a UMP group")]
    [InlineData("""<endpoint name="Port" /><endpoint name="Other" />""", "one endpoint")]
    [InlineData("""<endpoint group="1" />""", "'name' is required")]
    [InlineData("""<port name="Port" />""", "a device cannot contain 'port'")]
    public void Read_ReportsWhatDoesNotFit(string content, string message)
    {
        var xml = $"""<rig format="jacobi.midi.rig/1"><device alias="synth" mapRoot="Acme.AddressMap">{content}</device></rig>""";

        var exception = Assert.Throws<FileFormatException>(() => RigFile.Read(xml, FileFormat.Xml));

        Assert.Contains(message, exception.Message);
        Assert.StartsWith("rig/device[0]", exception.Message);
    }

    [Theory]
    [InlineData("""<rig format="jacobi.midi.dashboard/1" />""", "is not supported")]
    [InlineData("""<rig format="jacobi.midi.rig/1"><device alias="a" /></rig>""", "'mapRoot' is required")]
    [InlineData("""<rig format="jacobi.midi.rig/1"><device alias="a" mapRoot="X" /><device alias="a" mapRoot="Y" /></rig>""", "used more than once")]
    [InlineData("""<rig format="jacobi.midi.rig/1"><dashboard /></rig>""", "a rig cannot contain 'dashboard'")]
    public void Read_ReportsAnInvalidRig(string xml, string message)
    {
        var exception = Assert.Throws<FileFormatException>(() => RigFile.Read(xml, FileFormat.Xml));

        Assert.Contains(message, exception.Message);
    }
}
