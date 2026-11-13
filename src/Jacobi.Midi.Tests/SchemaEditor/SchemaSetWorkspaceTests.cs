using System.Collections.ObjectModel;
using System.Reflection;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Assemblies;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.NodeEditor;
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
        { "name": "Lib", "dataTypeDefs": [{ "name": "Small", "basedOn": "sys.uint8", "max": 10 }],
          "processorDefs": [{ "name": "mapped", "processor": "sys.map" }],
          "nodeDefs": [{ "name": "Header", "kind": "group", "members": [{ "name": "Length", "kind": "field", "datatype": "sys.uint8" }] }], "members": [] }
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
    public void FindDataTypeDef_FindsAQualifiedNameInTheSchemaOfItsNamespace()
    {
        var (schemas, device, library) = CreateReferences();

        var found = schemas.FindDataTypeDef(Member(device, "Root", "Items", "X"), new SchemaName("Lib.Small"), out var problem);

        Assert.Same(library.Schema.DataTypeDefs.Single(), found);
        Assert.Null(problem);
    }

    [Fact]
    public void FindDataTypeDef_FindsANameWithoutNamespaceInTheSchemaOfTheReferrer()
    {
        var (schemas, _, library) = CreateReferences();

        var found = schemas.FindDataTypeDef(library.Schema.NodeDefs.Single(), new SchemaName("Small"), out _);

        Assert.Same(library.Schema.DataTypeDefs.Single(), found);
    }

    [Fact]
    public void FindDataTypeDef_FollowsTheBaseTypeOfADataTypeDefIntoAnotherSchema()
    {
        var (schemas, device, library) = CreateReferences();
        var tiny = new EditDataTypeDef { Name = "Tiny", BasedOn = new SchemaName("Lib.Small") };
        device.Schema.DataTypeDefs.Add(tiny);

        var found = schemas.FindDataTypeDef(tiny, tiny.BasedOn, out var problem);

        Assert.Same(library.Schema.DataTypeDefs.Single(), found);
        Assert.Null(problem);
        Assert.Same(library, schemas.FindOwner(found!)?.Document);
    }

    [Fact]
    public void FindDataTypeDef_ReportsADataTypeDefThatIsNotInALoadedSchema()
    {
        var (schemas, _, _) = CreateReferences();
        var removed = new EditDataTypeDef { Name = "Removed", BasedOn = new SchemaName("Lib.Small") };

        var found = schemas.FindDataTypeDef(removed, removed.BasedOn, out var problem);

        Assert.Null(found);
        Assert.Contains("'Removed' is not in a loaded schema", problem);
    }

    [Fact]
    public void FindDataTypeDef_ReportsARegisteredDataType()
    {
        var (schemas, device, _) = CreateReferences();

        var found = schemas.FindDataTypeDef(Member(device, "Root", "Items", "X"), new SchemaName("sys.uint8"), out var problem);

        Assert.Null(found);
        Assert.Contains("registered data type", problem);
    }

    [Fact]
    public void FindDataTypeDef_ReportsASchemaThatIsNotLoaded()
    {
        var (schemas, device, _) = CreateReferences();

        var found = schemas.FindDataTypeDef(Member(device, "Root", "Items", "X"), new SchemaName("Other.Small"), out var problem);

        Assert.Null(found);
        Assert.Contains("'Other' is not loaded", problem);
    }

    [Fact]
    public void FindProcessorDef_FindsAQualifiedNameInTheSchemaOfItsNamespace()
    {
        var (schemas, device, library) = CreateReferences();

        var found = schemas.FindProcessorDef(Member(device, "Root", "Items", "X"), new SchemaName("Lib.mapped"), out var problem);

        Assert.Same(library.Schema.ProcessorDefs.Single(), found);
        Assert.Null(problem);
        Assert.Same(library, schemas.FindOwner(found!)?.Document);
    }

    [Fact]
    public void FindProcessorDef_ReportsANameThatIsNotADef()
    {
        var (schemas, device, _) = CreateReferences();

        var found = schemas.FindProcessorDef(Member(device, "Root", "Items", "X"), new SchemaName("midi.sysex"), out var problem);

        Assert.Null(found);
        Assert.Contains("'midi.sysex' is not found", problem);
    }

    [Theory]
    [InlineData("Lib.mapped", typeof(EditProcessorDef))]
    [InlineData("midi.sysex", typeof(ProcessorKey))]
    public void FindProcessor_FindsAProcessorDefOrAProcessorOfALoadedAssembly(string name, Type expected)
    {
        var (schemas, assemblies, device) = CreateProcessorReferences(name);
        using var details = ItemDetailsViewModel.Create(device, Member(device, "Root", "Items", "X"),
            new ProcessorCatalog(assemblies.Processors), schemas)!;

        var row = Assert.Single(details.Processors);

        Assert.IsType(expected, row.Target);
        Assert.True(row.CanGoTo);
        Assert.Null(row.TargetProblem);
        if (row.Target is ProcessorKey key)
        {
            Assert.Equal("midi", assemblies.FindProcessorNamespace(key)?.Namespace);
        }
    }

    [Fact]
    public void FindProcessor_ReportsANameThatIsNeitherAProcessorDefNorAProcessor()
    {
        var (schemas, assemblies, device) = CreateProcessorReferences("midi.unknown");
        using var details = ItemDetailsViewModel.Create(device, Member(device, "Root", "Items", "X"),
            new ProcessorCatalog(assemblies.Processors), schemas)!;

        var row = Assert.Single(details.Processors);

        Assert.Null(row.Target);
        Assert.False(row.CanGoTo);
        Assert.Contains("'midi.unknown' is not a processor of a loaded assembly", row.TargetProblem);
    }

    [Fact]
    public void FindReference_FollowsARefPathAndReportsARegisteredDataType()
    {
        var (schemas, device, library) = CreateReferences();
        var items = Member(device, "Root", "Items");
        var facets = new NodeFacets(device, items, schemas);
        var x = new NodeFacets(device, ((EditGroup)items).Members.Single(), schemas);

        var count = facets.References[nameof(NodeFacets.Count)];
        var dataType = x.References[nameof(NodeFacets.DataType)];

        Assert.True(count.CanGoTo);
        Assert.Same(((EditGroup)library.Schema.NodeDefs.Single()).Members.Single(), count.Target);
        Assert.False(dataType.CanGoTo);
        Assert.Contains("registered data type", dataType.Tip);
    }

    [Fact]
    public void FindReference_ReportsAPropertyThatIsNotSet()
    {
        var (schemas, device, _) = CreateReferences();
        var facets = new NodeFacets(device, Member(device, "Root", "Items"), schemas);

        var nodeDef = facets.FindReference(nameof(NodeFacets.NodeDef));

        Assert.False(nodeDef.CanGoTo);
        Assert.Contains("node def of Items is not set", nodeDef.Problem);
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

    [Fact]
    public void FindOwner_DoesNotFindANodeThatWasRemoved()
    {
        var (schemas, device, _) = CreateReferences();
        var root = (EditGroup)Member(device, "Root");
        var items = Member(device, "Root", "Items");
        var x = ((EditGroup)items).Members.Single();

        root.Members.Remove(items);

        // the removed node keeps its parent: neither it nor a node in it is found
        Assert.Null(schemas.FindOwner(items));
        Assert.Null(schemas.FindOwner(x));
        Assert.NotNull(schemas.FindOwner(root));
    }

    // the field Root.Items.X of the device schema has the processor 'name'
    private static (SchemaSetWorkspace Schemas, AssemblyWorkspace Assemblies, SchemaWorkspaceDocument Device) CreateProcessorReferences(string name)
    {
        var (documents, assemblies, _) = CreateWorkspace();
        var schemas = new SchemaSetWorkspace(documents, assemblies);
        var device = Open(documents, Device);
        Open(documents, Library);
        ((EditField)Member(device, "Root", "Items", "X")).Processors.Add(new EditProcessorRef { Processor = new SchemaName(name) });
        return (schemas, assemblies, device);
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
