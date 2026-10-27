using Jacobi.Midi.Console.Core.Faces;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// The conversion of logical values into the fractions of a face state, and the origin a value is drawn from.
/// </summary>
public class ValueRangeTests
{
    [Theory]
    [InlineData(0, 100, 0)]        // unipolar: from the minimum
    [InlineData(-50, 50, 0.5)]     // bipolar: from the centre
    [InlineData(-24, 24, 0.5)]
    [InlineData(-50, 0, 1)]        // only negative: from the maximum
    [InlineData(435, 445, 0)]      // zero below the range: from the minimum
    public void Origin_IsZeroWithinTheRange(int minimum, int maximum, double origin)
    {
        var range = new ValueRange(minimum, maximum);

        Assert.Equal(origin, range.Origin, 6);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(64, 64.0 / 127)]
    [InlineData(127, 1)]
    [InlineData(200, 1)]           // clamped
    [InlineData(-5, 0)]            // clamped
    public void ToFraction_IsTheValueAsFractionOfTheRange(int value, double fraction)
    {
        var range = new ValueRange(0, 127);

        Assert.Equal(fraction, range.ToFraction(value), 6);
    }

    [Fact]
    public void ToFaceState_OfAnUnknownValue_HasNoFraction()
    {
        var state = new ValueRange(-50, 50).ToFaceState(null);

        Assert.Null(state.Fraction);
        Assert.Equal(0.5, state.Origin, 6);
    }

    [Fact]
    public void ToFaceState_OfAKnownValue_HasItsFractionAndTheOrigin()
    {
        var state = new ValueRange(-50, 50).ToFaceState(25);

        Assert.Equal(0.75, state.Fraction!.Value, 6);
        Assert.Equal(0.5, state.Origin, 6);
    }

    [Fact]
    public void IsBipolar_OnlyForARangeWithNegativeAndPositiveValues()
    {
        Assert.True(new ValueRange(-50, 50).IsBipolar);
        Assert.False(new ValueRange(0, 100).IsBipolar);
        Assert.False(new ValueRange(-50, 0).IsBipolar);
    }

    [Fact]
    public void AnInvertedRange_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ValueRange(10, 0));
    }
}
