using System.Runtime.CompilerServices;
using Jacobi.BinarySerializer.Yaml;
using Jacobi.Midi.SchemaEditor.Models;
using JsonSchemaSerializer = Jacobi.BinarySerializer.Schema.Json.JsonSerializer;
using XmlSchemaSerializer = Jacobi.BinarySerializer.Schema.Xml.XmlSerializer;

namespace Jacobi.Midi.SchemaEditor.Services;

internal enum SchemaFormat
{
    Json,
    Xml,
    Yaml
}

internal static class SchemaFileService
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

    /// <summary>
    /// The name of the format to show to the user, e.g. "XML".
    /// </summary>
    public static string DisplayName(SchemaFormat format) => format switch
    {
        SchemaFormat.Json => "JSON",
        SchemaFormat.Xml => "XML",
        SchemaFormat.Yaml => "YAML",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static EditDocument CreateDocument(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var document = new EditDocument
        {
            Name = name,
            DataTypeDefs = [],
            Includes = [],
            Members = [],
            NodeDefs = [],
            ProcessorDefs = [],
            Processors = [],
            Properties = [],
        };
        document.Members.Add(new EditGroup(document)
        {
            Name = "SysEx",
            Members = [],
            Processors = [],
            Properties = [],
        });
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
            var canonical = XmlSchemaSerializer.Serialize(EditNodeMapper.ToSchemaDocument(document));
            XmlSources.Add(document, new XmlSchemaSource(source, canonical, document));
        }
        return document;
    }

    public static string Serialize(EditDocument document, SchemaFormat format)
    {
        var canonical = SerializeCanonical(document, format);
        return format == SchemaFormat.Xml && XmlSources.TryGetValue(document, out var source)
            ? source.Serialize(document, canonical)
            : canonical;
    }

    /// <summary>
    /// Serializes the schema of the document as the serializer writes it (<see cref="EditNodeMapper.ToSchemaDocument"/>),
    /// without the formatting or comments of the source it was loaded from.
    /// </summary>
    public static string SerializeCanonical(EditDocument document, SchemaFormat format)
    {
        var snapshot = EditNodeMapper.ToSchemaDocument(document);
        return format switch
        {
            SchemaFormat.Json => JsonSchemaSerializer.Serialize(snapshot),
            SchemaFormat.Xml => XmlSchemaSerializer.Serialize(snapshot),
            SchemaFormat.Yaml => YamlSerializer.Serialize(snapshot),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }
}