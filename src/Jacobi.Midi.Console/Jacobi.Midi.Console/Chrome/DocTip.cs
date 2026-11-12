using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Jacobi.Midi.Console.Chrome;

/// <summary>
/// The tip that shows the documentation of the schema (<c>midi.doc</c>): a title on one line with the documentation
/// below it, wrapped. The one place this tip is made: attached to any control (<see cref="DocProperty"/>,
/// <see cref="TitleProperty"/>; the control gets a tip only while it has documentation), or made with
/// <see cref="Create"/> (the label of a control, which always has a tip).
/// </summary>
internal sealed class DocTip
{
    private const double DocWidth = 360;

    /// <summary>The documentation shown in the tip of the control; no tip when it is empty.</summary>
    public static readonly AttachedProperty<string?> DocProperty =
        AvaloniaProperty.RegisterAttached<DocTip, Control, string?>("Doc");

    /// <summary>The title above the documentation in the tip (e.g. the name of the section or the node).</summary>
    public static readonly AttachedProperty<string?> TitleProperty =
        AvaloniaProperty.RegisterAttached<DocTip, Control, string?>("Title");

    static DocTip()
    {
        DocProperty.Changed.AddClassHandler<Control>((control, _) => Update(control));
        TitleProperty.Changed.AddClassHandler<Control>((control, _) => Update(control));
    }

    private DocTip()
    {
    }

    public static string? GetDoc(Control control) => control.GetValue(DocProperty);

    public static void SetDoc(Control control, string? value) => control.SetValue(DocProperty, value);

    public static string? GetTitle(Control control) => control.GetValue(TitleProperty);

    public static void SetTitle(Control control, string? value) => control.SetValue(TitleProperty, value);

    /// <summary>
    /// The content of a tip: the title (on one line), with the documentation below it when there is any; null when
    /// there is neither.
    /// </summary>
    /// <remarks>
    /// The title on one line: a plain text tip wraps, and with display scaling (e.g. 250%) its width can be rounded a
    /// fraction below the text's width, which wraps the last letter onto a line the tip has no room for. The
    /// documentation wraps, at the width it was measured with (<see cref="DocText"/>).
    /// </remarks>
    public static Control? Create(string? title, string? doc)
    {
        var heading = String.IsNullOrEmpty(title) ? null : new TextBlock { Text = title, TextWrapping = TextWrapping.NoWrap };
        if (String.IsNullOrWhiteSpace(doc))
            return heading;

        var text = new DocText { Text = doc, TextWrapping = TextWrapping.Wrap, MaxWidth = DocWidth };
        if (heading is null)
            return text;
        heading.FontWeight = FontWeight.SemiBold;
        heading.Margin = new Thickness(0, 0, 0, 4);
        return new StackPanel { Children = { heading, text } };
    }

    private static void Update(Control control)
    {
        var doc = GetDoc(control);
        ToolTip.SetTip(control, String.IsNullOrWhiteSpace(doc) ? null : Create(GetTitle(control), doc));
    }

    /// <summary>
    /// The documentation text of the tip: arranged at no less than the width it was measured with, so that a width
    /// rounded a fraction smaller (display scaling) does not wrap its lines again into more lines than it has room for.
    /// </summary>
    private sealed class DocText : TextBlock
    {
        protected override Type StyleKeyOverride => typeof(TextBlock);

        protected override Size ArrangeOverride(Size finalSize)
            => base.ArrangeOverride(new Size(Math.Max(finalSize.Width, DesiredSize.Width), finalSize.Height));
    }
}
