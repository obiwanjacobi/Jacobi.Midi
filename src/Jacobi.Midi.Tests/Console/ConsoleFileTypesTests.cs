using Jacobi.Midi.Console.Core.Files;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>The kind of a file the console opens, told by the end of its name.</summary>
public sealed class ConsoleFileTypesTests
{
    [Theory]
    [InlineData("Gp8.rig.xml", ConsoleFileKind.Rig)]
    [InlineData("Gp8.RIG.JSON", ConsoleFileKind.Rig)]
    [InlineData("Gx700-temp.dashboard.xml", ConsoleFileKind.Dashboard)]
    [InlineData("Acme.schema.json", ConsoleFileKind.Schema)]
    [InlineData("Acme.exchange.xml", ConsoleFileKind.Exchange)]
    [InlineData("Acme.exchange.json", ConsoleFileKind.Exchange)]
    [InlineData("gp8-sysex.syx", ConsoleFileKind.Dump)]
    public void KindOf_TheEndOfTheName_GivesTheKind(string name, ConsoleFileKind kind)
        => Assert.Equal(kind, ConsoleFileTypes.KindOf(name));

    [Theory]
    [InlineData("Acme.xml")]
    [InlineData("Acme.json")]
    [InlineData("Acme.dll")]
    [InlineData("rig.xml.bak")]
    public void KindOf_ANameOutsideTheConvention_IsNull(string name)
        => Assert.Null(ConsoleFileTypes.KindOf(name));

    [Fact]
    public void All_HasAFilterPerTypeOfEveryKind()
    {
        Assert.Equal(Enum.GetValues<ConsoleFileKind>(), ConsoleFileTypes.All.Select(type => type.Kind).Distinct());
        Assert.Equal(["*.exchange.xml", "*.exchange.json"], ConsoleFileTypes.Of(ConsoleFileKind.Exchange).Select(type => type.Pattern));
        Assert.Equal(["Exchange (XML)", "Exchange (JSON)"], ConsoleFileTypes.Of(ConsoleFileKind.Exchange).Select(type => type.Name));
    }
}
