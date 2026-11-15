using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Device.PropertyExchange;

/// <summary>
/// Gives the JSON data of a resource to a value sink along the plan of its <c>pe</c> root, by the rules of the
/// generated schema (<see cref="PropertySchemaGenerator"/>): a group takes the members of an object by their key
/// (<c>midi.key</c>, else the name), a repeat the elements of an array, a field a value converted to the type of the
/// field. A root of a resource that is an array or a value has one member, which takes the data itself. A key the data
/// does not have gives no value; a value of the wrong kind is an error.
/// </summary>
internal static class PropertyData
{
    /// <summary>Gives the data to the sink, as a read of the whole root.</summary>
    /// <exception cref="FormatException">A value does not fit its field.</exception>
    public static void CopyTo(GroupInfo root, JsonNode? data, IValueSink sink)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(sink);
        // a resource that is an array or a single value: its one member takes the data
        if (data is not JsonObject && root.Members is [var only])
            Member(only, data, sink, InstancePath.Empty);
        else
            Members(root, data, sink, InstancePath.Empty);
    }

    private static void Members(GroupInfo group, JsonNode? data, IValueSink sink, InstancePath instance)
    {
        if (data is not JsonObject json)
            return;
        foreach (var member in group.Members)
        {
            var key = MidiProperties.Find(member, MidiProperties.Key) ?? member.Name;
            if (json.TryGetPropertyValue(key, out var value))
                Member(member, value, sink, instance);
        }
    }

    private static void Member(NodeInfo member, JsonNode? data, IValueSink sink, InstancePath instance)
    {
        switch (member)
        {
            case FieldInfo field:
                if (data is not null)
                    sink.SetField(new FieldContext { Node = field, Services = NoServices.Instance, Instance = instance }, ToLogical(field, data));
                break;

            case RepeatInfo repeat:
                if (data is not JsonArray items)
                    break;
                var context = new RepeatContext { Node = repeat, Services = NoServices.Instance, Instance = instance };
                for (var index = 0; index < items.Count; index++)
                {
                    var item = sink.EnterItem(context, index, items.Count);
                    var itemInstance = instance.Append(index);
                    // an array of values: the item's one field takes the element
                    if (items[index] is not JsonObject && repeat.Members is [FieldInfo element])
                        Member(element, items[index], item, itemInstance);
                    else
                        Members(repeat, items[index], item, itemInstance);
                    item.ExitItem(context, index, items.Count);
                }
                break;

            case GroupInfo group:
                var groupContext = new GroupContext { Node = group, Services = NoServices.Instance, Instance = instance };
                var scope = sink.EnterGroup(groupContext);
                Members(group, data, scope, instance);
                scope.ExitGroup(groupContext);
                break;
        }
    }

    // the JSON value as the logical value of the field
    private static LogicalField ToLogical(FieldInfo field, JsonNode data)
    {
        var type = field.ValueType.ClrType;
        try
        {
            object? value = data is JsonValue json && json.GetValueKind() is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                ? type == typeof(string) ? data.ToString()
                : type == typeof(bool) ? json.GetValue<bool>()
                : Convert.ChangeType(json.GetValue<decimal>(), type, CultureInfo.InvariantCulture)
                : throw new FormatException($"'{data.ToJsonString()}' is not a value.");
            return new LogicalField(field.Name, type, value);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidCastException or OverflowException or FormatException)
        {
            throw new FormatException($"'{field.Path}': '{data.ToJsonString()}' is not a {type.Name}.", exception);
        }
    }

    // the plan walk of the serializer gives its services; the data of a resource needs none
    private sealed class NoServices : IServiceProvider
    {
        public static NoServices Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}
