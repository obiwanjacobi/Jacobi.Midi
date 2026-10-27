using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace Jacobi.Midi.Console.Faces;

/// <summary>
/// Drawing code the faces of all kits share: the angles of the knob sweep, arcs, glow, lit segments and display text.
/// </summary>
internal static class FaceDrawing
{
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

    /// <summary>Text centred in a rectangle, in a font of the typography.</summary>
    public static void DrawCenteredText(DrawingContext context, string text, Rect area, FontFamily font, IBrush? brush, double size)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(font), size, brush);
        context.DrawText(formatted, new Point(area.X + (area.Width - formatted.Width) / 2, area.Y + (area.Height - formatted.Height) / 2));
    }
}
