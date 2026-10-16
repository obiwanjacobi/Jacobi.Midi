using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Jacobi.BinarySerializer.Schema;
using Jacobi.BinarySerializer.Yaml;
using YamlDotNet.RepresentationModel;

namespace Jacobi.Midi.SchemaEditor.Services;

public enum SchemaFormat
{
    Json,
    Xml,
    Yaml
}

public static class SchemaFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static SchemaFormat FromFileName(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".json" => SchemaFormat.Json,
            ".xml" => SchemaFormat.Xml,
            ".yaml" or ".yml" => SchemaFormat.Yaml,
            var extension => throw new NotSupportedException($"Schema file extension '{extension}' is not supported.")
        };
    }

    public static string Extension(SchemaFormat format) => format switch
    {
        SchemaFormat.Json => ".json",
        SchemaFormat.Xml => ".xml",
        SchemaFormat.Yaml => ".yaml",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static SchemaDocument Parse(string source, SchemaFormat format)
    {
        ArgumentNullException.ThrowIfNull(source);

        return format switch
        {
            SchemaFormat.Json => LoadJson(source),
            SchemaFormat.Xml => LoadXml(source),
            SchemaFormat.Yaml => YamlSerializer.Deserialize(source),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    public static string CreateTemplate(string name, SchemaFormat format)
    {
        var safeName = JsonSerializer.Serialize(name);
        return format switch
        {
            SchemaFormat.Json => $$"""
                {
                  "name": {{safeName}},
                  "members": [
                    {
                      "kind": "group",
                      "name": "SysEx",
                      "members": []
                    }
                  ]
                }
                """,
            SchemaFormat.Xml => $"<schema name=\"{System.Security.SecurityElement.Escape(name)}\"><members><group name=\"SysEx\"><members /></group></members></schema>",
            SchemaFormat.Yaml => $"name: {JsonSerializer.Serialize(name)}\nmembers:\n  - kind: group\n    name: SysEx\n    members: []\n",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    public static string UpdateNode(
        string source,
        SchemaFormat format,
        IReadOnlyList<int> memberPath,
        string name,
        string dataType,
        string description,
        string sampleValue)
    {
        if (memberPath.Count == 0)
        {
            throw new ArgumentException("A schema node must be selected before editing.", nameof(memberPath));
        }

        var updated = format switch
        {
            SchemaFormat.Json => UpdateJsonNode(source, memberPath, name, dataType, description, sampleValue),
            SchemaFormat.Xml => UpdateXmlNode(source, memberPath, name, dataType, description, sampleValue),
            SchemaFormat.Yaml => UpdateYamlNode(source, memberPath, name, dataType, description, sampleValue),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

        Parse(updated, format);
        return updated;
    }

    public static string AddField(
        string source,
        SchemaFormat format,
        IReadOnlyList<int> groupPath,
        string name,
        string dataType,
        string sampleValue)
    {
        if (String.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A field name is required.", nameof(name));
        }
        if (String.IsNullOrWhiteSpace(dataType))
        {
            throw new ArgumentException("A data type is required.", nameof(dataType));
        }

        var updated = format switch
        {
            SchemaFormat.Json => AddJsonField(source, groupPath, name, dataType, sampleValue),
            SchemaFormat.Xml => AddXmlField(source, groupPath, name, dataType, sampleValue),
            SchemaFormat.Yaml => AddYamlField(source, groupPath, name, dataType, sampleValue),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

        Parse(updated, format);
        return updated;
    }

    public static string AddGroup(
        string source,
        SchemaFormat format,
        IReadOnlyList<int> parentPath,
        string name)
    {
        if (String.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A group name is required.", nameof(name));
        }

        var updated = format switch
        {
            SchemaFormat.Json => AddJsonGroup(source, parentPath, name),
            SchemaFormat.Xml => AddXmlGroup(source, parentPath, name),
            SchemaFormat.Yaml => AddYamlGroup(source, parentPath, name),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

        Parse(updated, format);
        return updated;
    }

    private static SchemaDocument LoadJson(string source)
    {
        var builder = new SchemaSetBuilder();
        return builder.LoadFromJson(source);
    }

    private static SchemaDocument LoadXml(string source)
    {
        var builder = new SchemaSetBuilder();
        return builder.LoadFromXml(source);
    }

    private static string UpdateJsonNode(
        string source,
        IReadOnlyList<int> memberPath,
        string name,
        string dataType,
        string description,
        string sampleValue)
    {
        var root = JsonNode.Parse(source) as JsonObject
            ?? throw new JsonException("A schema JSON document must be an object.");
        var node = FindJsonNode(root, memberPath);
        node["name"] = name;
        if (String.Equals(node["kind"]?.GetValue<string>(), "field", StringComparison.OrdinalIgnoreCase))
        {
            node["datatype"] = dataType;
        }
        SetJsonProperty(node, "description", description);
        SetJsonProperty(node, "sample", sampleValue);
        return root.ToJsonString(JsonOptions);
    }

    private static string AddJsonField(string source, IReadOnlyList<int> groupPath, string name, string dataType, string sampleValue)
    {
        var root = JsonNode.Parse(source) as JsonObject
            ?? throw new JsonException("A schema JSON document must be an object.");
        var group = groupPath.Count == 0 ? FindFirstJsonGroup(root) : FindJsonNode(root, groupPath);
        EnsureJsonGroup(group);
        var members = group["members"] as JsonArray;
        if (members is null)
        {
            members = [];
            group["members"] = members;
        }

        var field = new JsonObject
        {
            ["kind"] = "field",
            ["name"] = name,
            ["datatype"] = dataType
        };
        SetJsonProperty(field, "sample", sampleValue);
        members.Add(field);
        return root.ToJsonString(JsonOptions);
    }

    private static JsonObject FindJsonNode(JsonObject root, IReadOnlyList<int> memberPath)
    {
        JsonNode current = root;
        foreach (var index in memberPath)
        {
            var members = current["members"] as JsonArray
                ?? throw new InvalidOperationException("The selected schema node has no members.");
            if (index < 0 || index >= members.Count)
            {
                throw new InvalidOperationException("The selected schema node no longer exists.");
            }
            current = members[index]
                ?? throw new InvalidOperationException("The selected schema node is empty.");
        }
        return current as JsonObject
            ?? throw new InvalidOperationException("The selected schema node must be an object.");
    }

    private static JsonObject FindFirstJsonGroup(JsonObject root)
    {
        var members = root["members"] as JsonArray;
        var group = members?.OfType<JsonObject>().FirstOrDefault(node =>
            !String.Equals(node["kind"]?.GetValue<string>(), "field", StringComparison.OrdinalIgnoreCase));
        return group ?? throw new InvalidOperationException("Add a group to the schema before adding fields.");
    }

    private static string AddJsonGroup(string source, IReadOnlyList<int> parentPath, string name)
    {
        var root = JsonNode.Parse(source) as JsonObject
            ?? throw new JsonException("A schema JSON document must be an object.");
        var parent = parentPath.Count == 0 ? FindFirstJsonGroup(root) : FindJsonNode(root, parentPath);
        EnsureJsonGroup(parent);
        var members = parent["members"] as JsonArray;
        if (members is null)
        {
            members = [];
            parent["members"] = members;
        }
        members.Add(new JsonObject
        {
            ["kind"] = "group",
            ["name"] = name,
            ["members"] = new JsonArray()
        });
        return root.ToJsonString(JsonOptions);
    }

    private static void EnsureJsonGroup(JsonObject node)
    {
        if (String.Equals(node["kind"]?.GetValue<string>(), "field", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Select a group to add a field.");
        }
    }

    private static void SetJsonProperty(JsonObject node, string name, string value)
    {
        var properties = node["properties"] as JsonArray;
        var existing = properties?.OfType<JsonObject>().FirstOrDefault(item =>
            String.Equals(item["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase));

        if (String.IsNullOrWhiteSpace(value))
        {
            if (existing is not null)
            {
                properties!.Remove(existing);
            }
            if (properties?.Count == 0)
            {
                node.Remove("properties");
            }
            return;
        }

        if (existing is null)
        {
            properties ??= [];
            if (node["properties"] is null)
            {
                node["properties"] = properties;
            }
            existing = new JsonObject { ["name"] = name };
            properties.Add(existing);
        }
        existing["value"] = value;
    }

    private static string UpdateXmlNode(
        string source,
        IReadOnlyList<int> memberPath,
        string name,
        string dataType,
        string description,
        string sampleValue)
    {
        var document = XDocument.Parse(source, LoadOptions.PreserveWhitespace);
        var node = FindXmlNode(document, memberPath);
        node.SetAttributeValue("name", name);
        if (node.Name.LocalName == "field")
        {
            node.SetAttributeValue("datatype", dataType);
        }
        SetXmlProperty(node, "description", description);
        SetXmlProperty(node, "sample", sampleValue);
        return FormatXml(document);
    }

    private static string AddXmlField(string source, IReadOnlyList<int> groupPath, string name, string dataType, string sampleValue)
    {
        var document = XDocument.Parse(source, LoadOptions.PreserveWhitespace);
        var group = groupPath.Count == 0 ? FindFirstXmlGroup(document) : FindXmlNode(document, groupPath);
        EnsureXmlGroup(group);
        var members = Child(group, "members");
        if (members is null)
        {
            members = new XElement("members");
            group.Add(members);
        }

        var field = new XElement("field",
            new XAttribute("name", name),
            new XAttribute("datatype", dataType));
        SetXmlProperty(field, "sample", sampleValue);
        members.Add(field);
        return FormatXml(document);
    }

    private static XElement FindXmlNode(XDocument document, IReadOnlyList<int> memberPath)
    {
        var current = document.Root
            ?? throw new InvalidOperationException("The XML schema has no root element.");
        foreach (var index in memberPath)
        {
            var members = Child(current, "members")?.Elements().ToList()
                ?? throw new InvalidOperationException("The selected schema node has no members.");
            if (index < 0 || index >= members.Count)
            {
                throw new InvalidOperationException("The selected schema node no longer exists.");
            }
            current = members[index];
        }
        return current;
    }

    private static XElement FindFirstXmlGroup(XDocument document)
    {
        var group = Child(document.Root ?? throw new InvalidOperationException("The XML schema has no root element."), "members")?
            .Elements()
            .FirstOrDefault(node => node.Name.LocalName != "field");
        return group ?? throw new InvalidOperationException("Add a group to the schema before adding fields.");
    }

    private static string AddXmlGroup(string source, IReadOnlyList<int> parentPath, string name)
    {
        var document = XDocument.Parse(source, LoadOptions.PreserveWhitespace);
        var parent = parentPath.Count == 0 ? FindFirstXmlGroup(document) : FindXmlNode(document, parentPath);
        EnsureXmlGroup(parent);
        var members = Child(parent, "members");
        if (members is null)
        {
            members = new XElement("members");
            parent.Add(members);
        }
        members.Add(new XElement("group",
            new XAttribute("name", name),
            new XElement("members")));
        return FormatXml(document);
    }

    private static void EnsureXmlGroup(XElement node)
    {
        if (node.Name.LocalName == "field")
        {
            throw new InvalidOperationException("Select a group to add a field.");
        }
    }

    private static void SetXmlProperty(XElement node, string name, string value)
    {
        var properties = Child(node, "properties");
        var property = properties?.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "property"
            && String.Equals((string?)element.Attribute("name"), name, StringComparison.OrdinalIgnoreCase));

        if (String.IsNullOrWhiteSpace(value))
        {
            property?.Remove();
            if (properties?.Elements().Any() == false)
            {
                properties.Remove();
            }
            return;
        }

        if (property is null)
        {
            properties ??= new XElement("properties");
            if (properties.Parent is null)
            {
                node.Add(properties);
            }
            property = new XElement("property", new XAttribute("name", name));
            properties.Add(property);
        }
        property.SetAttributeValue("value", value);
    }

    private static XElement? Child(XElement? element, string name)
        => element?.Elements().FirstOrDefault(child => child.Name.LocalName == name);

    private static string FormatXml(XDocument document)
    {
        var declaration = document.Declaration is null ? String.Empty : document.Declaration + Environment.NewLine;
        return declaration + document.ToString(SaveOptions.DisableFormatting);
    }

    private static string UpdateYamlNode(
        string source,
        IReadOnlyList<int> memberPath,
        string name,
        string dataType,
        string description,
        string sampleValue)
    {
        var root = LoadYaml(source);
        var node = FindYamlNode(root, memberPath);
        SetYamlValue(node, "name", name);
        if (String.Equals(GetYamlValueAsString(node, "kind"), "field", StringComparison.OrdinalIgnoreCase))
        {
            SetYamlValue(node, "datatype", dataType);
        }
        SetYamlProperty(node, "description", description);
        SetYamlProperty(node, "sample", sampleValue);
        return SaveYaml(root);
    }

    private static string AddYamlField(string source, IReadOnlyList<int> groupPath, string name, string dataType, string sampleValue)
    {
        var root = LoadYaml(source);
        var group = groupPath.Count == 0 ? FindFirstYamlGroup(root) : FindYamlNode(root, groupPath);
        EnsureYamlGroup(group);
        var members = GetYamlValue(group, "members") as YamlSequenceNode;
        if (members is null)
        {
            members = new YamlSequenceNode();
            SetYamlNode(group, "members", members);
        }

        var field = new YamlMappingNode
        {
            { "kind", "field" },
            { "name", name },
            { "datatype", dataType }
        };
        SetYamlProperty(field, "sample", sampleValue);
        members.Add(field);
        return SaveYaml(root);
    }

    private static YamlMappingNode LoadYaml(string source)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(source);
        stream.Load(reader);
        return stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode
            ?? throw new InvalidOperationException("A schema YAML document must be a mapping.");
    }

    private static YamlMappingNode FindYamlNode(YamlMappingNode root, IReadOnlyList<int> memberPath)
    {
        YamlMappingNode current = root;
        foreach (var index in memberPath)
        {
            var members = GetYamlValue(current, "members") as YamlSequenceNode
                ?? throw new InvalidOperationException("The selected schema node has no members.");
            if (index < 0 || index >= members.Children.Count)
            {
                throw new InvalidOperationException("The selected schema node no longer exists.");
            }
            current = members.Children[index] as YamlMappingNode
                ?? throw new InvalidOperationException("The selected schema node must be a mapping.");
        }
        return current;
    }

    private static YamlMappingNode FindFirstYamlGroup(YamlMappingNode root)
    {
        var members = GetYamlValue(root, "members") as YamlSequenceNode;
        var group = members?.Children.OfType<YamlMappingNode>().FirstOrDefault(node =>
            !String.Equals(GetYamlValueAsString(node, "kind"), "field", StringComparison.OrdinalIgnoreCase));
        return group ?? throw new InvalidOperationException("Add a group to the schema before adding fields.");
    }

    private static string AddYamlGroup(string source, IReadOnlyList<int> parentPath, string name)
    {
        var root = LoadYaml(source);
        var parent = parentPath.Count == 0 ? FindFirstYamlGroup(root) : FindYamlNode(root, parentPath);
        EnsureYamlGroup(parent);
        var members = GetYamlValue(parent, "members") as YamlSequenceNode;
        if (members is null)
        {
            members = new YamlSequenceNode();
            SetYamlNode(parent, "members", members);
        }
        members.Add(new YamlMappingNode
        {
            { "kind", "group" },
            { "name", name },
            { "members", new YamlSequenceNode() }
        });
        return SaveYaml(root);
    }

    private static void EnsureYamlGroup(YamlMappingNode node)
    {
        if (String.Equals(GetYamlValueAsString(node, "kind"), "field", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Select a group to add a field.");
        }
    }

    private static YamlNode? GetYamlValue(YamlMappingNode node, string key)
        => node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static string? GetYamlValueAsString(YamlMappingNode node, string key)
        => (GetYamlValue(node, key) as YamlScalarNode)?.Value;

    private static void SetYamlNode(YamlMappingNode node, string key, YamlNode value)
        => node.Children[new YamlScalarNode(key)] = value;

    private static void SetYamlValue(YamlMappingNode node, string key, string value)
        => SetYamlNode(node, key, new YamlScalarNode(value));

    private static void SetYamlProperty(YamlMappingNode node, string name, string value)
    {
        var properties = GetYamlValue(node, "properties") as YamlSequenceNode;
        YamlMappingNode? existing = properties?.Children.OfType<YamlMappingNode>().FirstOrDefault(item =>
            String.Equals(GetYamlValueAsString(item, "name"), name, StringComparison.OrdinalIgnoreCase));

        if (String.IsNullOrWhiteSpace(value))
        {
            if (existing is not null)
            {
                properties!.Children.Remove(existing);
            }
            if (properties?.Children.Count == 0)
            {
                node.Children.Remove(new YamlScalarNode("properties"));
            }
            return;
        }

        if (existing is null)
        {
            properties ??= new YamlSequenceNode();
            if (GetYamlValue(node, "properties") is null)
            {
                SetYamlNode(node, "properties", properties);
            }
            existing = new YamlMappingNode { { "name", name } };
            properties.Add(existing);
        }
        SetYamlValue(existing, "value", value);
    }

    private static string SaveYaml(YamlMappingNode root)
    {
        var stream = new YamlStream(new YamlDocument(root));
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }
}
