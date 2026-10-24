using System.Collections.ObjectModel;
using System.Reflection;
using Jacobi.Midi.SchemaEditor.Assemblies;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.Services;
using Jacobi.Midi.Tests.Schema;
using Xunit;
using JsonSerializer = Jacobi.BinarySerializer.Schema.Json.JsonSerializer;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class SchemaSetWorkspaceTests
{
    private const string OpenMidi = """
        { "name": "Midi", "members": [{ "name": "Root", "kind": "group", "members": [{ "name": "A", "kind": "field", "datatype": "sys.uint8" }] }] }
        """;

    [Fact]
    public void Find_UsesAnOpenDocumentInsteadOfTheEmbeddedSchemaWithTheSameName()
    {
        var (documents, assemblies, embedded) = CreateWorkspace();
        var schemas = new SchemaSetWorkspace(documents, assemblies);
        Assert.Same(embedded.Document, schemas.Find("midi")!.Document);
        var changed = 0;
        schemas.Changed += (_, _) => changed++;

        var open = Open(documents, OpenMidi);

        Assert.Same(open, schemas.Find("midi")!.Document);
        Assert.Equal("the open document 'Midi'", embedded.ReplacedBy);
        Assert.Contains("is used instead of the embedded schema 'Midi'", schemas.GetSourceNote(open));
        Assert.Contains("The open document 'Midi' is used instead of this schema", schemas.GetSourceNote(embedded.Document));
        Assert.True(changed > 0);

        documents.Remove(open);

        Assert.Same(embedded.Document, schemas.Find("midi")!.Document);
        Assert.False(embedded.IsReplaced);
        Assert.Null(schemas.GetSourceNote(embedded.Document));
    }

    [Fact]
    public void Find_DoesNotResolveTheNameOfTheRequester()
    {
        var (documents, assemblies, embedded) = CreateWorkspace();
        var schemas = new SchemaSetWorkspace(documents, assemblies);

        Assert.Null(schemas.Find("Midi", embedded.Document.Schema));
        Assert.NotNull(schemas.Find("Midi"));
    }

    [Fact]
    public void Includes_AreFlaggedWhenTheIncludedSchemaIsOpenedOrClosed()
    {
        var (documents, assemblies, _) = CreateWorkspace();
        var schemas = new SchemaSetWorkspace(documents, assemblies);
        var device = Open(documents, """{ "name": "Dev", "includes": [{ "schema": "Lib" }], "members": [] }""");
        var include = device.Schema.Includes.Single();
        Assert.False(include.IsLoaded);

        var library = Open(documents, """{ "name": "Lib", "members": [] }""");
        Assert.True(include.IsLoaded);

        documents.Remove(library);
        Assert.False(include.IsLoaded);
    }

    [Fact]
    public void Compile_OfAnEmbeddedSchemaUsesItAndReportsTheOpenDocumentWithItsName()
    {
        var (documents, assemblies, embedded) = CreateWorkspace();
        var schemas = new SchemaSetWorkspace(documents, assemblies);
        Open(documents, OpenMidi);

        var compilation = schemas.Compile(embedded.Document.Schema);

        Assert.Contains(embedded.Document.Schema, compilation.Documents);
        Assert.Contains(compilation.Warnings, warning => warning.Contains("The embedded schema 'Midi'") && warning.Contains("instead of the open document 'Midi'"));
    }

    [Fact]
    public void Compile_OfACopyLeavesOutTheOriginalWithoutAWarning()
    {
        var (documents, assemblies, _) = CreateWorkspace();
        var schemas = new SchemaSetWorkspace(documents, assemblies);
        var original = Open(documents, """{ "name": "Dev", "members": [] }""");
        var copy = EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize("""{ "name": "Dev", "members": [] }"""));

        var compilation = schemas.Compile(copy, original);

        Assert.Contains(copy, compilation.Documents);
        Assert.DoesNotContain(original.Schema, compilation.Documents);
        Assert.Empty(compilation.Warnings);
    }

    // the Device assembly embeds the base 'Midi' schema
    private static (ObservableCollection<SchemaWorkspaceDocument> Documents, AssemblyWorkspace Assemblies, LoadedSchema Embedded) CreateWorkspace()
    {
        var assemblies = new AssemblyWorkspace();
        var loaded = assemblies.Load(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Device")), "Jacobi.Midi.Device.dll");
        return ([], assemblies, loaded.Schemas.Single(schema => schema.Name == "Midi"));
    }

    private static SchemaWorkspaceDocument Open(ObservableCollection<SchemaWorkspaceDocument> documents, string json)
    {
        var document = new SchemaWorkspaceDocument(EditNodeMapper.ToEditDocument(JsonSerializer.Deserialize(json)), SchemaFormat.Json);
        documents.Add(document);
        return document;
    }
}
