using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Parameters;

namespace Jacobi.Midi.Console.Controls;

/// <summary>
/// The tabs of a <see cref="SelectorTabsControl"/> in Play mode: the headers are the selector, the page of the current
/// value of <see cref="Parameter"/> is shown (<see cref="SelectorPages.PageOf"/>); an unknown value, or an option
/// without a page, shows no page and no selected header. A header click writes the option of its page. Without a
/// parameter (Edit mode) it is plain tabs. Unlike a <see cref="TabControl"/>, which always selects a page, it can have
/// none selected; it looks like one.
/// </summary>
internal sealed class SelectorTabsView : TabControl
{
    private bool _isRefreshing;
    private bool _isOnScreen;

    public SelectorTabsView(SelectorTabsControl control)
    {
        Control = control;
        SelectionMode = SelectionMode.Single;
    }

    protected override Type StyleKeyOverride => typeof(TabControl);

    /// <summary>The selector tabs shown: their pages are the items (in order).</summary>
    public SelectorTabsControl Control { get; }

    /// <summary>The value the selected page follows and writes; null for plain tabs (Edit mode, or not bound).</summary>
    public IControlValue? Parameter
    {
        get;
        set
        {
            Subscribe(false);
            field = value;
            Subscribe(_isOnScreen);
            Refresh();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isOnScreen = true;
        Subscribe(true);
        Refresh();
    }

    // tabs that are no longer shown (e.g. after a rebuild) do not follow the value
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isOnScreen = false;
        Subscribe(false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SelectedIndexProperty || _isRefreshing || Parameter is not { } parameter)
            return;
        // a page of no option is never selected: the value decides
        if (SelectedIndex >= 0 && SelectorPages.OptionOf(Control.Children[SelectedIndex], parameter.Info.Options ?? []) is { } option)
            parameter.Set((decimal)option);
        else
            Refresh();
    }

    private void Subscribe(bool onScreen)
    {
        if (Parameter is null)
            return;
        Parameter.Changed -= OnParameterChanged;
        if (onScreen)
            Parameter.Changed += OnParameterChanged;
    }

    // the device state may change on another thread (the exchange)
    private void OnParameterChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            Refresh();
        else
            Dispatcher.UIThread.Post(Refresh);
    }

    // the page of the current value, none when it is not known or its option has no page
    private void Refresh()
    {
        if (Parameter is not { } parameter)
            return;
        _isRefreshing = true;
        try
        {
            var options = parameter.Info.Options ?? [];
            var option = parameter.Number is { } number && number >= 0 && number < options.Count ? options[(int)number] : null;
            SelectedIndex = SelectorPages.PageOf(Control, option) ?? -1;
        }
        finally
        {
            _isRefreshing = false;
        }
    }
}
