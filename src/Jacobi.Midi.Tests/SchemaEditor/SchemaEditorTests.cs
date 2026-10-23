using System.Xml.Linq;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.Services;
using Jacobi.Midi.SchemaEditor.ViewModels;
using Xunit;

namespace Jacobi.Midi.Tests.SchemaEditor;

public class SchemaEditorTests
{
    [Theory]
    [InlineData(nameof(SchemaFormat.Json))]
    [InlineData(nameof(SchemaFormat.Xml))]
    [InlineData(nameof(SchemaFormat.Yaml))]
    public void NewSchema_CanAddDocumentedFieldAndReload(string formatName)
    {
        var format = Enum.Parse<SchemaFormat>(formatName);
        var schema = SchemaFileService.CreateDocument("TestSchema");
        var document = new SchemaWorkspaceDocument(schema, format);

        var group = Assert.IsType<EditGroup>(Assert.Single(schema.Members));
        Assert.Equal("SysEx", group.Name);

        group.Properties.Add(new EditProperty { Name = "description", Value = "The complete system-exclusive message." });
        var addedGroup = document.AddGroup(group, "Header");
        document.AddField(
            addedGroup,
            "Parameter",
            "sys.uint8",
            "2A");

        var source = SchemaFileService.Serialize(schema, format);
        schema = SchemaFileService.Parse(source, format);

        var updatedGroup = Assert.IsType<EditGroup>(Assert.Single(schema.Members));
        Assert.Equal("The complete system-exclusive message.",
            Assert.Single(updatedGroup.Properties, property => property.Name == "description").Value);
        var header = Assert.IsType<EditGroup>(Assert.Single(updatedGroup.Members));
        Assert.Equal("Header", header.Name);
        var field = Assert.IsType<EditField>(Assert.Single(header.Members));
        Assert.Equal("Parameter", field.Name);
        Assert.Equal("sys.uint8", field.DataType?.ToString());
        Assert.Equal("2A",
            Assert.Single(field.Properties, property => property.Name == "sample").Value);
    }

    [Theory]
        [InlineData(nameof(SchemaFormat.Json))]
        [InlineData(nameof(SchemaFormat.Xml))]
        [InlineData(nameof(SchemaFormat.Yaml))]
        public void ApplyUnchangedFieldAndSave_PreservesUnexposedSchemaFeatures(string formatName)
        {
                var format = Enum.Parse<SchemaFormat>(formatName);
                var schema = SchemaFileService.Parse("""
                        {
                            "name": "Advanced",
                            "includes": [{ "schema": "Other", "path": "other.json" }],
                            "nodeDefs": [{ "name": "Payload", "kind": "field", "datatype": "sys.uint8" },
                                                     { "name": "Body", "kind": "group", "members": [] }],
                            "dataTypeDefs": [{ "name": "Scaled", "basedOn": "sys.int16", "min": -10, "max": 20,
                                                                 "scale": 2, "shift": 1, "options": { "1": "One" } }],
                            "processorDefs": [{ "name": "mapping", "processor": "sys.map", "properties": [{ "name": "default", "value": null }] }],
                            "members": [{ "name": "Root", "kind": "group", "byteSize": { "pub": "header.size" },
                                "properties": [{ "name": "extra", "value": null }],
                                "members": [
                                    { "name": "Inherited", "kind": "field", "nodeDef": "Payload", "value": "240", "byteOffset": -1,
                                        "byteLength": { "ref": "Root.Items[].Length" },
                                        "processors": [{ "name": "mapping", "pubns": "field", "option": "custom" }] },
                                    { "name": "Items", "kind": "repeat", "count": { "ref": "Root.Inherited" },
                                        "valueProcessors": [{ "name": "mapping" }], "members": [
                                            { "name": "Length", "kind": "field", "datatype": "Scaled" }] },
                                    { "name": "Flags", "kind": "group", "byteSize": 2,
                                        "processors": [{ "name": "sys.bitpacker", "bitorder": "little" }],
                                        "members": [{ "name": "Bit", "kind": "field", "datatype": "sys.bool" }] },
                                    { "name": "Body", "kind": "choice", "memberNodeDef": "Body", "pipeline": "isolate" }
                                ] }]
                        }
                        """, SchemaFormat.Json);
                var original = SchemaFileService.Serialize(schema, SchemaFormat.Json);
                var loaded = SchemaFileService.Parse(SchemaFileService.Serialize(schema, format), format);
                var document = new SchemaWorkspaceDocument(loaded, format);
                var viewModel = new MainViewModel { SelectedDocument = document };
                var selectedField = Assert.IsType<EditGroup>(Assert.Single(loaded.Members)).Members[0];
                viewModel.SelectedNode = selectedField;

                // a field with a node def and no data type can be edited
                Assert.Equal(String.Empty, viewModel.NodeFacets.DataType);
                var name = selectedField.Name;
                viewModel.NodeFacets.Name = name + "X";
                viewModel.NodeFacets.Name = name;

                Assert.Same(loaded, document.Schema);
                Assert.Same(selectedField, viewModel.SelectedNode);
                Assert.Null(Assert.IsType<EditField>(selectedField).DataType);
                Assert.True(document.IsDirty);
                var saved = SchemaFileService.Serialize(document.Schema, format);
                var reloaded = SchemaFileService.Parse(saved, format);
                Assert.Equal(original, SchemaFileService.Serialize(reloaded, SchemaFormat.Json));
        }

        [Fact]
        public void XmlRename_PreservesCommentsDeclarationAndCompactSyntax()
        {
                const string source = """
                        <?xml version="1.0" encoding="utf-8"?>
                        <!-- Roland Exclusive Format Type IV
                                 Header documentation stays here. -->
                        <schema name="Roland">
                            <includes><include schema="midi" /></includes>
                            <nodeDefs>
                                <!-- Shared 7-bit representation. -->
                                <field name="sysex16" datatype="sys.uint16" byteLength="2">
                                    <processor name="midi.sysexbyte" />
                                </field>
                            </nodeDefs>
                            <members><group name="Message"><members>
                                <!-- Checksum includes only this scope. -->
                                <group name="ChecksumBlock"><processors><processor name="roland.checksum" /></processors>
                                    <members><field name="Address" nodeDef="sysex16" /></members>
                                </group>
                                <!-- This sibling stays untouched. -->
                                <field name="End" datatype="sys.uint8" value="0xF7" />
                            </members></group></members>
                        </schema>
                        <!-- End of schema. -->
                        """;
                var schema = SchemaFileService.Parse(source, SchemaFormat.Xml);
                var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Xml);
                var message = Assert.IsType<EditGroup>(schema.Members[0]);
                var scope = Assert.IsType<EditGroup>(message.Members[0]);
                new NodeFacets(document, scope).Name = "ChecksumScope";

                var saved = SchemaFileService.Serialize(schema, SchemaFormat.Xml);
                Assert.Equal(source.Replace("ChecksumBlock", "ChecksumScope", StringComparison.Ordinal), saved);
                var restored = SchemaFileService.Parse(saved, SchemaFormat.Xml);
                var definition = Assert.IsType<EditField>(Assert.Single(restored.NodeDefs));
                Assert.Equal("midi.sysexbyte", Assert.Single(definition.Processors).Processor.ToString());
                Assert.True(definition.ByteLength is int length && length == 2);
                Assert.Equal(saved, SchemaFileService.Serialize(schema, SchemaFormat.Xml));
        }

        [Fact]
        public void RolandXmlRename_PreservesOriginalPresentationAndDefinitions()
        {
                const string source = """
                        <?xml version="1.0" encoding="utf-8"?>
                        <!--
                            Roland Exclusive Format Type IV (one way / bulk dump), as used by the GP-8 (2 address bytes).

                                F0 41 0000nnnn <model> <cmd> <address:2> ... <checksum> F7

                            RQ1 (0x11): Address (2 x 7 bits), Size (2 x 7 bits), Checksum
                            DT1 (0x12): Address (2 x 7 bits), Data, Checksum

                            The model id is device specific (GP-8 = 0x13) and is therefore not fixed here.
                            The device id is the MIDI basic channel - 1 (0000nnnn).
                            The content of the Address and Data is described by a device schema (see Gp8.xml).

                            Legend:
                                [OK]   available now
                                [TODO] not available yet
                        -->
                        <schema name="Roland">
                            <includes>
                                <include schema="midi" />
                            </includes>
                            <nodeDefs>
                                <group name="messageBody" />
                                <field name="sysex16" datatype="sys.uint16" byteLength="2" >
                                    <processor name="midi.sysexbyte" />
                                </field>
                            </nodeDefs>

                            <members>
                                <group name="Message">
                                    <processors>
                                        <processor name="sys.bytepacker" byteorder="big" />
                                        <processor name="midi.sysex" />
                                    </processors>
                                    <members>
                                        <group name="RQ1">
                                            <members>
                                                <field name="RolandId" datatype="sys.uint8" value="0x41" />
                                                <field name="DeviceId" datatype="sys.uint8" />
                                                <field name="ModelId" datatype="sys.uint8" />
                                                <field name="Command" datatype="sys.uint8" value="0x11" />
                                                <group name="ChecksumBlock">
                                                    <processors>
                                                        <processor name="roland.checksum" />
                                                    </processors>
                                                    <members>
                                                        <field name="Address" nodeDef="sysex16" />
                                                        <field name="Size" nodeDef="sysex16" />
                                                        <choice name="Data" memberNodeDef="messageBody" />
                                                    </members>
                                                </group>
                                            </members>
                                        </group>
                                        <group name="DT1">
                                            <members>
                                                <field name="RolandId" datatype="sys.uint8" value="0x41" />
                                                <field name="DeviceId" datatype="sys.uint8" />
                                                <field name="ModelId" datatype="sys.uint8" />
                                                <field name="Command" datatype="sys.uint8" value="0x12" />
                                                <group name="ChecksumBlock">
                                                    <processors>
                                                        <processor name="roland.checksum" />
                                                    </processors>
                                                    <members>
                                                        <field name="Address" nodeDef="sysex16" />
                                                        <choice name="Data" memberNodeDef="messageBody" />
                                                    </members>
                                                </group>
                                            </members>
                                        </group>
                                    </members>
                                </group>
                            </members>
                        </schema>
                        """;
                var schema = SchemaFileService.Parse(source, SchemaFormat.Xml);
                var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Xml);
                var viewModel = new MainViewModel { SelectedDocument = document };
                foreach (var branch in Assert.IsType<EditGroup>(Assert.Single(schema.Members)).Members.OfType<EditGroup>())
                {
                        viewModel.SelectedNode = branch.Members.Single(node => node.Name == "ChecksumBlock");
                        viewModel.NodeFacets.Name = "ChecksumScope";
                }

                var saved = SchemaFileService.Serialize(schema, SchemaFormat.Xml);
                var expected = XDocument.Parse(source.Replace("ChecksumBlock", "ChecksumScope", StringComparison.Ordinal), LoadOptions.PreserveWhitespace);
                var actual = XDocument.Parse(saved, LoadOptions.PreserveWhitespace);
                Assert.True(XNode.DeepEquals(expected, actual));
                Assert.Equal(expected.Declaration?.ToString(), actual.Declaration?.ToString());
                Assert.DoesNotContain("xmlns:", saved);
                Assert.DoesNotContain("<properties", saved);
                Assert.DoesNotContain("<valueProcessors", saved);
                var restored = SchemaFileService.Parse(saved, SchemaFormat.Xml);
                var definition = Assert.IsType<EditField>(restored.NodeDefs.Single(node => node.Name == "sysex16"));
                Assert.Equal("midi.sysexbyte", Assert.Single(definition.Processors).Processor.ToString());
                Assert.True(definition.ByteLength is int length && length == 2);
                Assert.Equal(saved, SchemaFileService.Serialize(restored, SchemaFormat.Xml));
        }

        [Theory]
        [InlineData("\n")]
        [InlineData("\r\n")]
        public void XmlSave_PreservesNoOpTextAndLineEndings(string newLine)
        {
                var source = String.Join(newLine,
                        "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
                        "<!-- Header -->",
                        "<schema name=\"Doc\"><members>",
                        "  <!-- Node -->",
                        "  <group name=\"Root\" />",
                        "</members></schema>",
                        String.Empty);
                var schema = SchemaFileService.Parse(source, SchemaFormat.Xml);
                Assert.Equal(source, SchemaFileService.Serialize(schema, SchemaFormat.Xml));
                var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Xml);
                new NodeFacets(document, schema.Members[0]).Name = "Renamed";
                Assert.Equal(source.Replace("name=\"Root\"", "name=\"Renamed\"", StringComparison.Ordinal),
                        SchemaFileService.Serialize(schema, SchemaFormat.Xml));
        }

        [Fact]
        public void XmlAddAndEdit_PreservesInlinePropertiesAndNodeComments()
        {
                const string source = """
                        <schema name="Test"><members><group name="Root"><members>
                            <!-- Keep this field comment. -->
                            <field name="Value" datatype="sys.uint8" description="Old" />
                            <!-- Keep this group comment. -->
                        </members></group></members></schema>
                        """;
                var schema = SchemaFileService.Parse(source, SchemaFormat.Xml);
                var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Xml);
                var root = Assert.IsType<EditGroup>(schema.Members[0]);
                new NodeFacets(document, root.Members[0]).Name = "Renamed";
                Assert.Single(root.Members[0].Properties, property => property.Name == "description").Value = "New";
                document.AddField(root, "Added", "sys.uint8", "2A");

                var saved = SchemaFileService.Serialize(schema, SchemaFormat.Xml);
                Assert.Contains("<!-- Keep this field comment. -->", saved);
                Assert.Contains("<!-- Keep this group comment. -->", saved);
                Assert.Contains("description=\"New\"", saved);
                var restored = SchemaFileService.Parse(saved, SchemaFormat.Xml);
                var members = Assert.IsType<EditGroup>(restored.Members[0]).Members;
                Assert.Equal(new[] { "Renamed", "Added" }, members.Select(node => node.Name));
                Assert.Equal("2A", Assert.Single(members[1].Properties).Value);
        }

        [Fact]
        public void XmlReorderInsertAndRemove_PreservesDraftOrderAndMovesNodeComments()
        {
            const string source = """
                <schema name="Test"><members><group name="Root"><members>
                  <!-- First field -->
                  <field name="First" datatype="sys.uint8" />
                  <!-- Second field -->
                  <field name="Second" datatype="sys.uint8" />
                </members></group></members></schema>
                """;
            var schema = SchemaFileService.Parse(source, SchemaFormat.Xml);
            var root = Assert.IsType<EditGroup>(schema.Members[0]);
            root.Members.Move(1, 0);
            root.Members.Insert(1, new EditField(root) { Name = "Inserted", DataType = new("sys.uint8"), Processors = [], Properties = [] });
            var saved = SchemaFileService.Serialize(schema, SchemaFormat.Xml);
            var members = XDocument.Parse(saved).Descendants("group").Single().Element("members")!;
            Assert.Equal(new[] { "Second", "Inserted", "First" }, members.Elements().Select(element => (string?)element.Attribute("name")));
            Assert.Equal(new[] { " Second field ", " First field " }, members.Nodes().OfType<XComment>().Select(comment => comment.Value));
            root.Members.RemoveAt(2);
            saved = SchemaFileService.Serialize(schema, SchemaFormat.Xml);
            var restored = SchemaFileService.Parse(saved, SchemaFormat.Xml);
            Assert.Equal(new[] { "Second", "Inserted" }, Assert.IsType<EditGroup>(restored.Members[0]).Members.Select(node => node.Name));
        }

        [Fact]
        public void XmlRemoveInlineProcessor_DoesNotResurrectOriginalDeclaration()
        {
            const string source = """
                <schema name="Test"><nodeDefs><field name="Byte" datatype="sys.uint8">
                  <!-- Keep this definition note. -->
                  <processor name="sys.scale" scale="2" />
                </field></nodeDefs><members><group name="Root" /></members></schema>
                """;
            var schema = SchemaFileService.Parse(source, SchemaFormat.Xml);
            Assert.IsType<EditField>(schema.NodeDefs[0]).Processors.Clear();
            var saved = SchemaFileService.Serialize(schema, SchemaFormat.Xml);
            Assert.Contains("<!-- Keep this definition note. -->", saved);
            Assert.DoesNotContain("sys.scale", saved);
            Assert.Empty(Assert.IsType<EditField>(SchemaFileService.Parse(saved, SchemaFormat.Xml).NodeDefs[0]).Processors);
        }

        [Fact]
        public void Commands_EditDraftInPlaceAndSelectNewNodes()
        {
                var viewModel = new MainViewModel();
                viewModel.NewDocumentCommand.Execute(null);
                var document = Assert.Single(viewModel.Documents);
                var schema = document.Schema;
                var root = Assert.IsType<EditGroup>(Assert.Single(schema.Members));
                Assert.True(document.IsDirty);

                viewModel.AddNodeCommand.Execute(SchemaNodeKind.Group);
                var header = Assert.IsType<EditGroup>(Assert.Single(root.Members));
                Assert.Same(header, viewModel.SelectedNode);
                Assert.Equal("NewGroup", viewModel.NodeFacets.Name);
                viewModel.AddNodeCommand.Execute(SchemaNodeKind.Field);
                var field = Assert.IsType<EditField>(Assert.Single(header.Members));
                Assert.Same(field, viewModel.SelectedNode);
                Assert.Equal("NewField", viewModel.NodeFacets.Name);
                Assert.Equal("sys.uint8", viewModel.NodeFacets.DataType);

                // the General tab edits the node live and keeps its properties
                field.Properties.Add(new EditProperty { Name = "description", Value = "A parameter" });
                viewModel.NodeFacets.Name = "Renamed";
                Assert.Equal("Renamed", field.Name);
                Assert.Equal("A parameter", Assert.Single(field.Properties, property => property.Name == "description").Value);
                Assert.Same(field, viewModel.SelectedNode);

                viewModel.AddNodeCommand.Execute(SchemaNodeKind.Field);
                Assert.Equal(2, header.Members.Count);
                Assert.Same(header.Members[1], viewModel.SelectedNode);
                Assert.Equal("NewField", header.Members[1].Name);
                Assert.Same(schema, document.Schema);
                Assert.Equal(4, document.Revision);
        }

        [Fact]
        public void AddNode_CreatesEachKindWithUniqueNames()
        {
                var viewModel = new MainViewModel();
                viewModel.NewDocumentCommand.Execute(null);
                var root = Assert.IsType<EditGroup>(Assert.Single(viewModel.SelectedDocument!.Schema.Members));

                viewModel.AddNodeCommand.Execute(SchemaNodeKind.Repeat);
                var repeat = Assert.IsType<EditRepeat>(Assert.Single(root.Members));
                Assert.True(repeat.Count is int count && count == 1);
                viewModel.SelectedNode = root;
                viewModel.AddNodeCommand.Execute(SchemaNodeKind.Choice);
                Assert.IsType<EditChoice>(root.Members[1]);
                viewModel.SelectedNode = root;
                viewModel.AddNodeCommand.Execute(SchemaNodeKind.Repeat);
                Assert.Equal(["NewRepeat", "NewChoice", "NewRepeat2"], root.Members.Select(member => member.Name));

                // the new kinds survive a save round trip
                var saved = SchemaFileService.Serialize(viewModel.SelectedDocument.Schema, SchemaFormat.Json);
                var restored = Assert.IsType<EditGroup>(Assert.Single(SchemaFileService.Parse(saved, SchemaFormat.Json).Members));
                Assert.Equal([typeof(EditRepeat), typeof(EditChoice), typeof(EditRepeat)], restored.Members.Select(member => member.GetType()));
        }

        [Fact]
        public void DeleteNode_RemovesSelectionAndSelectsParent()
        {
                var viewModel = new MainViewModel();
                Assert.False(viewModel.DeleteItemCommand.CanExecute(null));
                viewModel.NewDocumentCommand.Execute(null);
                var document = viewModel.SelectedDocument!;
                var root = Assert.IsType<EditGroup>(Assert.Single(document.Schema.Members));
                viewModel.AddNodeCommand.Execute(SchemaNodeKind.Field);
                var field = Assert.IsType<EditField>(Assert.Single(root.Members));
                var revision = document.Revision;

                Assert.True(viewModel.DeleteItemCommand.CanExecute(null));
                viewModel.DeleteItemCommand.Execute(null);
                viewModel.ConfirmDialog!.ConfirmCommand.Execute(null);
                Assert.Empty(root.Members);
                Assert.Same(root, viewModel.SelectedNode);
                Assert.Equal(revision + 1, document.Revision);

                viewModel.DeleteItemCommand.Execute(null);
                viewModel.ConfirmDialog!.ConfirmCommand.Execute(null);
                Assert.Empty(document.Schema.Members);
                Assert.Same(document.Schema, viewModel.SelectedNode);
                Assert.False(viewModel.DeleteItemCommand.CanExecute(null));

                var definition = new EditGroup(null) { Name = "def", Members = [], Processors = [], Properties = [] };
                document.Schema.NodeDefs.Add(definition);
                viewModel.SelectedNode = definition;
                viewModel.DeleteItemCommand.Execute(null);
                viewModel.ConfirmDialog!.ConfirmCommand.Execute(null);
                Assert.Empty(document.Schema.NodeDefs);
                Assert.Same(document.Schema, viewModel.SelectedNode);
        }

        [Fact]
        public void SwitchingDocuments_SelectsTheDocumentRoot()
        {
                var viewModel = new MainViewModel();
                viewModel.NewDocumentCommand.Execute(null);
                var first = Assert.Single(viewModel.Documents);
                viewModel.AddNodeCommand.Execute(SchemaNodeKind.Field);
                Assert.IsType<EditField>(viewModel.SelectedNode);
                viewModel.NewDocumentCommand.Execute(null);
                var second = viewModel.SelectedDocument;
                Assert.Equal("NewSchema2", second!.Schema.Name);
                Assert.Same(second.Schema, viewModel.SelectedItem);
                Assert.Equal(String.Empty, viewModel.NodeFacets.Name);

                viewModel.SelectedDocument = first;
                Assert.Same(first.Schema, viewModel.SelectedItem);
        }

        [Fact]
        public void InvalidEdits_DoNotChangeDraftOrDirtyState()
        {
                var schema = SchemaFileService.CreateDocument("Test");
                var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Json);
                var root = Assert.IsType<EditGroup>(Assert.Single(schema.Members));
                var field = document.AddField(root, "Original", "sys.uint8", "2A");
                document.IsDirty = false;
                var revision = document.Revision;
                var viewModel = new MainViewModel { SelectedDocument = document };
                viewModel.SelectedNode = field;
                // the bound editor shows the error of an invalid edit; the node keeps its value
                Assert.Throws<ArgumentException>(() => viewModel.NodeFacets.Name = " ");
                Assert.Throws<ArgumentException>(() => viewModel.NodeFacets.DataType = String.Empty);
                // leaving an editor without a change writes nothing
                viewModel.NodeFacets.Name = "Original";

                Assert.Equal("Original", field.Name);
                Assert.Equal("sys.uint8", field.DataType?.ToString());
                Assert.False(document.IsDirty);
                Assert.Equal(revision, document.Revision);
        }

        [Fact]
        public void OpenChoice_RejectsExplicitMembersWithoutMutation()
        {
                var schema = SchemaFileService.CreateDocument("Special");
                var root = Assert.IsType<EditGroup>(Assert.Single(schema.Members));
                var open = new EditChoice(root)
                {
                        Name = "Body",
                        MemberNodeDef = new("BodyType"),
                        Members = [],
                        Processors = [],
                        Properties = [],
                        ValueProcessors = [],
                };
                root.Members.Add(open);
                var document = new SchemaWorkspaceDocument(schema, SchemaFormat.Json);
                var viewModel = new MainViewModel { SelectedDocument = document };

                viewModel.SelectedNode = open;
                viewModel.AddNodeCommand.Execute(SchemaNodeKind.Field);
                viewModel.AddNodeCommand.Execute(SchemaNodeKind.Group);
                Assert.Empty(open.Members);
                Assert.False(document.IsDirty);

                viewModel.SelectedNode = root;
                viewModel.AddNodeCommand.Execute(SchemaNodeKind.Field);
                Assert.IsType<EditField>(root.Members[1]);
                Assert.True(document.IsDirty);
        }

        [Theory]
    [InlineData("schema.json", nameof(SchemaFormat.Json))]
    [InlineData("schema.xml", nameof(SchemaFormat.Xml))]
    [InlineData("schema.yaml", nameof(SchemaFormat.Yaml))]
    [InlineData("schema.yml", nameof(SchemaFormat.Yaml))]
    public void FromFileName_RecognizesSupportedFormats(string fileName, string expected)
    {
        Assert.Equal(Enum.Parse<SchemaFormat>(expected), SchemaFileService.FromFileName(fileName));
    }

    [Fact]
    public void FromFileName_RejectsUnsupportedFormats()
    {
        Assert.Throws<NotSupportedException>(() => SchemaFileService.FromFileName("schema.syx"));
    }

}
