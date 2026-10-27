using System.Globalization;
using Avalonia.Data.Converters;

namespace Jacobi.Midi.Console.Chrome;

/// <summary>Shows a text in capitals, for chrome templates with engraved or printed labels.</summary>
public sealed class UpperCaseConverter : IValueConverter
{
    public static UpperCaseConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string)?.ToUpper(culture);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
