using System.Globalization;
using Avalonia;
using Jacobi.Midi.Console.Core.Faces;

namespace Jacobi.Midi.Console.Controls;

/// <summary>The text: shows a string parameter (e.g. a patch name) in its face; a double-click edits it.</summary>
public sealed class TextControl : ParameterControlBase
{
    /// <summary>The text; null when it is not known.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<TextControl, string?>(nameof(Text));

    public TextControl()
    {
        UpdateFace();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void Refresh()
        => Text = Parameter?.Value is { } value ? Convert.ToString(value, CultureInfo.CurrentCulture) : null;

    protected override string? EditText => Text ?? String.Empty;

    protected override bool TryApplyEdit(string text)
    {
        Text = text;
        return true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TextProperty)
            return;
        UpdateFace();
        if (Text is { } text)
            Write(text);
    }

    // the face shows the text: the chrome has no value text
    private void UpdateFace() => FaceState = new TextFaceState(Text);
}
