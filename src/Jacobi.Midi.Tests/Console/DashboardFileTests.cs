using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Files;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// Reading and writing dashboards as XML and JSON: both formats give the same dashboard, writing what was read gives
/// the same file again, and content that does not fit is reported with its location.
/// </summary>
public class DashboardFileTests
{
    private const string Xml = """
        <dashboard format="jacobi.midi.dashboard/1" name="Lead sound" cellSize="40">
          <device alias="synth" mapRoot="Acme.AddressMap" color="#3D8BFD" />
          <device alias="pedalboard" mapRoot="Gx.AddressMap" />
          <assembly path="Custom.dll" />
          <row gap="1" wrap="true">
            <variable name="patch" value="5" />
            <section label="Filter" direction="column" device="synth" base="AddressMap.Edit.Filter">
              <knob label="Cutoff" unit="Hz" decimals="1">
                <bind slot="value" parameter="Cutoff" />
              </knob>
              <knob label="Resonance" color="#E8590C" grow="2">
                <bind slot="value" parameter="Resonance" />
              </knob>
            </section>
            <envelope label="Amp env" size="6,3" unit="ms" device="synth" base="AddressMap.Edit.AmpEnv">
              <segment time="Attack" />
              <segment time="Decay" level="Sustain" hold="true" />
              <segment time="Release" />
            </envelope>
            <grid>
              <slider at="0,1">
                <bind slot="min" parameter="pedalboard:AddressMap.Programs[$patch].Program.Low" />
                <bind slot="max" parameter="pedalboard:AddressMap.Programs[$patch].Program.High" />
              </slider>
            </grid>
            <numberBox label="Patch">
              <bind slot="value" parameter="$patch" />
            </numberBox>
          </row>
        </dashboard>
        """;

    private const string Json = """
        {
          // comments and trailing commas are allowed
          "format": "jacobi.midi.dashboard/1",
          "name": "Lead sound",
          "cellSize": 40,
          "devices": [
            { "alias": "synth", "mapRoot": "Acme.AddressMap", "color": "#3D8BFD" },
            { "alias": "pedalboard", "mapRoot": "Gx.AddressMap" },
          ],
          "assemblies": [ "Custom.dll" ],
          "layout": {
            "type": "row", "gap": 1, "wrap": true,
            "variables": [ { "name": "patch", "value": 5 } ],
            "children": [
              { "type": "section", "label": "Filter", "direction": "column", "device": "synth", "base": "AddressMap.Edit.Filter",
                "children": [
                  { "type": "knob", "label": "Cutoff", "unit": "Hz", "decimals": 1, "bind": { "value": "Cutoff" } },
                  { "type": "knob", "label": "Resonance", "color": "#E8590C", "grow": 2, "bind": { "value": "Resonance" } }
                ] },
              { "type": "envelope", "label": "Amp env", "size": [6, 3], "unit": "ms", "device": "synth", "base": "AddressMap.Edit.AmpEnv",
                "segments": [
                  { "time": "Attack" },
                  { "time": "Decay", "level": "Sustain", "hold": true },
                  { "time": "Release" }
                ] },
              { "type": "grid",
                "children": [
                  { "type": "slider", "at": [0, 1], "bind": {
                      "min": "pedalboard:AddressMap.Programs[$patch].Program.Low",
                      "max": "pedalboard:AddressMap.Programs[$patch].Program.High" } }
                ] },
              { "type": "numberBox", "label": "Patch", "bind": { "value": "$patch" } }
            ]
          }
        }
        """;

    [Fact]
    public void Read_Xml_GivesTheDashboard()
    {
        var dashboard = DashboardFile.Read(Xml, FileFormat.Xml);

        Assert.Equal("Lead sound", dashboard.Name);
        Assert.Equal(40, dashboard.CellSize);
        Assert.Equal([new DashboardDevice("synth", "Acme.AddressMap", "#3D8BFD"), new DashboardDevice("pedalboard", "Gx.AddressMap")], dashboard.Devices);
        Assert.Equal(["Custom.dll"], dashboard.Sources.Assemblies);

        var row = dashboard.Layout;
        Assert.Equal(LayoutControlKind.Row, row.Kind);
        Assert.Equal(1, row.Gap);
        Assert.True(row.Wrap);
        Assert.Equal([new IndexVariable("patch", 5)], row.Variables);
        Assert.Equal(4, row.Children.Count);

        var section = Assert.IsType<LayoutControl>(row.Children[0]);
        Assert.Equal(LayoutControlKind.Section, section.Kind);
        Assert.Equal(LayoutDirection.Column, section.Direction);
        Assert.Equal(("synth", "AddressMap.Edit.Filter"), (section.Device, section.Base));

        var cutoff = Assert.IsType<ValueControl>(section.Children[0]);
        Assert.Equal(ParameterControlKind.Knob, cutoff.Kind);
        Assert.Equal(new DisplaySettings { Unit = "Hz", Decimals = 1 }, cutoff.Display);
        Assert.Equal(new Binding("Cutoff"), cutoff.Bindings[SlotNames.Value]);

        var resonance = Assert.IsType<ValueControl>(section.Children[1]);
        Assert.Equal(("#E8590C", 2), (resonance.Color, resonance.Grow));

        var envelope = Assert.IsType<EnvelopeControl>(row.Children[1]);
        Assert.Equal(new CellSize(6, 3), envelope.Size);
        Assert.Equal(
            [
                new EnvelopeSegment(new Binding("Attack")),
                new EnvelopeSegment(new Binding("Decay"), new Binding("Sustain"), Hold: true),
                new EnvelopeSegment(new Binding("Release")),
            ],
            envelope.Segments);

        var slider = Assert.IsType<ValueControl>(Assert.IsType<LayoutControl>(row.Children[2]).Children[0]);
        Assert.Equal(new CellPosition(0, 1), slider.At);
        Assert.Equal(new Binding("AddressMap.Programs[$patch].Program.Low") { Device = "pedalboard" }, slider.Bindings[SlotNames.Min]);

        var selector = Assert.IsType<ValueControl>(row.Children[3]);
        Assert.Equal(ParameterControlKind.NumberBox, selector.Kind);
        Assert.Equal("patch", selector.Bindings[SlotNames.Value].Variable);
    }

    [Fact]
    public void Read_Json_GivesTheSameDashboardAsXml()
    {
        var fromXml = DashboardFile.Read(Xml, FileFormat.Xml);
        var fromJson = DashboardFile.Read(Json, FileFormat.Json);

        Assert.Equal(DashboardFile.Write(fromXml, FileFormat.Xml), DashboardFile.Write(fromJson, FileFormat.Xml));
    }

    [Theory]
    [InlineData(FileFormat.Xml)]
    [InlineData(FileFormat.Json)]
    public void Write_OfWhatWasRead_GivesTheSameFile(FileFormat format)
    {
        var written = DashboardFile.Write(DashboardFile.Read(Xml, FileFormat.Xml), format);

        Assert.Equal(written, DashboardFile.Write(DashboardFile.Read(written, format), format));
    }

    [Fact]
    public void Write_Json_GroupsChildrenByRole()
    {
        var json = DashboardFile.Write(DashboardFile.Read(Xml, FileFormat.Xml), FileFormat.Json);

        Assert.Contains("\"devices\": [", json);
        Assert.Contains("\"layout\": {", json);
        Assert.Contains("\"type\": \"envelope\"", json);
        Assert.Contains("\"value\": \"Cutoff\"", json);
        Assert.Contains("\"hold\": true", json);
        Assert.DoesNotContain("\"type\": \"segment\"", json);
    }

    [Theory]
    [InlineData("""<knob label="Cutoff" colour="#FF0000" />""", "dashboard/row[0]/knob[0]: unknown attribute 'colour'")]
    [InlineData("""<knob><bind slot="value" parameter="Cutoff" /><segment time="Attack" /></knob>""", "cannot contain 'segment'")]
    [InlineData("""<knob><bind slot="x" parameter="Cutoff" /></knob>""", "has no slot 'x'")]
    [InlineData("""<knob><bind slot="value" parameter="Cutoff" /><bind slot="value" parameter="Reso" /></knob>""", "bound more than once")]
    [InlineData("""<knob><bind slot="value" parameter="Level[x]" /></knob>""", "'parameter': 'Level[x]'")]
    [InlineData("""<knob><bind slot="value" /></knob>""", "'parameter' is required")]
    [InlineData("""<knob color="red" />""", "is not a colour")]
    [InlineData("""<knob size="0,2" />""", "smaller than one cell")]
    [InlineData("""<knob size="two" />""", "not a pair of whole numbers")]
    [InlineData("""<section direction="diagonal" />""", "'direction': 'diagonal' is not one of row, column")]
    [InlineData("""<section base="Edit..Filter[" />""", "'base'")]
    [InlineData("""<envelope><segment time="Attack"><bind slot="value" parameter="X" /></segment></envelope>""", "'segment' cannot contain 'bind'")]
    [InlineData("""<dial />""", "'dial' is not a control")]
    [InlineData("""<section><variable name="1st" /></section>""", "'1st' is not a variable name")]
    [InlineData("""<section><variable name="patch" /><variable name="patch" /></section>""", "declares the variable 'patch' more than once")]
    [InlineData("""<section><variable name="patch" value="-1" /></section>""", "-1 is less than 0")]
    [InlineData("""<numberBox><bind slot="value" parameter="$1patch" /></numberBox>""", "'1patch' is not a variable name")]
    [InlineData("""<indexSelector label="Patch" variable="patch" />""", "'indexSelector' is not a control")]
    [InlineData("""<knob><variable name="patch" /></knob>""", "'knob' cannot contain 'variable'")]
    public void Read_ReportsWhatDoesNotFit(string control, string message)
    {
        var xml = $"""<dashboard format="jacobi.midi.dashboard/1" name="Test"><row>{control}</row></dashboard>""";

        var exception = Assert.Throws<FileFormatException>(() => DashboardFile.Read(xml, FileFormat.Xml));

        Assert.Contains(message, exception.Message);
    }

    [Theory]
    [InlineData("""<dashboard name="Test"><row /></dashboard>""", "'format' is required")]
    [InlineData("""<dashboard format="jacobi.midi.dashboard/2" name="Test"><row /></dashboard>""", "is not supported")]
    [InlineData("""<dashboard format="jacobi.midi.dashboard/1"><row /></dashboard>""", "'name' is required")]
    [InlineData("""<dashboard format="jacobi.midi.dashboard/1" name="Test" />""", "has no layout control")]
    [InlineData("""<dashboard format="jacobi.midi.dashboard/1" name="Test"><knob /></dashboard>""", "must be a layout control")]
    [InlineData("""<dashboard format="jacobi.midi.dashboard/1" name="Test"><row /><row /></dashboard>""", "one layout control")]
    [InlineData("""<dashboard format="jacobi.midi.dashboard/1" name="Test"><device alias="a" mapRoot="X" /><device alias="A" mapRoot="Y" /><row /></dashboard>""", "used more than once")]
    [InlineData("""<rig format="jacobi.midi.rig/1" />""", "root element is 'rig'")]
    [InlineData("""<dashboard""", "Invalid XML")]
    public void Read_ReportsAnInvalidDashboard(string xml, string message)
    {
        var exception = Assert.Throws<FileFormatException>(() => DashboardFile.Read(xml, FileFormat.Xml));

        Assert.Contains(message, exception.Message);
    }

    [Theory]
    [InlineData("""{ "format": "jacobi.midi.dashboard/1", "name": "Test", "layout": { "gap": 1 } }""", "every element needs a 'type'")]
    [InlineData("""{ "format": "jacobi.midi.dashboard/1", "name": "Test", "layout": [] }""", "'layout' must be a JSON object")]
    [InlineData("""{ "format": "jacobi.midi.dashboard/1", "name": "Test", "layout": { "type": "row", "gap": { } } }""", "'gap': a Object is not a valid value")]
    [InlineData("""{ "format": "jacobi.midi.dashboard/1", "name": "Test", "devices": [ { "type": "x", "alias": "a", "mapRoot": "X" } ], "layout": { "type": "row" } }""", "unknown attribute 'type'")]
    [InlineData("""[]""", "must hold a JSON object")]
    [InlineData("""{ "format": """, "Invalid JSON")]
    public void Read_Json_ReportsWhatDoesNotFit(string json, string message)
    {
        var exception = Assert.Throws<FileFormatException>(() => DashboardFile.Read(json, FileFormat.Json));

        Assert.Contains(message, exception.Message);
    }

    [Theory]
    [InlineData("lead.dashboard.xml", FileFormat.Xml)]
    [InlineData("lead.dashboard.JSON", FileFormat.Json)]
    public void FileFormat_FollowsTheExtension(string path, FileFormat format)
    {
        Assert.Equal(format, FileFormats.FromPath(path));
    }

    [Fact]
    public void FileFormat_OfAnUnknownExtension_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => FileFormats.FromPath("lead.dashboard.yaml"));
    }
}
