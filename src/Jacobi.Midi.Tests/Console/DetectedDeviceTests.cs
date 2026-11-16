using System.Text.Json.Nodes;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Devices;
using Jacobi.Midi.Console.Core.Parameters;
using Jacobi.Midi.Console.Core.Files;
using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Device.PropertyExchange;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// A device detected with MIDI-CI Property Exchange in the console: its generated schema saved as a schema file of the
/// rig, its Property Exchange roots, and the mark in the rig file.
/// </summary>
public class DetectedDeviceTests
{
    // the resources of the General MIDI synthesizer of Windows MIDI Services, with the published schemas
    private static PropertySchema Generate()
        => PropertySchemaGenerator.Generate("GeneralMIDISynth", PropertyResource.FromResourceList(JsonNode.Parse(
            """[{"resource":"DeviceInfo"},{"resource":"ChannelList","canSubscribe":true},{"resource":"ChCtrlList"}]""")));

    [Fact]
    public void ASavedGeneratedSchema_GivesTheDeviceItsPropertyExchangeRoots()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.schema.xml");
        File.WriteAllText(path, Jacobi.BinarySerializer.Schema.Xml.XmlSerializer.Serialize(Generate().Document));
        try
        {
            var library = ConsoleLibrary.BuiltIn().AddSchemaFile(path).Build();

            var state = new DeviceState(new DeviceInstance { Alias = "synth", Schema = "GeneralMIDISynth", IsDetected = true }, library);

            Assert.Contains("GeneralMIDISynth", library.DeviceSchemas);
            Assert.True(library.HasPropertyRoots("GeneralMIDISynth"));
            Assert.False(library.HasPropertyRoots("Gx700"));
            Assert.Equal(["DeviceInfo", "ChannelList", "ChCtrlList"], state.Roots.Select(root => root.Name));
            Assert.Equal(["DeviceInfo", "ChannelList", "ChCtrlList"], state.Parameters.Select(node => node.Name));
            // its repeats get their count from the schema (there is no message map)
            Assert.Equal(3, state.Parameters[0].Children.Single(node => node.Name == "manufacturerId").ItemCount);
            Assert.NotNull(state.Find("ChannelList.items.channel"));
            // the exchanges a dashboard names apply to a root with a message map only
            Assert.False(state.UsesExchanges("DeviceInfo.model"));
            Assert.False(state.UsesExchanges("Other.value"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AParameterOfAResourceThatCannotBeSet_IsReadOnly()
    {
        var schema = PropertySchemaGenerator.Generate("Settable", PropertyResource.FromResourceList(JsonNode.Parse(
            """[{"resource":"DeviceInfo"},{"resource":"X-Tempo","canSet":"full","schema":{"type":"integer","minimum":40,"maximum":240}}]""")));
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.schema.xml");
        File.WriteAllText(path, Jacobi.BinarySerializer.Schema.Xml.XmlSerializer.Serialize(schema.Document));
        try
        {
            var library = ConsoleLibrary.BuiltIn().AddSchemaFile(path).Build();
            var state = new DeviceState(new DeviceInstance { Alias = "device", Schema = "Settable", IsDetected = true }, library);

            var model = ParameterInfo.From(Assert.IsType<Jacobi.BinarySerializer.Execution.FieldInfo>(state.Find("DeviceInfo.model")));
            var tempo = ParameterInfo.From(Assert.IsType<Jacobi.BinarySerializer.Execution.FieldInfo>(state.Find("XTempo.value")));

            // the access of the root (midi.access="read" from canSet none) is the access of its fields
            Assert.True(model.IsReadOnly);
            Assert.False(tempo.IsReadOnly);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AVariable_IndexesARepeatOfTheRootThePathStartsAt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.schema.xml");
        File.WriteAllText(path, Jacobi.BinarySerializer.Schema.Xml.XmlSerializer.Serialize(Generate().Document));
        try
        {
            var library = ConsoleLibrary.BuiltIn().AddSchemaFile(path).Build();
            var rig = $"""
                <rig format="jacobi.midi.rig/1">
                  <schema path="{path}" />
                  <device alias="synth" schema="GeneralMIDISynth" detected="true" />
                </rig>
                """;
            const string dashboard = """
                <dashboard format="jacobi.midi.dashboard/1" name="Synth">
                  <device alias="synth" schema="GeneralMIDISynth" />
                  <column device="synth">
                    <variable name="channel" value="0" />
                    <variable name="byte" value="0" />
                    <numberBox><bind slot="value" parameter="ChannelList.items[$channel].channel" /></numberBox>
                    <numberBox><bind slot="value" parameter="DeviceInfo.manufacturerId[$byte].item" /></numberBox>
                  </column>
                </dashboard>
                """;
            using var session = DashboardSession.Open(RigSession.Open(RigFile.Read(rig, FileFormat.Xml), library), DashboardFile.Read(dashboard, FileFormat.Xml));

            // a list resource: its number of items only the data gives, so its variable has no range yet; no problem
            Assert.Empty(session.Problems);
            var channel = new VariableReference((LayoutControl)session.Dashboard.Layout, "channel");
            Assert.False(session.Resolution.ItemCounts.ContainsKey(channel));
            // a repeat with a count: the variable has its range
            Assert.Equal(3, session.Resolution.ItemCounts[new VariableReference((LayoutControl)session.Dashboard.Layout, "byte")]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(FileFormat.Xml)]
    [InlineData(FileFormat.Json)]
    public void ADetectedDevice_IsMarkedInTheRigFile(FileFormat format)
    {
        var rig = new Rig
        {
            Devices = [new DeviceInstance { Alias = "synth", Schema = "GeneralMIDISynth", IsDetected = true },
                new DeviceInstance { Alias = "gx", Schema = "Gx700" }],
        };

        var read = RigFile.Read(RigFile.Write(rig, format), format);

        Assert.Equal([true, false], read.Devices.Select(device => device.IsDetected));
    }
}
