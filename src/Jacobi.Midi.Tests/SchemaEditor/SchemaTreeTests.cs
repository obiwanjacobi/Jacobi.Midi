using Jacobi.Midi.SchemaEditor.SchemaTree;
using Jacobi.Midi.SchemaEditor.Services;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class SchemaTreeTests
{
    [Fact]
    public void ChangingDocument_ClearsSelection()
    {
        var first = SchemaFileService.CreateDocument("First");
        var tree = new SchemaTreeViewModel { Document = first };
        tree.SelectedNode = first.Members[0];

        tree.Document = SchemaFileService.CreateDocument("Second");
        Assert.Null(tree.SelectedNode);

        tree.SelectedNode = tree.Document.Members[0];
        tree.Document = null;
        Assert.Null(tree.SelectedNode);
    }
}
