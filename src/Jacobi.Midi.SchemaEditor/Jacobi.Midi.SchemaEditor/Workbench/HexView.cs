using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Jacobi.Midi.SchemaEditor.Workbench;

/// <summary>
/// The byte view of the workbench: draws the messages of the baseline as a hex grid (16 bytes per row, an offset and an ASCII column),
/// one block per message slot, with the comparison drawn on top of it: the difference tint, the changed bits of a byte,
/// hatching for bytes that are probably derived (checksums), annotation marks and the selection.
/// The bytes are selected with the mouse (click, drag, shift+click) or the keyboard (arrows, shift+arrows, Tab to the next difference).
/// </summary>
/// <remarks>
/// The control draws itself (instead of a control per byte) so a dump of several kilobytes stays fast;
/// only the rows in the visible part of the scroll viewer are drawn.
/// </remarks>
internal sealed class HexView : Control
{
    public static readonly StyledProperty<IReadOnlyList<SlotComparison>?> SlotsProperty =
        AvaloniaProperty.Register<HexView, IReadOnlyList<SlotComparison>?>(nameof(Slots));

    public static readonly StyledProperty<IReadOnlyList<Annotation>?> AnnotationsProperty =
        AvaloniaProperty.Register<HexView, IReadOnlyList<Annotation>?>(nameof(Annotations));

    public static readonly StyledProperty<IReadOnlyList<MessageLayout?>?> LayoutsProperty =
        AvaloniaProperty.Register<HexView, IReadOnlyList<MessageLayout?>?>(nameof(Layouts));

    public static readonly StyledProperty<IReadOnlyList<ByteRange>?> MatchesProperty =
        AvaloniaProperty.Register<HexView, IReadOnlyList<ByteRange>?>(nameof(Matches));

    public static readonly StyledProperty<IReadOnlyDictionary<int, string>?> SlotNotesProperty =
        AvaloniaProperty.Register<HexView, IReadOnlyDictionary<int, string>?>(nameof(SlotNotes));

    public static readonly StyledProperty<bool> ShowDecimalProperty =
        AvaloniaProperty.Register<HexView, bool>(nameof(ShowDecimal));

    public static readonly StyledProperty<ByteRange?> SelectionProperty =
        AvaloniaProperty.Register<HexView, ByteRange?>(nameof(Selection), defaultBindingMode: BindingMode.TwoWay);

    private const int BytesPerRow = 16;
    private const double Pad = 8;
    private const double OffsetWidth = 44;
    private const double CellWidth = 27;
    private const double GroupGap = 8;
    private const double RowHeight = 32;
    private const double HeaderHeight = 30;
    private const double SlotGap = 14;
    private const double AsciiGap = 18;
    private const double AsciiWidth = 9;
    private const double HexFontSize = 13;
    private const double SmallFontSize = 11;

    private static readonly Color DifferenceColor = Color.FromRgb(0xE3, 0x8B, 0x3C);
    private static readonly Typeface MonoTypeface = new(new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"));
    private static readonly Typeface HeaderTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    // the top of each slot block, plus the total height as last entry
    private double[] _slotTops = [0];
    private Rect _viewport;

    // selection by mouse and keyboard: the anchor stays, the caret moves
    private int _anchor;
    private int _caret;
    private bool _dragging;
    private bool _updatingSelection;
    private (int Slot, int Offset)? _hovered;

    // drawing resources, recreated when the theme brushes change
    private IBrush? _textBrush;
    private readonly Dictionary<(string Text, IBrush Brush, double Size), FormattedText> _texts = [];
    private readonly IBrush[] _differenceBrushes = Enumerable.Range(0, 11)
        .Select(step => (IBrush)new SolidColorBrush(DifferenceColor, 0.15 + 0.06 * step)).ToArray();

    static HexView()
    {
        AffectsMeasure<HexView>(SlotsProperty);
        AffectsRender<HexView>(SlotsProperty, AnnotationsProperty, LayoutsProperty, SelectionProperty,
            MatchesProperty, SlotNotesProperty, ShowDecimalProperty);
        FocusableProperty.OverrideDefaultValue<HexView>(true);
    }

    public HexView()
    {
        EffectiveViewportChanged += (_, e) =>
        {
            _viewport = e.EffectiveViewport;
            InvalidateVisual();
        };
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The message slots of the baseline with their comparison state.</summary>
    public IReadOnlyList<SlotComparison>? Slots
    {
        get => GetValue(SlotsProperty);
        set => SetValue(SlotsProperty, value);
    }

    /// <summary>The annotations to mark.</summary>
    public IReadOnlyList<Annotation>? Annotations
    {
        get => GetValue(AnnotationsProperty);
        set => SetValue(AnnotationsProperty, value);
    }

    /// <summary>The layout of the schema root of each slot (null for a slot without one): the fields are marked under their bytes.</summary>
    public IReadOnlyList<MessageLayout?>? Layouts
    {
        get => GetValue(LayoutsProperty);
        set => SetValue(LayoutsProperty, value);
    }

    /// <summary>The bytes where a searched value was found: outlined.</summary>
    public IReadOnlyList<ByteRange>? Matches
    {
        get => GetValue(MatchesProperty);
        set => SetValue(MatchesProperty, value);
    }

    /// <summary>The notes of the user on the slots (by slot index), shown in the slot headers.</summary>
    public IReadOnlyDictionary<int, string>? SlotNotes
    {
        get => GetValue(SlotNotesProperty);
        set => SetValue(SlotNotesProperty, value);
    }

    /// <summary>True to show the bytes and offsets in decimal; false for hex.</summary>
    public bool ShowDecimal
    {
        get => GetValue(ShowDecimalProperty);
        set => SetValue(ShowDecimalProperty, value);
    }

    /// <summary>The selected bytes (always within one slot); null when nothing is selected.</summary>
    public ByteRange? Selection
    {
        get => GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    /// <summary>Provides the tooltip of a byte (slot index, offset); null for none.</summary>
    public Func<int, int, string?>? DescribeByte { get; set; }

    private static double CellX(int column) => Pad + OffsetWidth + column * CellWidth + (column >= BytesPerRow / 2 ? GroupGap : 0);
    private static double AsciiX(int column) => CellX(BytesPerRow) + AsciiGap + column * AsciiWidth;
    private static int RowCount(SlotComparison slot) => Math.Max(1, (slot.Bytes.Length + BytesPerRow - 1) / BytesPerRow);

    protected override Size MeasureOverride(Size availableSize)
    {
        var slots = Slots ?? [];
        _slotTops = new double[slots.Count + 1];
        var top = Pad;
        for (var index = 0; index < slots.Count; index++)
        {
            _slotTops[index] = top;
            top += HeaderHeight + RowCount(slots[index]) * RowHeight + SlotGap;
        }
        _slotTops[slots.Count] = top;
        return new Size(AsciiX(BytesPerRow) + Pad, top + Pad);
    }

    private Rect CellRect(int slot, int offset)
    {
        var y = _slotTops[slot] + HeaderHeight + offset / BytesPerRow * RowHeight;
        return new Rect(CellX(offset % BytesPerRow), y, CellWidth - 2, RowHeight - 3);
    }

    private Rect AsciiRect(int slot, int offset)
    {
        var y = _slotTops[slot] + HeaderHeight + offset / BytesPerRow * RowHeight;
        return new Rect(AsciiX(offset % BytesPerRow) - 1, y + 2, AsciiWidth + 1, RowHeight - 9);
    }

    // ---- drawing ----

    public override void Render(DrawingContext context)
    {
        // a background, so the whole area takes pointer input
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Slots is not { Count: > 0 } slots || _slotTops.Length != slots.Count + 1)
        {
            return;
        }

        var text = Resource("AppTextHeadingBrush", Brushes.White);
        if (!ReferenceEquals(text, _textBrush))
        {
            // the theme changed
            _texts.Clear();
            _textBrush = text;
        }
        var muted = Resource("AppTextMutedBrush", Brushes.Gray);
        var accent = Resource("AppTextAccentBrush", Brushes.Teal);
        var divider = Resource("AppDividerBrush", Brushes.DimGray);
        var card = Resource("AppBackgroundCardBrush", Brushes.Black);
        var hatch = new Pen(muted, 1);
        var selectionPen = new Pen(accent, 1.5);
        var selectionFill = new SolidColorBrush(Colors.White, 0.08);

        var visible = _viewport.Height > 0 ? _viewport : new Rect(Bounds.Size);
        var annotations = Annotations ?? [];
        var selection = Selection;

        for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            var top = _slotTops[slotIndex];
            var bottom = _slotTops[slotIndex + 1];
            if (bottom < visible.Top || top > visible.Bottom)
            {
                continue;
            }

            var slot = slots[slotIndex];
            var layout = Layouts?.ElementAtOrDefault(slotIndex);
            // the slot header with a line to separate it from the previous slot
            context.FillRectangle(card, new Rect(Pad, top, AsciiX(BytesPerRow) - Pad, HeaderHeight - 6), 4);
            var header = layout is null
                ? slot.Header
                : $"{slot.Header} · {layout.Root.Name}: {layout.DescribedCount} of {layout.Spans.Sum(span => span.Length)} bytes described";
            if (SlotNotes?.GetValueOrDefault(slotIndex) is { Length: > 0 } note)
            {
                header += $" — {note}";
            }
            DrawText(context, header, slot.HasDifference ? accent : muted, SmallFontSize, new Point(Pad + 8, top + 5), HeaderTypeface);
            if (layout is not null)
            {
                DrawCoverage(context, slotIndex, layout, accent);
            }

            var rows = RowCount(slot);
            for (var row = 0; row < rows; row++)
            {
                var y = top + HeaderHeight + row * RowHeight;
                if (y + RowHeight < visible.Top || y > visible.Bottom)
                {
                    continue;
                }

                DrawText(context, (row * BytesPerRow).ToString(ShowDecimal ? "D4" : "X4", CultureInfo.InvariantCulture), muted, SmallFontSize, new Point(Pad, y + 6));
                for (var column = 0; column < BytesPerRow; column++)
                {
                    var offset = row * BytesPerRow + column;
                    if (offset >= slot.Bytes.Length)
                    {
                        break;
                    }
                    DrawByte(context, slot, slotIndex, offset, text, muted, accent, hatch);
                    var value = slot.Bytes[offset];
                    var printable = value is >= 0x20 and < 0x7F;
                    DrawText(context, printable ? ((char)value).ToString() : "·", printable ? text : divider, HexFontSize,
                        new Point(AsciiX(column), y + 4));
                }
            }

            foreach (var annotation in annotations.Where(annotation => annotation.Range.Slot == slotIndex))
            {
                for (var offset = annotation.Range.Start; offset <= annotation.Range.End && offset < slot.Bytes.Length; offset++)
                {
                    var cell = CellRect(slotIndex, offset);
                    var corner = new StreamGeometry();
                    using (var geometry = corner.Open())
                    {
                        geometry.BeginFigure(cell.TopRight, true);
                        geometry.LineTo(cell.TopRight + new Point(-6, 0));
                        geometry.LineTo(cell.TopRight + new Point(0, 6));
                        geometry.EndFigure(true);
                    }
                    context.DrawGeometry(accent, null, corner);
                }
            }

            // the bytes where the searched value was found: a dashed outline
            foreach (var match in (Matches ?? []).Where(match => match.Slot == slotIndex))
            {
                for (var offset = match.Start; offset <= match.End && offset < slot.Bytes.Length; offset++)
                {
                    context.DrawRectangle(null, MatchPen, CellRect(slotIndex, offset).Deflate(1), 3, 3);
                }
            }

            if (selection is { } range && range.Slot == slotIndex)
            {
                for (var offset = range.Start; offset <= range.End && offset < slot.Bytes.Length; offset++)
                {
                    context.DrawRectangle(selectionFill, selectionPen, CellRect(slotIndex, offset), 3, 3);
                }
                // the same bytes in the ASCII column: one rectangle per row
                for (var row = range.Start / BytesPerRow; row <= Math.Min(range.End, slot.Bytes.Length - 1) / BytesPerRow; row++)
                {
                    var first = Math.Max(range.Start, row * BytesPerRow);
                    var last = Math.Min(Math.Min(range.End, slot.Bytes.Length - 1), row * BytesPerRow + BytesPerRow - 1);
                    var left = AsciiRect(slotIndex, first);
                    context.DrawRectangle(selectionFill, selectionPen, left.Union(AsciiRect(slotIndex, last)), 2, 2);
                }
            }
        }
    }

    // a bar under the bytes of each field (alternating colours, so neighbouring fields can be told apart); unknown spans have none
    private void DrawCoverage(DrawingContext context, int slotIndex, MessageLayout layout, IBrush accent)
    {
        var fieldIndex = 0;
        foreach (var span in layout.Spans)
        {
            if (span.IsPlaceholder)
            {
                continue;
            }
            var brush = fieldIndex++ % 2 == 0 ? accent : FieldBrush;
            for (var offset = span.Start; offset <= span.End; offset++)
            {
                var cell = CellRect(slotIndex, offset);
                // the bar runs on to the next byte of the same field
                var width = offset < span.End && (offset + 1) % BytesPerRow != 0 ? CellRect(slotIndex, offset + 1).Left - cell.Left : cell.Width;
                context.FillRectangle(brush, new Rect(cell.Left, cell.Bottom + 0.5, width, 2));
            }
        }
    }

    private static readonly IBrush FieldBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0xDC));
    private static readonly Pen MatchPen = new(new SolidColorBrush(Color.FromRgb(0xF2, 0xD5, 0x4B)), 1.5, new DashStyle([2, 1.5], 0));

    private void DrawByte(DrawingContext context, SlotComparison slot, int slotIndex, int offset, IBrush text, IBrush muted, IBrush accent, Pen hatch)
    {
        var cell = CellRect(slotIndex, offset);
        var state = slot.States[offset];
        if (state.IsDifferent && state.IsDerived)
        {
            // probably a checksum: hatched, so it does not draw attention
            using (context.PushClip(cell))
            {
                for (var x = cell.Left - cell.Height; x < cell.Right; x += 5)
                {
                    context.DrawLine(hatch, new Point(x, cell.Bottom), new Point(x + cell.Height, cell.Top));
                }
            }
        }
        else if (state.Tint > 0)
        {
            context.FillRectangle(_differenceBrushes[(int)Math.Round(state.Tint * 10)], cell, 3);
        }

        // F0 and F7 are framing: dimmed
        var value = slot.Bytes[offset];
        var framing = (offset == 0 && value == 0xF0) || (offset == slot.Bytes.Length - 1 && value == 0xF7);
        // centred: a decimal value has up to 3 digits
        var digits = GetText(value.ToString(ShowDecimal ? "D" : "X2", CultureInfo.InvariantCulture), framing ? muted : text,
            ShowDecimal && value > 99 ? SmallFontSize : HexFontSize);
        context.DrawText(digits, new Point(cell.Left + (cell.Width - digits.Width) / 2, cell.Top + 3));

        if (state.ChangedBits != 0)
        {
            // one dot per bit, most significant left; 8 dots only when the 8th bit changed
            var bits = state.ChangedBits > 0x7F ? 8 : 7;
            var dotsLeft = cell.Left + (cell.Width - bits * 3) / 2;
            for (var bit = 0; bit < bits; bit++)
            {
                var set = (state.ChangedBits & (1 << (bits - 1 - bit))) != 0;
                var dot = new Rect(dotsLeft + bit * 3, cell.Bottom - 6, 2, set ? 3 : 1);
                context.FillRectangle(set ? accent : muted, dot);
            }
        }
    }

    private void DrawText(DrawingContext context, string value, IBrush brush, double size, Point origin, Typeface? typeface = null)
    {
        if (typeface is not null)
        {
            context.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface.Value, size, brush), origin);
            return;
        }
        context.DrawText(GetText(value, brush, size), origin);
    }

    // the (cached) monospace text
    private FormattedText GetText(string value, IBrush brush, double size)
    {
        if (!_texts.TryGetValue((value, brush, size), out var formatted))
        {
            formatted = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MonoTypeface, size, brush);
            _texts[(value, brush, size)] = formatted;
        }
        return formatted;
    }

    private IBrush Resource(string key, IBrush fallback)
        => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;

    // ---- input ----

    private (int Slot, int Offset)? HitTest(Point point)
    {
        if (Slots is not { Count: > 0 } slots || _slotTops.Length != slots.Count + 1)
        {
            return null;
        }
        var slot = Array.BinarySearch(_slotTops, point.Y);
        slot = slot >= 0 ? slot : ~slot - 1;
        if (slot < 0 || slot >= slots.Count)
        {
            return null;
        }

        var row = (int)Math.Floor((point.Y - _slotTops[slot] - HeaderHeight) / RowHeight);
        if (row < 0 || row >= RowCount(slots[slot]))
        {
            return null;
        }
        // a byte is hit in the hex grid or in the ASCII column
        for (var column = 0; column < BytesPerRow; column++)
        {
            if ((point.X >= CellX(column) && point.X < CellX(column) + CellWidth)
                || (point.X >= AsciiX(column) && point.X < AsciiX(column) + AsciiWidth))
            {
                var offset = row * BytesPerRow + column;
                return offset < slots[slot].Bytes.Length ? (slot, offset) : null;
            }
        }
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || HitTest(e.GetPosition(this)) is not { } hit)
        {
            return;
        }
        if (e.ClickCount == 2 && Layouts?.ElementAtOrDefault(hit.Slot)?.Find(hit.Offset) is { } span)
        {
            // a double click selects the whole field or unknown span
            Select(hit.Slot, span.Start, extend: false);
            Select(hit.Slot, span.End, extend: true);
            e.Handled = true;
            return;
        }
        var extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift) && Selection?.Slot == hit.Slot;
        Select(hit.Slot, hit.Offset, extend);
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTest(e.GetPosition(this));
        if (_dragging && hit is { } drag && drag.Slot == Selection?.Slot)
        {
            Select(drag.Slot, drag.Offset, extend: true);
        }
        if (hit != _hovered)
        {
            _hovered = hit;
            ToolTip.SetTip(this, hit is { } byteHit ? DescribeByte?.Invoke(byteHit.Slot, byteHit.Offset) : null);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = null;
        ToolTip.SetTip(this, null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Slots is not { Count: > 0 } slots || Selection is not { } selection || selection.Slot >= slots.Count)
        {
            return;
        }

        var extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var length = slots[selection.Slot].Bytes.Length;
        int? caret = e.Key switch
        {
            Key.Left => _caret - 1,
            Key.Right => _caret + 1,
            Key.Up => _caret - BytesPerRow,
            Key.Down => _caret + BytesPerRow,
            Key.Home => _caret - _caret % BytesPerRow,
            Key.End => Math.Min(length - 1, _caret - _caret % BytesPerRow + BytesPerRow - 1),
            _ => null
        };
        if (caret is { } moved)
        {
            Select(selection.Slot, Math.Clamp(moved, 0, length - 1), extend);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && FindDifference(slots, selection.Slot, _caret, forward: !extend) is { } next)
        {
            Select(next.Slot, next.Offset, extend: false);
            e.Handled = true;
        }
    }

    // the next (or previous) byte that differs and is not derived, across the slots
    private static (int Slot, int Offset)? FindDifference(IReadOnlyList<SlotComparison> slots, int slot, int offset, bool forward)
    {
        var step = forward ? 1 : -1;
        offset += step;
        while (slot >= 0 && slot < slots.Count)
        {
            var states = slots[slot].States;
            for (; offset >= 0 && offset < states.Length; offset += step)
            {
                if (states[offset] is { IsDifferent: true, IsDerived: false })
                {
                    return (slot, offset);
                }
            }
            slot += step;
            offset = forward || slot < 0 ? 0 : slots[slot].States.Length - 1;
        }
        return null;
    }

    private void Select(int slot, int offset, bool extend)
    {
        if (!extend)
        {
            _anchor = offset;
        }
        _caret = offset;
        var start = Math.Min(_anchor, _caret);
        _updatingSelection = true;
        try
        {
            Selection = new ByteRange(slot, start, Math.Abs(_caret - _anchor) + 1);
        }
        finally
        {
            _updatingSelection = false;
        }
        this.BringIntoView(CellRect(slot, _caret).Inflate(new Thickness(0, HeaderHeight, 0, RowHeight)));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectionProperty && !_updatingSelection && Selection is { } range)
        {
            // selected from outside (e.g. the slots list)
            _anchor = range.Start;
            _caret = range.End;
            if (range.Slot < _slotTops.Length - 1)
            {
                this.BringIntoView(CellRect(range.Slot, range.Start).Inflate(new Thickness(0, HeaderHeight, 0, RowHeight)));
            }
        }
        else if (change.Property == SlotsProperty)
        {
            _hovered = null;
        }
    }
}
