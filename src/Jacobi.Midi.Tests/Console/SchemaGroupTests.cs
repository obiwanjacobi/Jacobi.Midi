using Jacobi.Midi.Console.Core.Schemas;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>Schemas grouped by the first part of their name, as the + of the rig offers them.</summary>
public sealed class SchemaGroupTests
{
    [Fact]
    public void Of_GroupsByTheFirstPartOfTheName_InTheOrderOfTheFirstSchema()
    {
        var groups = SchemaGroup.Of(["Gp8", "Midi.GM", "Gx700", "Midi", "midi.CapabilityInquiry", "Roland.Gs"]);

        Assert.Equal(["Gp8", "Midi", "Gx700", "Roland"], groups.Select(group => group.Name));
        Assert.Equal(["Midi.GM", "Midi", "midi.CapabilityInquiry"], groups[1].Schemas);
        Assert.Equal(["Roland.Gs"], groups[3].Schemas);
    }
}
