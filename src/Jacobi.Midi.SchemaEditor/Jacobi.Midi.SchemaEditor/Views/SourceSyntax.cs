using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using Jacobi.Midi.SchemaEditor.Services;

namespace Jacobi.Midi.SchemaEditor.Views;

/// <summary>
/// The syntax support of the schema source viewer per <see cref="SchemaFormat"/>: the built-in AvaloniaEdit highlighting
/// definition with its colors taken from the theme (the <c>AppSyntax…Brush</c> resources in Themes/Palette.axaml),
/// and the folding strategy.
/// </summary>
internal static class SourceSyntax
{
    // per built-in highlighting definition: its named colors, mapped to the syntax brush of the palette
    private static readonly Dictionary<string, Dictionary<string, string>> ColorKeys = new()
    {
        ["XML"] = new()
        {
            ["Comment"] = "AppSyntaxCommentBrush",
            ["CData"] = "AppSyntaxStringBrush",
            ["DocType"] = "AppSyntaxKeywordBrush",
            ["XmlDeclaration"] = "AppSyntaxKeywordBrush",
            ["XmlTag"] = "AppSyntaxTagBrush",
            ["AttributeName"] = "AppSyntaxNameBrush",
            ["AttributeValue"] = "AppSyntaxStringBrush",
            ["Entity"] = "AppSyntaxKeywordBrush",
            ["BrokenEntity"] = "AppSyntaxKeywordBrush",
        },
        ["Json"] = new()
        {
            ["Bool"] = "AppSyntaxKeywordBrush",
            ["Null"] = "AppSyntaxKeywordBrush",
            ["Number"] = "AppSyntaxNumberBrush",
            ["String"] = "AppSyntaxStringBrush",
            ["FieldName"] = "AppSyntaxNameBrush",
            ["Punctuation"] = "AppSyntaxPunctuationBrush",
        },
    };

    /// <summary>
    /// The highlighting definition of the format, colored with the syntax brushes of the theme of the control.
    /// The built-in definitions are shared instances; only this viewer uses them.
    /// </summary>
    public static IHighlightingDefinition? GetHighlighting(SchemaFormat format, Control themed)
    {
        var name = format switch
        {
            SchemaFormat.Xml => "XML",
            SchemaFormat.Json => "Json",
            _ => null
        };
        if (name is null || HighlightingManager.Instance.GetDefinition(name) is not { } definition)
        {
            return null;
        }

        foreach (var (colorName, key) in ColorKeys[name])
        {
            if (definition.GetNamedColor(colorName) is { } color
                && themed.TryFindResource(key, themed.ActualThemeVariant, out var resource)
                && resource is ISolidColorBrush brush)
            {
                color.Foreground = new SimpleHighlightingBrush(brush.Color);
            }
        }
        return definition;
    }

    /// <summary>
    /// Updates the foldings of the document in the format.
    /// </summary>
    public static void UpdateFoldings(SchemaFormat format, FoldingManager manager, TextDocument document)
    {
        switch (format)
        {
            case SchemaFormat.Xml:
                new XmlFoldingStrategy().UpdateFoldings(manager, document);
                break;
            case SchemaFormat.Json:
                new JsonFoldingStrategy().UpdateFoldings(manager, document);
                break;
            default:
                manager.Clear();
                break;
        }
    }
}
