using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jacobi.Midi.Console.Core.Files;

/// <summary>
/// The JSON form of a <see cref="FileElement"/> tree. An element is an object of its attributes; its child elements
/// are grouped in properties by their <see cref="FileRole"/>. Elements of a role without element name (the controls)
/// give their name in <c>type</c>.
/// </summary>
internal static class JsonFile
{
    private const string TypeProperty = "type";

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static FileElement Read(string text, string rootName)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, ReadOptions);
        }
        catch (JsonException exception)
        {
            throw new FileFormatException($"Invalid JSON: {exception.Message}", exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new FileFormatException($"{rootName}: the file must hold a JSON object.");
            var root = new FileElement(rootName);
            ReadObject(document.RootElement, root, readsType: false);
            return root;
        }
    }

    public static string Write(FileElement root)
        => WriteObject(root, writesType: false).ToJsonString(WriteOptions);

    // reading

    private static void ReadObject(JsonElement json, FileElement element, bool readsType)
    {
        foreach (var property in json.EnumerateObject())
        {
            if (readsType && property.Name == TypeProperty)
                continue;
            if (FileRoles.TryGet(property.Name, out var role))
                ReadRole(property.Value, element, role);
            else
                element.Set(property.Name, ReadValue(property.Value, element, property.Name, out var kind), kind);
        }
    }

    private static void ReadRole(JsonElement json, FileElement parent, FileRole role)
    {
        switch (role.Shape)
        {
            case FileRoleShape.Single:
                parent.Add(ReadChild(Expect(json, JsonValueKind.Object, parent, role), parent, role));
                break;
            case FileRoleShape.List:
                foreach (var item in Expect(json, JsonValueKind.Array, parent, role).EnumerateArray())
                    parent.Add(ReadChild(Expect(item, JsonValueKind.Object, parent, role), parent, role));
                break;
            case FileRoleShape.Map:
                foreach (var entry in Expect(json, JsonValueKind.Object, parent, role).EnumerateObject())
                {
                    var value = ReadValue(entry.Value, parent, $"{role.JsonName}.{entry.Name}", out var kind);
                    parent.Add(new FileElement(role.ElementName!, role)
                        .Set(role.KeyAttribute!, entry.Name)
                        .Set(role.ValueAttribute!, value, kind));
                }
                break;
            case FileRoleShape.Values:
                foreach (var item in Expect(json, JsonValueKind.Array, parent, role).EnumerateArray())
                {
                    var value = ReadValue(item, parent, role.JsonName, out var kind);
                    parent.Add(new FileElement(role.ElementName!, role).Set(role.ValueAttribute!, value, kind));
                }
                break;
        }
    }

    private static FileElement ReadChild(JsonElement json, FileElement parent, FileRole role)
    {
        var name = role.ElementName;
        if (name is null)
        {
            name = json.TryGetProperty(TypeProperty, out var type) && type.ValueKind == JsonValueKind.String
                ? type.GetString()!
                : throw parent.Error($"'{role.JsonName}': every element needs a '{TypeProperty}'.");
        }
        var child = new FileElement(name, role);
        ReadObject(json, child, readsType: role.ElementName is null);
        return child;
    }

    private static string ReadValue(JsonElement json, FileElement element, string name, out FileValueKind kind)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.String:
                kind = FileValueKind.Text;
                return json.GetString()!;
            case JsonValueKind.Number:
                kind = FileValueKind.Number;
                return json.GetRawText();
            case JsonValueKind.True or JsonValueKind.False:
                kind = FileValueKind.Boolean;
                return json.ValueKind == JsonValueKind.True ? "true" : "false";
            case JsonValueKind.Array when json.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Number):
                kind = FileValueKind.NumberList;
                return String.Join(",", json.EnumerateArray().Select(item => item.GetRawText()));
            default:
                throw element.Error($"'{name}': a {json.ValueKind} is not a valid value here.");
        }
    }

    private static JsonElement Expect(JsonElement json, JsonValueKind kind, FileElement parent, FileRole role)
        => json.ValueKind == kind
            ? json
            : throw parent.Error($"'{role.JsonName}' must be a JSON {kind.ToString().ToLowerInvariant()}.");

    // writing

    private static JsonObject WriteObject(FileElement element, bool writesType)
    {
        var json = new JsonObject();
        if (writesType)
            json[TypeProperty] = element.Name;
        foreach (var attribute in element.Attributes)
            json[attribute.Name] = WriteValue(attribute);

        foreach (var group in element.Children.GroupBy(child => child.Role
            ?? throw new InvalidOperationException($"{child.Location}: an element without role cannot be written as JSON.")))
        {
            var role = group.Key;
            json[role.JsonName] = role.Shape switch
            {
                FileRoleShape.Single => WriteObject(group.Single(), role.ElementName is null),
                FileRoleShape.List => new JsonArray([.. group.Select(child => (JsonNode)WriteObject(child, role.ElementName is null))]),
                FileRoleShape.Map => new JsonObject(group.Select(child => KeyValuePair.Create<string, JsonNode?>(
                    child.Find(role.KeyAttribute!)!, WriteValue(child.Attributes.Single(attribute => attribute.Name == role.ValueAttribute))))),
                FileRoleShape.Values => new JsonArray([.. group.Select(child => WriteValue(child.Attributes.Single(attribute => attribute.Name == role.ValueAttribute)))]),
                _ => throw new InvalidOperationException($"Unknown role shape {role.Shape}."),
            };
        }
        return json;
    }

    private static JsonNode WriteValue(FileAttribute attribute)
        => attribute.Kind switch
        {
            FileValueKind.Number => JsonValue.Create(Decimal.Parse(attribute.Value, NumberStyles.Number, CultureInfo.InvariantCulture)),
            FileValueKind.Boolean => JsonValue.Create(Boolean.Parse(attribute.Value)),
            FileValueKind.NumberList => new JsonArray([.. attribute.Value.Split(',')
                .Select(item => (JsonNode)JsonValue.Create(Decimal.Parse(item, NumberStyles.Number, CultureInfo.InvariantCulture)))]),
            _ => JsonValue.Create(attribute.Value),
        };
}
