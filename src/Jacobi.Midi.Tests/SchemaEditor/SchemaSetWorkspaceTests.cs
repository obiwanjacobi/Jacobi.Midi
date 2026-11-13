using System.Collections.ObjectModel;
using System.Reflection;
using Jacobi.BinarySerializer.Schema;
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

    private const string Library = """
        { "name": "Lib", "nodeDefs": [{ "name": "Header", "kind": "group", "members": [{ "name": "Length", "kind": "field", "datatype": "sys.uint8" }] }], "members": [] }
        """;

    private const string Device = """
        {
          "name": "Dev",
          "includes": [{ "schema": "Lib" }],
          "nodeDefs": [{ "name": "Body", "kind": "group", "members": [
              { "name": "Size", "kind": "field", "datatype": "sys.uint8" },
              { "name": "Data", "kind": "repeat", "count": { "ref": "^.Size" }, "members": [{ "name": "D", "kind": "field", "datatype": "sys.uint8" }] }] }],
          "members": [{ "name": "Root", "kind": "group", "members": [
              { "name": "Head", "kind": "group", "nodeDef": "Lib.Header" },
              { "name": "Items", "kind": "repeat", "count": { "ref": "Root.Head.Length" }, "members": [{ "name": "X", "kind": "field", "datatype": "sys.uint8" }] }] }]
        }
        """;

    [Fact]
    public void FindNodeDef_FindsAQualifiedNameInTheSchemaOfItsNamespace()
    {
        var (schemas, device, library) = CreateReferences();

        var found = schemas.FindNodeDef(Member(device, "Root", "Head"), new SchemaName("Lib.Header"), out var problem);

        Assert.Same(library.Schema.NodeDefs.Single(), found);
        Assert.Null(problem);
    }

    [Fact]
    public void FindNodeDef_FindsANameWithoutNamespaceInTheSchemaOfTheReferrer()
    {
        var (schemas, device, _) = CreateReferences();

        var found = schemas.FindNodeDef(Member(device, "Root"), new SchemaName("Body"), out _);

        Assert.Same(device.Schema.NodeDefs.Single(), found);
    }

    [Fact]
    public void FindNodeDef_ReportsASchemaThatIsNotLoaded()
    {
        var (schemas, device, _) = CreateReferences();

        var found = schemas.FindNodeDef(Member(device, "Root"), new SchemaName("Other.Header"), out var problem);

        Assert.Null(found);
        Assert.Contains("the schema 'Other' is not loaded", problem);
    }

    [Fact]
    public void FindNodeRef_FollowsAnAbsolutePathIntoTheNodeDefOfAnotherSchema()
    {
        var (schemas, device, library) = CreateReferences();
        Assert.True(SchemaNodeRef.TryParse("ref:Root.Head.Length", out var reference));

        var found = schemas.FindNodeRef(Member(device, "Root", "Items"), reference, out var problem);

        Assert.Same(((EditGroup)library.Schema.NodeDefs.Single()).Members.Single(), found);
        Assert.Null(problem);
    }

    [Fact]
    public void FindNodeRef_ResolvesARelativePathAgainstTheReferrer()
    {
        var (schemas, device, _) = CreateReferences();
        var body = (EditGroup)device.Schema.NodeDefs.Single();
        Assert.True(SchemaNodeRef.TryParse("ref:^.Size", out var reference));

        var found = schemas.FindNodeRef(body.Members[1], reference, out _);

        Assert.Same(body.Members[0], found);
    }

    [Fact]
    public void FindNodeRef_ReportsAnAbsolutePathThatDoesNotStartAtTheOutermostNode()
    {
        var (schemas, device, _) = CreateReferences();
        var body = (EditGroup)device.Schema.NodeDefs.Single();
        Assert.True(SchemaNodeRef.TryParse("ref:Root.Head.Length", out var reference));

        var found = schemas.FindNodeRef(body.Members[1], reference, out var problem);

        Assert.Null(found);
        Assert.Contains("does not start at 'Body'", problem);
    }

    [Fact]
    public void FindNodeRef_ReportsAPathThatGoesUpBeyondTheOutermostNode()
    {
        var (schemas, device, _) = CreateReferences();
        Assert.True(SchemaNodeRef.TryParse("ref:^.^.^.X", out var reference));

        var found = schemas.FindNodeRef(Member(device, "Root", "Items"), reference, out var problem);

        Assert.Null(found);
        Assert.Contains("goes up beyond 'Root'", problem);
    }

    [Fact]
    public void FindOwner_FindsTheEmbeddedSchemaOfANodeInIt()
    {
        var (documents, assemblies, _) = CreateWorkspace();
        var roland = assemblies.Load(Assembly.LoadFrom(TestHelpers.AssemblyPathOf("Jacobi.Midi.Roland")), "Jacobi.Midi.Roland.dll");
        var schemas = new SchemaSetWorkspace(documents, assemblies);
        var embedded = roland.Schemas.First(schema => schema.Document.Schema.Members.Count > 0);

        var owner = schemas.FindOwner(embedded.Document.Schema.Members[0]);

        Assert.Same(embedded, owner?.Embedded);
    }

    private static (SchemaSetWorkspace Schemas, SchemaWorkspaceDocument Device, SchemaWorkspaceDocument Library) CreateReferences()
    {
        var (documents, assemblies, _) = CreateWorkspace();
        var schemas = new SchemaSetWorkspace(documents, assemblies);
        var device = Open(documents, Device);
        var library = Open(documents, Library);
        return (schemas, device, library);
    }

    // a member of the document by its names from a root down
    private static EditNode Member(SchemaWorkspaceDocument document, params string[] names)
    {
        EditNode node = document.Schema;
        foreach (var name in names)
        {
            node = ((EditGroup)node).Members.Single(member => member.Name == name);
        }
        return node;
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
