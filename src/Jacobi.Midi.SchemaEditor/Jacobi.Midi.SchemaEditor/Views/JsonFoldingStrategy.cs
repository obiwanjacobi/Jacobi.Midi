using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace Jacobi.Midi.SchemaEditor.Views;

/// <summary>
/// Creates the foldings of a JSON text for a <see cref="FoldingManager"/>: one per object (<c>{}</c>) or array (<c>[]</c>)
/// that spans more than one line. Brackets inside strings are skipped.
/// AvaloniaEdit only has an <see cref="XmlFoldingStrategy"/>; this one has the same shape.
/// </summary>
internal sealed class JsonFoldingStrategy
{
    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        var foldings = CreateNewFoldings(document, out var firstErrorOffset);
        manager.UpdateFoldings(foldings, firstErrorOffset);
    }

    /// <summary>
    /// Creates the foldings of the document, ordered by start offset.
    /// </summary>
    /// <param name="firstErrorOffset">The offset of the first unbalanced closing bracket; -1 when there is none.</param>
    public IEnumerable<NewFolding> CreateNewFoldings(TextDocument document, out int firstErrorOffset)
    {
        firstErrorOffset = -1;
        var foldings = new List<NewFolding>();
        var open = new Stack<int>();
        var inString = false;
        for (var offset = 0; offset < document.TextLength; offset++)
        {
            var c = document.GetCharAt(offset);
            if (inString)
            {
                if (c == '\\')
                {
                    offset++;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{' or '[':
                    open.Push(offset);
                    break;
                case '}' or ']' when open.Count == 0:
                    if (firstErrorOffset < 0)
                    {
                        firstErrorOffset = offset;
                    }
                    break;
                case '}' or ']':
                    var start = open.Pop();
                    if (document.GetLineByOffset(start).LineNumber != document.GetLineByOffset(offset).LineNumber)
                    {
                        foldings.Add(new NewFolding(start, offset + 1));
                    }
                    break;
            }
        }
        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }
}
