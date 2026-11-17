using Xunit;

namespace Jacobi.Midi.Tests;

/// <summary>
/// The tests that measure time: they run alone, after the tests that run in parallel, so a busy machine does not
/// stretch or squeeze the times they check. Put a test class in it with <c>[Collection(TimingCollection.Name)]</c>.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing";
}
