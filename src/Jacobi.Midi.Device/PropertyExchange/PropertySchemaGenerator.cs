using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Device.PropertyExchange;

/// <summary>
/// A schema document generated for the Property Exchange resources of a device, and what could not be generated
/// (a resource without a schema, a construct the generator does not support), so nothing is left out silently.
/// </summary>
/// <param name="Document">The schema document: one root (<c>midi.role="pe"</c>) per resource that has a schema.</param>
/// <param name="Notes">What was left out, and why.</param>
public sealed record PropertySchema(SchemaDocument Document, IReadOnlyList<string> Notes);

/// <summary>
/// Generates a schema document from what a Property Exchange device describes: its <c>ResourceList</c> and the JSON
/// Schemas of its resources. Each JSON resource with a schema becomes a root group with <c>midi.role="pe"</c>, named
/// after the resource, so the resource works with plan ranges like any schema root. The document is never serialized:
/// it gives the structure, the paths and the value domains.
/// <list type="bullet">
/// <item>a resource with entries (<c>resId</c>s, found in the links of the device's data) has a choice <c>resId</c>
/// without a selected index, with a group per <c>resId</c> that has the structure of the resource (a nodeDef
/// <c>&lt;root&gt;_entry</c>);</item>
/// <item>an <c>object</c> is a group of its <c>properties</c>, in their order; an <c>array</c> a repeat (with a count
/// when <c>minItems</c> equals <c>maxItems</c>) of the properties of its <c>items</c> object, or of one field
/// <c>item</c>; a resource that is an array is a root with the repeat <c>items</c>, a resource with a single value a root
/// with the field <c>value</c>;</item>
/// <item><c>integer</c> is <c>sys.int64</c>, <c>number</c> <c>sys.decimal</c>, <c>string</c> <c>sys.string</c>,
/// <c>boolean</c> <c>sys.bool</c>; <c>minimum</c>/<c>maximum</c> and <c>enum</c> make a data type def (facets,
/// options);</item>
/// <item>a name that is not a valid schema name is made one, and the JSON key is kept in <c>midi.key</c>;
/// <c>description</c> (else <c>title</c>) is the <c>midi.doc</c>.</item>
/// </list>
/// </summary>
public static class PropertySchemaGenerator
{
    /// <summary>The name of the field of an array element that is a single value.</summary>
    public const string ItemName = "item";
    /// <summary>The name of the repeat of a resource that is an array.</summary>
    public const string ItemsName = "items";
    /// <summary>The name of the field of a resource that is a single value.</summary>
    public const string ValueName = "value";
    /// <summary>The name of the choice of a resource with entries: an alternative per <c>resId</c>.</summary>
    public const string ResIdName = "resId";

    /// <summary>Generates the document for the resources.</summary>
    /// <param name="documentName">The name of the document (a valid schema name, e.g. after the device).</param>
    /// <param name="resources">The entries of the device's <c>ResourceList</c>.</param>
    /// <param name="referencedSchemas">The JSON Schemas the entries refer to (<see cref="PropertyResource.SchemaReference"/>),
    /// by id, as the device's <c>JSONSchema</c> resource gives them.</param>
    /// <param name="resourceIds">The <c>resId</c>s of the resources, by resource name, as the links in the device's data
    /// give them; a resource with <c>resId</c>s gets the choice <c>resId</c>.</param>
    public static PropertySchema Generate(string documentName, IEnumerable<PropertyResource> resources,
        IReadOnlyDictionary<string, JsonObject>? referencedSchemas = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? resourceIds = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentName);
        ArgumentNullException.ThrowIfNull(resources);

        var builder = new SchemaDocumentBuilder(documentName);
        var notes = new List<string>();
        var roots = new HashSet<string>(StringComparer.Ordinal);
        builder.Members(members =>
        {
            foreach (var resource in resources)
            {
                if (!resource.IsJson)
                {
                    notes.Add($"'{resource.Name}': its data is not JSON ({String.Join(", ", resource.MediaTypes)}).");
                    continue;
                }
                if (SchemaOf(resource, referencedSchemas, notes) is not { } schema)
                    continue;

                var root = Unique(NameOf(resource.Name), roots);
                var generator = new Generator(builder, root, notes);
                generator.Root(members, root, resource, schema, resourceIds?.GetValueOrDefault(resource.Name) ?? []);
            }
        });
        return new PropertySchema(builder.Build(), notes);
    }

    // the schema of a resource: given in full, or referred to by id; null (with a note) when there is none
    private static JsonObject? SchemaOf(PropertyResource resource, IReadOnlyDictionary<string, JsonObject>? referenced, List<string> notes)
    {
        if (resource.SchemaReference is { } id)
        {
            if (referenced?.GetValueOrDefault(id) is { } schema)
                return schema;
            notes.Add($"'{resource.Name}': its JSON Schema '{id}' (JSONSchema resource) is not given.");
            return null;
        }
        if (resource.Schema is null)
        {
            // a resource of the specifications: its published schema
            if (StandardSchemas.Find(resource.Name) is { } standard)
                return standard;
            notes.Add($"'{resource.Name}': the ResourceList gives no JSON Schema, and it has no published one.");
            return null;
        }
        return resource.Schema;
    }

    // a valid schema name: letters, digits and '_', not starting with a digit
    internal static string NameOf(string key)
    {
        var name = new StringBuilder();
        foreach (var c in key)
        {
            if (Char.IsAsciiLetterOrDigit(c) || c == '_')
                name.Append(c);
        }
        if (name.Length == 0 || Char.IsAsciiDigit(name[0]))
            name.Insert(0, '_');
        return name.ToString();
    }

    private static string Unique(string name, HashSet<string> used)
    {
        var unique = name;
        for (var number = 2; !used.Add(unique); number++)
            unique = $"{name}{number.ToString(CultureInfo.InvariantCulture)}";
        return unique;
    }

    // the nodes of one resource; the data type defs are named after the root and the path
    private sealed class Generator(SchemaDocumentBuilder document, string root, List<string> notes)
    {
        public void Root(SchemaDocumentBuilder.GroupBuilder members, string name, PropertyResource resource, JsonObject schema, IReadOnlyList<string> resIds)
        {
            if (TypeOf(schema, resource.Name) is not { } type)
                return;

            var note = resIds.Count == 0 && resource.RequireResId
                ? $"'{resource.Name}' requires a resId, but no link in the data of the device refers to one: add a choice '{ResIdName}' with a group per resId that has the structure of this root."
                : null;
            List<SchemaProperty> properties =
            [
                new() { Name = MidiProperties.Role, Value = MidiProperties.RolePropertyExchange },
                .. Access(resource),
                .. Tags(resource.Name, name, schema, note),
            ];
            if (resIds.Count == 0)
            {
                members.Group(name, group => Content(group, name, schema, type), properties: properties);
                return;
            }

            // the structure once, as a nodeDef; a group per resId that has it
            var entry = $"{name}_entry";
            document.NodeDefs(nodes => nodes.Group(entry, group => Content(group, name, schema, type)));
            var doc = $"The entries of '{resource.Name}' (resId) that links in the data of the device refer to."
                + (resource.RequireResId ? String.Empty : " The resource can also be read without a resId: remove this choice for that.");
            members.Group(name, group => group.Choice(ResIdName, default, choice =>
            {
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (var resId in resIds)
                {
                    var alternative = Unique(NameOf(resId), used);
                    choice.Group(alternative, nodeDef: entry, properties: alternative == resId ? [] : [new() { Name = MidiProperties.Key, Value = resId }]);
                }
            }, properties: [new() { Name = MidiProperties.Doc, Value = doc }]), properties: properties);
        }

        // the structure of a resource: the properties of an object, the repeat of an array, or the field of a value
        private void Content(SchemaDocumentBuilder.GroupBuilder group, string name, JsonObject schema, string type)
        {
            switch (type)
            {
                case "object":
                    Properties(group, schema, name);
                    break;
                case "array":
                    Array(group, ItemsName, ItemsName, schema, $"{name}.{ItemsName}");
                    break;
                default:
                    Value(group, ValueName, ValueName, schema, type, $"{name}.{ValueName}");
                    break;
            }
        }

        private void Properties(SchemaDocumentBuilder.GroupBuilder group, JsonObject schema, string path)
        {
            if (schema["properties"] is not JsonObject properties || properties.Count == 0)
            {
                notes.Add($"'{path}': the object has no properties in its JSON Schema (its structure would come from the data).");
                return;
            }
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (key, node) in properties)
            {
                var name = Unique(NameOf(key), used);
                if (node is not JsonObject property)
                {
                    notes.Add($"'{path}.{name}': the schema of '{key}' is not an object.");
                    continue;
                }
                Member(group, name, key, property, $"{path}.{name}");
            }
        }

        private void Member(SchemaDocumentBuilder.GroupBuilder group, string name, string key, JsonObject schema, string path)
        {
            switch (TypeOf(schema, path))
            {
                case "object":
                    group.Group(name, members => Properties(members, schema, path), properties: Tags(key, name, schema));
                    break;
                case "array":
                    Array(group, name, key, schema, path);
                    break;
                case { } type:
                    Value(group, name, key, schema, type, path);
                    break;
            }
        }

        private void Array(SchemaDocumentBuilder.GroupBuilder group, string name, string key, JsonObject schema, string path)
        {
            var count = Integer(schema, "minItems") is { } min && Integer(schema, "maxItems") == min ? (SchemaValueOrRef<int>)(int)min : default;
            if (schema["items"] is not JsonObject items)
            {
                notes.Add($"'{path}': the array has no 'items' schema.");
                return;
            }
            var properties = Tags(key, name, schema);
            if (TypeOf(items, path) is "object")
                group.Repeat(name, count, members => Properties(members, items, path), properties: properties);
            else if (TypeOf(items, path) is { } type)
                group.Repeat(name, count, members => Value(members, ItemName, ItemName, items, type, $"{path}.{ItemName}"), properties: properties);
        }

        private void Value(SchemaDocumentBuilder.GroupBuilder group, string name, string key, JsonObject schema, string type, string path)
        {
            var basedOn = type switch
            {
                "integer" => "sys.int64",
                "number" => "sys.decimal",
                "string" => "sys.string",
                "boolean" => "sys.bool",
                _ => null,
            };
            if (basedOn is null)
            {
                notes.Add($"'{path}': the type '{type}' is not supported.");
                return;
            }

            var options = schema["enum"] is JsonArray values
                ? values.Select(value => value?.ToString()).OfType<string>().Distinct().ToDictionary(value => value, value => value)
                : null;
            var min = Number(schema, "minimum");
            var max = Number(schema, "maximum");
            var dataType = basedOn;
            if (options is not null || min is not null || max is not null)
            {
                // a data type of its own, named after the path in the document
                dataType = $"{root}_{String.Join('_', path.Split('.').Skip(1))}";
                document.DataTypeDef(dataType, basedOn, min: options is null ? min : null, max: options is null ? max : null, options: options);
            }
            group.Field(name, dataType, properties: Tags(key, name, schema));
        }

        // the type of a schema: one type, or one besides "null"; null (with a note) otherwise
        private string? TypeOf(JsonObject schema, string path)
        {
            switch (schema["type"])
            {
                case JsonValue value when value.TryGetValue<string>(out var type):
                    return type;
                case JsonArray types when types.Select(type => type?.GetValue<string>()).Where(type => type != "null").ToList() is [var single]:
                    return single;
                case null when schema["$ref"] is not null:
                    notes.Add($"'{path}': a '$ref' inside a JSON Schema is not supported.");
                    return null;
                default:
                    notes.Add($"'{path}': the JSON Schema has no single 'type'.");
                    return null;
            }
        }

        // a resource that cannot be set (canSet "none") is read-only; else the default readwrite (no tag)
        private static IEnumerable<SchemaProperty> Access(PropertyResource resource)
            => String.Equals(resource.CanSet, PropertyResource.CanSetNone, StringComparison.OrdinalIgnoreCase)
                ? [new() { Name = MidiProperties.Access, Value = MidiProperties.AccessRead }]
                : [];

        // the JSON key when the node got another name, and the documentation (with a note of the generator)
        private static List<SchemaProperty> Tags(string key, string name, JsonObject schema, string? note = null)
            => name == key ? [.. Doc(schema, note)] : [new() { Name = MidiProperties.Key, Value = key }, .. Doc(schema, note)];

        private static IEnumerable<SchemaProperty> Doc(JsonObject schema, string? note = null)
        {
            var doc = String.Join(" ", new[] { (schema["description"] ?? schema["title"])?.GetValue<string>(), note }.Where(text => !String.IsNullOrEmpty(text)));
            return doc.Length > 0 ? [new() { Name = MidiProperties.Doc, Value = doc }] : [];
        }

        private static long? Integer(JsonObject schema, string key)
            => schema[key] is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;

        private static string? Number(JsonObject schema, string key)
            => schema[key] is JsonValue value && value.TryGetValue<decimal>(out var number) ? number.ToString(CultureInfo.InvariantCulture) : null;
    }
}
