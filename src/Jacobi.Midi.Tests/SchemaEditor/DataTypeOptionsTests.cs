using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class DataTypeOptionsTests
{
    [Fact]
    public void RegisteredType_IsDescribedByItsSizeAndClrType()
    {
        var options = DataTypeOptions.Get(null);

        Assert.Equal("1 byte · Byte", options.Single(option => option.Name == "sys.uint8").Description);
        Assert.Equal("2 bytes · Int16", options.Single(option => option.Name == "sys.int16").Description);
        Assert.StartsWith("variable · ", options.Single(option => option.Name == "sys.string").Description);
    }

    [Fact]
    public void DataTypeDef_IsDescribedByItsBaseAndTheFacetsThatAreSet()
    {
        Assert.Equal("sys.uint8", Describe(new EditDataTypeDef { Name = "plain", BasedOn = new SchemaName("sys.uint8") }));
        Assert.Equal("sys.uint8 · 0..127", Describe(new EditDataTypeDef { Name = "level", BasedOn = new SchemaName("sys.uint8"), Min = "0", Max = "127" }));
        Assert.Equal("sys.uint8 · 2 options", Describe(new EditDataTypeDef
        {
            Name = "onOff",
            BasedOn = new SchemaName("sys.uint8"),
            Options = new() { ["0"] = "Off", ["1"] = "On" }
        }));
        Assert.Equal("sys.uint8 · scale 1, shift -64 · as sys.int16", Describe(new EditDataTypeDef
        {
            Name = "pan",
            BasedOn = new SchemaName("sys.uint8"),
            Scale = 1,
            Shift = -64,
            ValueType = new SchemaName("sys.int16")
        }));
        Assert.Equal("sys.uint16 · 0..1000 · midi.sysexbyte, roland.checksum", Describe(new EditDataTypeDef
        {
            Name = "packed",
            BasedOn = new SchemaName("sys.uint16"),
            Min = "0",
            Max = "1000",
            Processors = [new EditProcessorRef { Processor = new SchemaName("midi.sysexbyte") }, new EditProcessorRef { Processor = new SchemaName("roland.checksum") }]
        }));
    }

    [Fact]
    public void DataTypeDef_IsListedWithItsDescription()
    {
        var document = new EditDocument
        {
            Name = "Test",
            Members = [],
            Processors = [],
            Properties = [],
            NodeDefs = [],
            DataTypeDefs = [new EditDataTypeDef { Name = "level", BasedOn = new SchemaName("sys.uint8"), Max = "127" }],
            ProcessorDefs = [],
            Includes = []
        };

        var option = DataTypeOptions.Get(SchemaScope.Of(document)).Single(option => option.Name == "Test.level");

        Assert.Equal("sys.uint8 · ..127", option.Description);
        Assert.Equal("Test.level", option.ToString());
    }

    private static string Describe(EditDataTypeDef definition) => DataTypeOptions.Describe(definition);
}
