using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Files;
using Jacobi.Midi.Console.Core.Libraries;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// Reading and writing libraries of user controls as XML and JSON, the rules a user control's section must fit
/// (no device of its own, so relative bases and bindings; only its own index variables) and replacing a user control by name.
/// </summary>
public class ControlLibraryFileTests
{
    private const string Xml = """
        <library format="jacobi.midi.library/1">
          <userControl name="Patch" nodeDef="Gx700.patch">
            <section size="20,12">
              <variable name="assign" value="0" />
              <section label="Compressor" at="0,0" size="8,9" base="Compressor">
                <knob label="Sustain" at="0,0" size="5,7"><bind slot="value" parameter="Sustain" /></knob>
              </section>
              <knob label="Target" at="9,0" size="5,7"><bind slot="value" parameter="Common.Assign[$assign].Target" /></knob>
              <numberBox label="Assign" at="9,8" size="5,3"><bind slot="value" parameter="$assign" /></numberBox>
            </section>
          </userControl>
          <userControl name="Compressor" nodeDef="Gx700.compressor">
            <section size="6,9">
              <knob at="0,0" size="5,7"><bind slot="value" parameter="Sustain" /></knob>
            </section>
          </userControl>
        </library>
        """;

    [Fact]
    public void Read_Xml_GivesTheUserControls()
    {
        var library = ControlLibraryFile.Read(Xml, FileFormat.Xml);

        Assert.Equal(["Patch", "Compressor"], library.Controls.Select(control => control.Name));
        var patch = library.Find("PATCH")!;
        Assert.Equal(("Gx700", "patch"), (patch.NodeDef.Namespace, patch.NodeDef.Name));
        Assert.Equal(LayoutControlKind.Section, patch.Section.Kind);
        Assert.Equal("Compressor", patch.Section.Children[0].Base);
    }

    [Theory]
    [InlineData(FileFormat.Xml)]
    [InlineData(FileFormat.Json)]
    public void Write_OfWhatWasRead_GivesTheSameFile(FileFormat format)
    {
        var written = ControlLibraryFile.Write(ControlLibraryFile.Read(Xml, FileFormat.Xml), format);

        Assert.Equal(written, ControlLibraryFile.Write(ControlLibraryFile.Read(written, format), format));
    }

    [Fact]
    public void Put_ReplacesTheUserControlWithTheSameName_InItsPlace()
    {
        var library = ControlLibraryFile.Read(Xml, FileFormat.Xml);
        var section = new LayoutControl { Kind = LayoutControlKind.Section };

        Assert.True(library.Put(new UserControlDefinition { Name = "patch", NodeDef = "Gx700.patch", Section = section }));
        Assert.False(library.Put(new UserControlDefinition { Name = "Reverb", NodeDef = "Gx700.reverb", Section = section }));

        Assert.Equal(["patch", "Compressor", "Reverb"], library.Controls.Select(control => control.Name));
        Assert.Same(section, library.Find("Patch")!.Section);
    }

    [Theory]
    [InlineData("""<row />""", "a user control is a section, not a row")]
    [InlineData("""<section base="AddressMap.Temporary" />""", "is one node, not 'AddressMap.Temporary'")]
    [InlineData("""<section device="fx" />""", "has the device 'fx'")]
    [InlineData("""<section><section device="fx" base="AddressMap.Temporary.Compressor" /></section>""", "has the device 'fx'")]
    [InlineData("""<section><knob><bind slot="value" parameter="fx:AddressMap.Temporary.Compressor.Sustain" /></knob></section>""", "the placement gives the device")]
    [InlineData("""<section><knob><bind slot="value" parameter="$patch" /></knob></section>""", "'$patch', which is not declared in the user control")]
    [InlineData("""<section><knob><bind slot="value" parameter="Common.Assign[$assign].Target" /></knob></section>""", "'$assign', which is not declared")]
    [InlineData("""<section><section base="Chain[$link]" /></section>""", "'$link', which is not declared")]
    [InlineData("""<section /><section />""", "has one section")]
    [InlineData("""<knob />""", "is a section, not a 'knob'")]
    public void Read_ReportsAUserControlThatDoesNotFit(string content, string message)
    {
        var xml = $"""<library format="jacobi.midi.library/1"><userControl name="X" nodeDef="Gx700.patch">{content}</userControl></library>""";

        var exception = Assert.Throws<FileFormatException>(() => ControlLibraryFile.Read(xml, FileFormat.Xml));

        Assert.Contains(message, exception.Message);
    }

    [Theory]
    [InlineData("""<library format="jacobi.midi.rig/1" />""", "is not supported")]
    [InlineData("""<library format="jacobi.midi.library/1"><dashboard /></library>""", "a library cannot contain 'dashboard'")]
    [InlineData("""<library format="jacobi.midi.library/1"><userControl nodeDef="Gx700.patch"><section /></userControl></library>""", "'name' is required")]
    [InlineData("""<library format="jacobi.midi.library/1"><userControl name="X" nodeDef="patch"><section /></userControl></library>""", "is not the full name of a nodeDef")]
    [InlineData("""<library format="jacobi.midi.library/1"><userControl name="X" nodeDef="A.b"><section /></userControl><userControl name="x" nodeDef="A.b"><section /></userControl></library>""", "more than once")]
    public void Read_ReportsWhatDoesNotFit(string xml, string message)
    {
        var exception = Assert.Throws<FileFormatException>(() => ControlLibraryFile.Read(xml, FileFormat.Xml));

        Assert.Contains(message, exception.Message);
    }
}
