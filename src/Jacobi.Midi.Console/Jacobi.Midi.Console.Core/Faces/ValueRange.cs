namespace Jacobi.Midi.Console.Core.Faces;

/// <summary>
/// The range of a logical value (the data type's <c>min</c> and <c>max</c>, after <c>scale</c> and <c>shift</c>):
/// converts values into fractions for the face state. The origin, where a value is drawn from, is zero within the
/// range: the minimum for 0-100, the centre for -50..+50, the maximum for -50..0.
/// </summary>
public readonly record struct ValueRange
{
    public ValueRange(decimal minimum, decimal maximum)
    {
        if (maximum < minimum)
            throw new ArgumentException($"The maximum ({maximum}) is smaller than the minimum ({minimum}).", nameof(maximum));
        Minimum = minimum;
        Maximum = maximum;
    }

    public decimal Minimum { get; }
    public decimal Maximum { get; }

    /// <summary>True when the range holds negative and positive values.</summary>
    public bool IsBipolar => Minimum < 0 && Maximum > 0;

    /// <summary>The value, clamped to the range.</summary>
    public decimal Clamp(decimal value) => Math.Clamp(value, Minimum, Maximum);

    /// <summary>The value as a fraction of the range (0-1), clamped to the range.</summary>
    public double ToFraction(decimal value)
        => Maximum == Minimum ? 0 : (double)((Clamp(value) - Minimum) / (Maximum - Minimum));

    /// <summary>Where a value is drawn from: zero within the range, as a fraction.</summary>
    public double Origin => ToFraction(0);

    /// <summary>The face state of a value in this range; null for an unknown value.</summary>
    public RangeFaceState ToFaceState(decimal? value)
        => new(value is { } known ? ToFraction(known) : null, Origin);
}
