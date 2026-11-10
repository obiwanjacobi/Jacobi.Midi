using Jacobi.BinarySerializer;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Tests.Addressing;
using Jacobi.Midi.Tests.Schema;
using Xunit;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Tests.Messages;

/// <summary>
/// Tests <see cref="MessageMapValidator"/> and <see cref="AddressMapTags"/>: the use of the midi.* tags in the schemas and
/// how the roles of the envelope messages are fulfilled.
/// </summary>
public class MessageMapValidatorTests
{
    private static IReadOnlyList<MessageMapProblem> Validate(params string[] xml)
        => MessageMapValidator.Validate(TestDevice.CreateSerializer([.. xml.Select(TestDevice.Parse)]));

    private static IEnumerable<MessageMapProblem> Of(IEnumerable<MessageMapProblem> problems, MessageMapProblemSeverity severity)
        => problems.Where(problem => problem.Severity == severity);

    [Fact]
    public void Validate_TestDevice_OnlyTheDeviceIdIsSuppliedByTheClient()
    {
        var problems = Validate();

        Assert.Empty(Of(problems, MessageMapProblemSeverity.Error));
        Assert.Empty(Of(problems, MessageMapProblemSeverity.Warning));
        var information = Of(problems, MessageMapProblemSeverity.Information).ToList();
        // the data message is the one the map finds; the request is only a message when an exchange file names it
        Assert.Equal(["Message.Data.DeviceId"], information.Select(p => p.Path.Value));
        Assert.All(information, problem => Assert.Equal("TestEnvelope", problem.Document));
    }

    [Fact]
    public void Validate_ADumpThatIsAMessageItself_IsValidatedLikeAnyMap()
    {
        var problems = Validate("""
            <schema name="Dump">
              <includes>
                <include schema="midi" />
              </includes>
              <members>
                <group name="Voice" midi.device.modelId="0x10" midi.device.unused="1">
                  <processors>
                    <processor name="midi.sysex" />
                  </processors>
                  <members>
                    <field name="Id" datatype="sys.uint8" value="0x7D" />
                    <field name="Device" datatype="sys.uint8" midi.role="device.deviceId" />
                    <field name="Model" datatype="sys.uint8" midi.role="device.modelId" />
                    <field name="Level" datatype="sys.uint8" />
                  </members>
                </group>
                <group name="Plain">
                  <members>
                    <field name="Value" datatype="sys.uint8" />
                  </members>
                </group>
              </members>
            </schema>
            """).Where(problem => problem.Document == "Dump").ToList();

        // the device id is the client's, the model is fixed by the root; a value no role uses is a warning
        Assert.Empty(Of(problems, MessageMapProblemSeverity.Error));
        Assert.Equal(["Voice.Device"], Of(problems, MessageMapProblemSeverity.Information).Select(p => p.Path.Value));
        Assert.Contains("midi.device.unused", Assert.Single(Of(problems, MessageMapProblemSeverity.Warning)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RolandSchemas_NoErrorsOrWarnings()
    {
        var serializer = new SerializerBuilder()
            .AddSchemas(TestHelpers.CreateSchemas())
            .AddProcessors(TestHelpers.CreateProcessors())
            .Build();

        var problems = MessageMapValidator.Validate(serializer);

        Assert.Empty(Of(problems, MessageMapProblemSeverity.Error));
        Assert.Empty(Of(problems, MessageMapProblemSeverity.Warning));
        Assert.Contains(problems, p => String.Equals(p.Document, "Roland", StringComparison.OrdinalIgnoreCase) && p.Path.Value == "Message16.DT1.DeviceId");
    }

    [Fact]
    public void Validate_InvalidRoleAndMisplacedData_AreErrors()
    {
        var problems = Validate("""
            <schema name="Other">
              <members>
                <group name="Message">
                  <members>
                    <field name="Bad" datatype="sys.uint8" midi.role="adress" />
                    <field name="Data" datatype="sys.uint8" midi.role="data" />
                  </members>
                </group>
              </members>
            </schema>
            """);

        var errors = Of(problems, MessageMapProblemSeverity.Error).Where(p => p.Document == "Other").ToList();
        Assert.Contains(errors, p => p.Path.Value == "Message.Bad");
        Assert.Contains(errors, p => p.Path.Value == "Message.Data");
    }

    [Fact]
    public void Validate_DataChoiceWithoutMapRoot_IsInformation()
    {
        var problems = Validate("""
            <schema name="Other">
              <nodeDefs>
                <group name="body" />
              </nodeDefs>
              <members>
                <group name="Envelope">
                  <members>
                    <group name="Data">
                      <members>
                        <field name="Address" datatype="sys.uint8" byteLength="1" midi.role="address" />
                        <choice name="Body" memberNodeDef="body" midi.role="data" />
                      </members>
                    </group>
                  </members>
                </group>
              </members>
            </schema>
            """);

        var problem = Assert.Single(problems, p => p.Document == "Other");
        Assert.Equal(MessageMapProblemSeverity.Information, problem.Severity);
        Assert.Equal("Envelope.Data.Body", problem.Path.Value);
        Assert.Contains("Other.body", problem.Message);
    }

    [Fact]
    public void Validate_UnusedDeviceValueOnTheMapRoot_IsAWarning()
    {
        var problems = Validate("""
            <schema name="Other">
              <includes>
                <include schema="TestEnvelope" />
              </includes>
              <members>
                <group name="Map" nodeDef="TestEnvelope.body" midi.device.modelId="0x43" midi.device.color="1">
                  <members>
                    <field name="Value" datatype="sys.uint8" />
                  </members>
                </group>
              </members>
            </schema>
            """);

        var problem = Assert.Single(Of(problems, MessageMapProblemSeverity.Warning));
        Assert.Equal("Other", problem.Document);
        Assert.Equal("Map", problem.Path.Value);
        Assert.Contains("midi.device.color", problem.Message);
    }

    [Fact]
    public void Validate_InvalidMapIsReportedPerNode()
    {
        var problems = Validate("""
            <schema name="Other">
              <includes>
                <include schema="TestEnvelope" />
              </includes>
              <members>
                <group name="Map" nodeDef="TestEnvelope.body">
                  <members>
                    <field name="Value" datatype="sys.uint8" midi.repeat="2" />
                  </members>
                </group>
              </members>
            </schema>
            """);

        var problem = Assert.Single(Of(problems, MessageMapProblemSeverity.Error));
        Assert.Equal("Other", problem.Document);
        Assert.Equal("Map.Value", problem.Path.Value);
    }

    [Fact]
    public void Validate_Transfer_AContradictionIsAnError_ARedundancyIsNot()
    {
        var problems = Validate("""
            <schema name="Other">
              <includes>
                <include schema="TestEnvelope" />
              </includes>
              <members>
                <group name="Map" nodeDef="TestEnvelope.body">
                  <members>
                    <group name="Outer" midi.transfer="atomic">
                      <members>
                        <group name="Inner" midi.transfer="atomic">
                          <members>
                            <field name="A" datatype="sys.uint8" />
                          </members>
                        </group>
                        <repeat name="Items" count="2" midi.transfer="individual">
                          <members>
                            <field name="B" datatype="sys.uint8" />
                          </members>
                        </repeat>
                      </members>
                    </group>
                    <repeat name="Chain" count="2" midi.transfer="atomic">
                      <members>
                        <field name="C" datatype="sys.uint8" />
                      </members>
                    </repeat>
                    <field name="D" datatype="sys.uint8" midi.transfer="atomic" />
                    <group name="E" midi.transfer="whole">
                      <members>
                        <field name="F" datatype="sys.uint8" />
                      </members>
                    </group>
                  </members>
                </group>
              </members>
            </schema>
            """);

        // individual inside atomic, transfer on a field, an unknown value; atomic in atomic and on a repeat are fine
        Assert.Equal(
            ["Map.D", "Map.E", "Map.Outer.Items"],
            Of(problems, MessageMapProblemSeverity.Error).Select(problem => problem.Path.Value).Order());
    }

    [Theory]
    [InlineData("midi.role", "address", true)]
    [InlineData("midi.role", "device.modelId", true)]
    [InlineData("midi.role", "device.", false)]
    [InlineData("midi.role", "adress", false)]
    // the exchange patterns are in the exchange files, not in the schemas
    [InlineData("midi.message", "data", false)]
    [InlineData("midi.messageInterval", "20", false)]
    [InlineData("midi.address", "0x40-00", true)]
    [InlineData("midi.address", "forty", false)]
    [InlineData("midi.transfer", "atomic", true)]
    [InlineData("midi.transfer", "individual", true)]
    [InlineData("midi.transfer", "true", false)]
    [InlineData("midi.atomic", "true", false)]
    [InlineData("midi.access", "read", true)]
    [InlineData("midi.unit", "field", true)]
    [InlineData("midi.exchange", "acknowledged", false)]
    [InlineData("midi.device.modelId", "0x13", true)]
    [InlineData("midi.device.modelId", "x", false)]
    [InlineData("midi.adress", "0x40-00", false)]
    public void Tags_Validate(string name, string value, bool isValid)
    {
        Assert.True(AddressMapTags.IsTag(name));
        Assert.Equal(isValid, AddressMapTags.Validate(name, value) is null);
    }

    [Theory]
    [InlineData("midi.sysex.bytes")]
    [InlineData("sys.align.bytes")]
    [InlineData("role")]
    public void Tags_ProcessorPropertiesAreNotTags(string name)
        => Assert.False(AddressMapTags.IsTag(name));

    [Fact]
    public void Info_DescribeRole_OfAnAddressMap()
    {
        var map = new MessageMaps(TestDevice.CreateSerializer()).Info(TestDevice.MapName);

        Assert.NotNull(map.DescribeRole("address"));
        Assert.NotNull(map.DescribeRole("size"));
        Assert.Contains("0x42", map.DescribeRole("device.modelId"));
        // the map root does not fix the device id: the client supplies it
        Assert.Null(map.DescribeRole("device.deviceId"));
    }
}
