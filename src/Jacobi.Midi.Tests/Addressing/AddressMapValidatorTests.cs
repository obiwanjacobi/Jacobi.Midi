using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Tests.Schema;
using Xunit;

namespace Jacobi.Midi.Tests.Addressing;

/// <summary>
/// Tests <see cref="AddressMapValidator"/> and <see cref="AddressMapTags"/>: the use of the midi.* tags in the schemas and
/// how the roles of the envelope messages are fulfilled.
/// </summary>
public class AddressMapValidatorTests
{
    private static IReadOnlyList<AddressMapProblem> Validate(params string[] xml)
        => AddressMapValidator.Validate(TestDevice.CreateSerializer([.. xml.Select(TestDevice.Parse)]));

    private static IEnumerable<AddressMapProblem> Of(IEnumerable<AddressMapProblem> problems, AddressMapProblemSeverity severity)
        => problems.Where(problem => problem.Severity == severity);

    [Fact]
    public void Validate_TestDevice_OnlyTheDeviceIdIsSuppliedByTheClient()
    {
        var problems = Validate();

        Assert.Empty(Of(problems, AddressMapProblemSeverity.Error));
        Assert.Empty(Of(problems, AddressMapProblemSeverity.Warning));
        var information = Of(problems, AddressMapProblemSeverity.Information).ToList();
        // the data message is the one the map finds; the request is only a message when an exchange file names it
        Assert.Equal(["Message.Data.DeviceId"], information.Select(p => p.Path.Value));
        Assert.All(information, problem => Assert.Equal("TestEnvelope", problem.Document));
    }

    [Fact]
    public void Validate_RolandSchemas_NoErrorsOrWarnings()
    {
        var serializer = new SerializerBuilder()
            .AddSchemas(TestHelpers.CreateSchemas())
            .AddProcessors(TestHelpers.CreateProcessors())
            .Build();

        var problems = AddressMapValidator.Validate(serializer);

        Assert.Empty(Of(problems, AddressMapProblemSeverity.Error));
        Assert.Empty(Of(problems, AddressMapProblemSeverity.Warning));
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

        var errors = Of(problems, AddressMapProblemSeverity.Error).Where(p => p.Document == "Other").ToList();
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
        Assert.Equal(AddressMapProblemSeverity.Information, problem.Severity);
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
                <group name="Map" nodeDef="TestEnvelope.body" midi.device.modelId="0x43" midi.device.colour="1">
                  <members>
                    <field name="Value" datatype="sys.uint8" />
                  </members>
                </group>
              </members>
            </schema>
            """);

        var problem = Assert.Single(Of(problems, AddressMapProblemSeverity.Warning));
        Assert.Equal("Other", problem.Document);
        Assert.Equal("Map", problem.Path.Value);
        Assert.Contains("midi.device.colour", problem.Message);
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

        var problem = Assert.Single(Of(problems, AddressMapProblemSeverity.Error));
        Assert.Equal("Other", problem.Document);
        Assert.Equal("Map.Value", problem.Path.Value);
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
    [InlineData("midi.atomic", "true", true)]
    [InlineData("midi.atomic", "yes", false)]
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
    public void Adapter_DescribeRole()
    {
        var adapter = new AddressMapAdapter(TestDevice.CreateSerializer());

        Assert.NotNull(adapter.DescribeRole(TestDevice.MapName, "address"));
        Assert.NotNull(adapter.DescribeRole(TestDevice.MapName, "size"));
        Assert.Contains("0x42", adapter.DescribeRole(TestDevice.MapName, "device.modelId"));
        // the map root does not fix the device id: the client supplies it
        Assert.Null(adapter.DescribeRole(TestDevice.MapName, "device.deviceId"));
    }
}
