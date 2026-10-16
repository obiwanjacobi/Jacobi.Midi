using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.Services;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class SchemaEditorTests
{
    [Theory]
    [InlineData(SchemaFormat.Json)]
    [InlineData(SchemaFormat.Xml)]
    [InlineData(SchemaFormat.Yaml)]
    public void NewSchema_CanAddDocumentedFieldAndReload(SchemaFormat format)
    {
        var source = SchemaFileService.CreateTemplate("TestSchema", format);
        var schema = SchemaFileService.Parse(source, format);

        var group = Assert.Single(schema.Members);
        Assert.Equal("SysEx", group.Name);

        source = SchemaFileService.UpdateNode(
            source,
            format,
            [0],
            "SysEx",
            "sys.uint8",
            "The complete system-exclusive message.",
            String.Empty);
        source = SchemaFileService.AddGroup(source, format, [0], "Header");
        source = SchemaFileService.AddField(
            source,
            format,
            [0, 0],
            "Parameter",
            "sys.uint8",
            "2A");

        schema = SchemaFileService.Parse(source, format);

        var updatedGroup = Assert.IsType<SchemaGroup>(Assert.Single(schema.Members));
        Assert.Equal("The complete system-exclusive message.",
            Assert.Single(updatedGroup.Properties, property => property.Name == "description").Value);
        var header = Assert.IsType<SchemaGroup>(Assert.Single(updatedGroup.Members));
        Assert.Equal("Header", header.Name);
        var field = Assert.IsType<SchemaField>(Assert.Single(header.Members));
        Assert.Equal("Parameter", field.Name);
        Assert.Equal("sys.uint8", field.DataType?.ToString());
        Assert.Equal("2A",
            Assert.Single(field.Properties, property => property.Name == "sample").Value);
    }

    [Theory]
    [InlineData("schema.json", SchemaFormat.Json)]
    [InlineData("schema.xml", SchemaFormat.Xml)]
    [InlineData("schema.yaml", SchemaFormat.Yaml)]
    [InlineData("schema.yml", SchemaFormat.Yaml)]
    public void FromFileName_RecognizesSupportedFormats(string fileName, SchemaFormat expected)
    {
        Assert.Equal(expected, SchemaFileService.FromFileName(fileName));
    }

    [Fact]
    public void FromFileName_RejectsUnsupportedFormats()
    {
        Assert.Throws<NotSupportedException>(() => SchemaFileService.FromFileName("schema.syx"));
    }

    [Fact]
    public void SysExMessage_RecognizesMarkersAndCreatesIndexedBytes()
    {
        var message = new SysExMessage([0xF0, 0x41, 0x13, 0xF7]);

        Assert.True(message.HasStartMarker);
        Assert.True(message.HasEndMarker);
        Assert.Equal("F0 41 13 F7", message.Hex);
        Assert.Equal(4, message.ByteCells.Count);
        Assert.Equal("F0", message.ByteCells[0].Marker);
        Assert.Equal("F7", message.ByteCells[3].Marker);
    }
}
