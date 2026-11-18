using System.Globalization;
using Jacobi.Midi.Console.Core.Faces;

namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// The field of one option of the selected control's chrome or face in the control panel, built from its declaration
/// (<see cref="FaceOption"/>): a switch or choice as a list with <c>inherited</c> first, a number as a number field
/// that is empty when inherited. It shows the value the control inherits (the look's, or the declared default) and
/// gives a change to the control panel, which makes it an edit of the control (null: inherited again).
/// </summary>
internal sealed class FaceOptionField(FaceOption option, string? value, string inherited, Action<string?> change)
{
    private const string On = "on";
    private const string Off = "off";

    public string Name => option.Name;

    public string Description => option.Description;

    /// <summary>The name with the value it inherits, for the caption.</summary>
    public string Caption => $"{option.Name} (inherited: {Show(inherited)})";

    /// <summary>True for a number: a number field; else a list.</summary>
    public bool IsNumber => option.Type == FaceOptionType.Number;

    public bool IsList => !IsNumber;

    /// <summary>The items of the list: <c>inherited</c>, then the values (on and off for a switch).</summary>
    public IReadOnlyList<string> Items
        => [Inherited, .. option.Type == FaceOptionType.Switch ? [On, Off] : option.Choices];

    /// <summary>The selected item of the list: the control's value or <c>inherited</c>.</summary>
    public string Selected
    {
        get => value is null ? Inherited : Show(value);
        set
        {
            if (value is null)
                return;
            change(value == Inherited ? null : option.Type == FaceOptionType.Switch ? FaceOption.Text(value == On) : value);
        }
    }

    /// <summary>The control's number; null (empty) when it inherits it.</summary>
    public decimal? Number
    {
        get => value is not null && Decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;
        set => change(value is { } number ? FaceOption.Text(Math.Clamp(number, option.Minimum, option.Maximum)) : null);
    }

    public decimal Minimum => option.Minimum;

    public decimal Maximum => option.Maximum;

    private static string Inherited => "inherited";

    private string Show(string text)
        => option.Type == FaceOptionType.Switch ? text == FaceOption.Text(true) ? On : Off : text;
}
