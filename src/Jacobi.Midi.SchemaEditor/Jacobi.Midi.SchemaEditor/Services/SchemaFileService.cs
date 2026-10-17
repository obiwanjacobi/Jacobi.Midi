using System.Runtime.CompilerServices;
using Jacobi.BinarySerializer.Schema;
using Jacobi.BinarySerializer.Schema.Editing;
using Jacobi.BinarySerializer.Yaml;
using JsonSchemaSerializer = Jacobi.BinarySerializer.Schema.Json.JsonSerializer;
using XmlSchemaSerializer = Jacobi.BinarySerializer.Schema.Xml.XmlSerializer;

namespace Jacobi.Midi.SchemaEditor.Services;

public enum SchemaFormat
{
    Json,
    Xml,
    Yaml
}

public static class SchemaFileService
{
    private static readonly ConditionalWeakTable<EditDocument, XmlSchemaSource> XmlSources = new();

    public static SchemaFormat FromFileName(string fileName)
        => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".json" => SchemaFormat.Json,
            ".xml" => SchemaFormat.Xml,
            ".yaml" or ".yml" => SchemaFormat.Yaml,
            var extension => throw new NotSupportedException($"Schema file extension '{extension}' is not supported.")
        };

    public static string Extension(SchemaFormat format) => format switch
    {
        SchemaFormat.Json => ".json",
        SchemaFormat.Xml => ".xml",
        SchemaFormat.Yaml => ".yaml",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static EditDocument CreateDocument(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var document = new EditDocument { Name = name };
        document.Members.Add(new EditGroup { Name = "SysEx" });
        return document;
    }

    public static EditDocument Parse(string source, SchemaFormat format)
    {
        ArgumentNullException.ThrowIfNull(source);
        var schema = format switch
        {
            SchemaFormat.Json => JsonSchemaSerializer.Deserialize(source),
            SchemaFormat.Xml => XmlSchemaSerializer.Deserialize(source),
            SchemaFormat.Yaml => YamlSerializer.Deserialize(source),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        var document = EditNodeMapper.ToEditDocument(schema);
        if (format == SchemaFormat.Xml)
        {
            var canonical = XmlSchemaSerializer.Serialize(new SchemaDocumentBuilder(document).Build());
            XmlSources.Add(document, new XmlSchemaSource(source, canonical, document));
        }
        return document;
    }

    public static string Serialize(EditDocument document, SchemaFormat format)
    {
        var snapshot = new SchemaDocumentBuilder(document).Build();
        return format switch
        {
            SchemaFormat.Json => JsonSchemaSerializer.Serialize(snapshot),
            SchemaFormat.Xml => XmlSources.TryGetValue(document, out var source)
                ? source.Serialize(document, XmlSchemaSerializer.Serialize(snapshot))
                : XmlSchemaSerializer.Serialize(snapshot),
            SchemaFormat.Yaml => YamlSerializer.Serialize(snapshot),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }
}