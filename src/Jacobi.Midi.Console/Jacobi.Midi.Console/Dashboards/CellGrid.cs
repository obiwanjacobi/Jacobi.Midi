using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Jacobi.Midi.Console.Dashboards;

/// <summary>
/// The cells of a grid, drawn over it in Edit mode: a line between every cell, a stronger one every five, and the
/// column and row numbers (zero based, as the positions are) every five cells along the top and left edge.
/// </summary>
internal sealed class CellGrid : Control
{
    private const int Major = 5;

    private static readonly IPen MinorPen = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0x4F, 0xA3, 0xFF)), 1);
    private static readonly IPen MajorPen = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0x4F, 0xA3, 0xFF)), 1);
    private static readonly IBrush NumberBrush = new SolidColorBrush(Color.FromArgb(0xC0, 0x8F, 0xC6, 0xFF));

    public double CellSize { get; init; } = 24;

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        var columns = (int)Math.Ceiling(size.Width / CellSize);
        var rows = (int)Math.Ceiling(size.Height / CellSize);
        for (var column = 0; column <= columns; column++)
        {
            var x = Math.Min(column * CellSize, size.Width);
            context.DrawLine(column % Major == 0 ? MajorPen : MinorPen, new Point(x, 0), new Point(x, size.Height));
            if (column % Major == 0 && column < columns)
                DrawNumber(context, column, new Point(x + 2, 1));
        }
        for (var row = 0; row <= rows; row++)
        {
            var y = Math.Min(row * CellSize, size.Height);
            context.DrawLine(row % Major == 0 ? MajorPen : MinorPen, new Point(0, y), new Point(size.Width, y));
            if (row % Major == 0 && row > 0 && row < rows)
                DrawNumber(context, row, new Point(2, y + 1));
        }
    }

    private static void DrawNumber(DrawingContext context, int number, Point at)
    {
        var text = new FormattedText(number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 9, NumberBrush);
        context.DrawText(text, at);
    }
}
