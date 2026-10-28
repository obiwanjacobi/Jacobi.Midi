namespace Jacobi.Midi.Console.Core.Faces;

/// <summary>
/// Where the curve of an envelope is drawn, as fractions of its area (X from the left, Y from the bottom): the faces
/// draw it, the envelope control finds and drags its breakpoints with it. The curve starts at the bottom left; each
/// segment takes an equal share of the width (and the segment that holds one more share, flat, for the time until note
/// off), of which its time fills a part; it ends at its level.
/// </summary>
public static class EnvelopeShape
{
    /// <summary>The part of its share a segment takes when its time is not known.</summary>
    public const double UnknownTime = 0.5;

    /// <summary>
    /// The points of the curve: the start, the breakpoint of each segment (with its number) and, after the segment that
    /// holds, the end of the hold. A level that is not known is the level before it.
    /// </summary>
    public static IReadOnlyList<EnvelopePoint> Points(EnvelopeFaceState state)
    {
        var shares = Shares(state);
        var points = new List<EnvelopePoint> { new(0, 0, null) };
        double x = 0, y = 0;
        for (var segment = 0; segment < state.Breakpoints.Count; segment++)
        {
            var breakpoint = state.Breakpoints[segment];
            x += (breakpoint.Time ?? UnknownTime) / shares;
            y = breakpoint.Level ?? y;
            points.Add(new EnvelopePoint(x, y, segment));
            if (segment == state.HoldSegment)
            {
                x += 1.0 / shares;
                points.Add(new EnvelopePoint(x, y, null));
            }
        }
        return points;
    }

    /// <summary>The time (0-1) of a segment whose breakpoint is at <paramref name="x"/>: the part of its share up to there.</summary>
    public static double TimeAt(EnvelopeFaceState state, int segment, double x)
    {
        var points = Points(state);
        var index = points.ToList().FindIndex(point => point.Segment == segment);
        if (index < 1)
            throw new ArgumentOutOfRangeException(nameof(segment), segment, "The envelope has no such segment.");
        return Math.Clamp((x - points[index - 1].X) * Shares(state), 0, 1);
    }

    // the number of equal shares of the width: one per segment, one more for the hold
    private static int Shares(EnvelopeFaceState state)
        => Math.Max(1, state.Breakpoints.Count + (state.HoldSegment is not null ? 1 : 0));
}

/// <summary>A point of the curve of an envelope (fractions of its area); <paramref name="Segment"/> for a breakpoint.</summary>
public readonly record struct EnvelopePoint(double X, double Y, int? Segment);
