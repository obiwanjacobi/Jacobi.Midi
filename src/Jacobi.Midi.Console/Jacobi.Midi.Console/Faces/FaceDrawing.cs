using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Faces;

namespace Jacobi.Midi.Console.Faces;

/// <summary>
/// Drawing code the faces of all kits share: the angles of the knob sweep, arcs, glow, lit segments, display text, the
/// step arrows, the keys of a selector and the plot of an XY pad and an envelope. The controls find their parts with
/// the same geometry (<see cref="ArrowsWidth"/>, <see cref="KeyAt"/>, <see cref="PlotArea"/>).
/// </summary>
internal static class FaceDrawing
{
    /// <summary>The part of the width of a display, at the right, with the ▲/▼ step arrows.</summary>
    public const double ArrowsWidth = 0.3;

    /// <summary>The space around the plot of an XY pad or an envelope, for the points on its edges.</summary>
    public const double PlotInset = 7;

    /// <summary>The space between the keys of a selector.</summary>
    private const double KeyGap = 3;

    /// <summary>A knob's track runs clockwise from bottom-left (135°) to bottom-right (405°); angles from +x, y down.</summary>
    public const double StartAngle = 135;
    public const double SweepAngle = 270;

    public static double AngleOf(double fraction) => StartAngle + SweepAngle * fraction;

    public static Point PointAt(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    public static StreamGeometry Arc(Point center, double radius, double fromAngle, double toAngle)
    {
        var sweep = toAngle - fromAngle;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointAt(center, radius, fromAngle), isFilled: false);
            context.ArcTo(PointAt(center, radius, toAngle), new Size(radius, radius), 0,
                isLargeArc: Math.Abs(sweep) > 180,
                sweep > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
            context.EndFigure(isClosed: false);
        }
        return geometry;
    }

    /// <summary>The arc from the origin to the value; nothing when they are (almost) the same.</summary>
    public static void DrawValueArc(DrawingContext context, IPen pen, Point center, double radius, double origin, double value)
    {
        var from = AngleOf(origin);
        var to = AngleOf(value);
        if (Math.Abs(to - from) >= 0.5)
            context.DrawGeometry(null, pen, Arc(center, radius, from, to));
    }

    /// <summary>The brush with an opacity, for a cheap glow around lit parts; other brushes as they are.</summary>
    public static IBrush? Glow(IBrush? brush, double opacity)
        => brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, opacity) : brush;

    /// <summary>True when <paramref name="fraction"/> lies between the origin and the value (inclusive): a lit segment.</summary>
    public static bool IsLit(double fraction, double origin, double value)
        => fraction >= Math.Min(origin, value) - 0.001 && fraction <= Math.Max(origin, value) + 0.001;

    /// <summary>
    /// The ▲ and ▼ of a number box in the right part of a display (<paramref name="arrowsWidth"/> of its width), with
    /// a line between them and the number. Returns the area left for the number.
    /// </summary>
    public static Rect DrawStepArrows(DrawingContext context, Rect display, double arrowsWidth, IBrush? brush, IPen divider)
    {
        var arrows = new Rect(display.Right - display.Width * arrowsWidth, display.Y, display.Width * arrowsWidth, display.Height);
        context.DrawLine(divider, arrows.TopLeft + new Vector(0, 4), arrows.BottomLeft - new Vector(0, 4));
        var center = arrows.Center;
        var half = Math.Min(arrows.Width, arrows.Height) * 0.18;
        context.DrawGeometry(brush, null, Triangle(new Point(center.X, center.Y - display.Height / 4), half, up: true));
        context.DrawGeometry(brush, null, Triangle(new Point(center.X, center.Y + display.Height / 4), half, up: false));
        return new Rect(display.X, display.Y, display.Width - arrows.Width, display.Height);
    }

    private static StreamGeometry Triangle(Point center, double half, bool up)
    {
        var tip = up ? -half : half;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(center.X, center.Y + tip), isFilled: true);
            context.LineTo(new Point(center.X - half, center.Y - tip));
            context.LineTo(new Point(center.X + half, center.Y - tip));
            context.EndFigure(isClosed: true);
        }
        return geometry;
    }

    /// <summary>The area of a face in which an XY pad or an envelope plots its values.</summary>
    public static Rect PlotArea(Size size) => new Rect(size).Deflate(PlotInset);

    /// <summary>A point of a plot: fractions from the left and from the bottom (0-1) of its area.</summary>
    public static Point PlotPoint(Rect area, double x, double y) => new(area.X + area.Width * x, area.Bottom - area.Height * y);

    /// <summary>The fractions (from the left, from the bottom) of a position in a plot, clamped to 0-1.</summary>
    public static (double X, double Y) PlotFractions(Rect area, Point position)
        => (Math.Clamp((position.X - area.X) / Math.Max(1, area.Width), 0, 1), Math.Clamp((area.Bottom - position.Y) / Math.Max(1, area.Height), 0, 1));

    /// <summary>The curve of an envelope (see <see cref="EnvelopeShape"/>) in a plot area.</summary>
    public static StreamGeometry EnvelopeCurve(Rect area, IReadOnlyList<EnvelopePoint> points, bool closed)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PlotPoint(area, points[0].X, points[0].Y), isFilled: closed);
            foreach (var point in points.Skip(1))
                context.LineTo(PlotPoint(area, point.X, point.Y));
            if (closed)
                context.LineTo(PlotPoint(area, points[^1].X, 0));
            context.EndFigure(isClosed: closed);
        }
        return geometry;
    }

    /// <summary>
    /// The size of a key of a selector shown as buttons, in cells, the gap to the next key included: the same for every
    /// selector, whatever its number of options.
    /// </summary>
    public static readonly Size KeyCells = new(1.75, 1.5);

    // the distance from one key to the next, in pixels (the key and the gap)
    private static Size KeyPitch => new(KeyCells.Width * Face.CellPixels, KeyCells.Height * Face.CellPixels);

    /// <summary>
    /// The number of keys of <paramref name="count"/> in a row across a width; one in an unlimited width, so the keys
    /// never widen their control: they take the width of their plate.
    /// </summary>
    public static int KeyColumns(double width, int count)
        => Double.IsInfinity(width) ? 1 : Math.Clamp((int)Math.Floor((width + KeyGap) / KeyPitch.Width), 1, Math.Max(1, count));

    /// <summary>The size of <paramref name="count"/> keys across a width: as many rows as they need.</summary>
    public static Size KeysSize(int count, double width)
    {
        var columns = KeyColumns(width, count);
        var rows = (Math.Max(1, count) + columns - 1) / columns;
        return new Size(Math.Min(Math.Max(1, count), columns) * KeyPitch.Width - KeyGap, rows * KeyPitch.Height - KeyGap);
    }

    /// <summary>The rectangle of a key of <paramref name="count"/> in the keys' area (see <see cref="KeysSize"/>).</summary>
    public static Rect Key(Size area, int count, int index)
    {
        var columns = KeyColumns(area.Width, count);
        var pitch = KeyPitch;
        return new Rect(index % columns * pitch.Width, index / columns * pitch.Height, pitch.Width - KeyGap, pitch.Height - KeyGap);
    }

    /// <summary>The key of <paramref name="count"/> at a position in the keys' area; null between or beside the keys.</summary>
    public static int? KeyAt(Size area, int count, Point position)
    {
        for (var index = 0; index < count; index++)
        {
            if (Key(area, count, index).Contains(position))
                return index;
        }
        return null;
    }

    /// <summary>Text centred in a rectangle at its size, cut off ("…") when it does not fit its width.</summary>
    public static void DrawTrimmedText(DrawingContext context, string text, Rect area, FontFamily font, IBrush? brush, double size)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(font), size, brush)
        {
            MaxTextWidth = Math.Max(1, area.Width - 4),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        context.DrawText(formatted, new Point(area.X + (area.Width - formatted.Width) / 2, area.Y + (area.Height - formatted.Height) / 2));
    }

    /// <summary>Text centred in a rectangle, in a font of the typography.</summary>
    public static void DrawCenteredText(DrawingContext context, string text, Rect area, FontFamily font, IBrush? brush, double size)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(font), size, brush);
        context.DrawText(formatted, new Point(area.X + (area.Width - formatted.Width) / 2, area.Y + (area.Height - formatted.Height) / 2));
    }
}
