using System.Globalization;
using System.Text;
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
/// A resource with entries has the choice <c>resId</c> as the one member of its root: the data of an entry goes to its
/// alternative. The reverse, for a Set: the data of a block from the values of a model (<see cref="ToJson(GroupInfo,
/// Func{FieldInfo, InstancePath, LogicalField?}, Func{RepeatInfo, InstancePath, int?})"/>) and the JSON Pointer of a field
/// occurrence (<see cref="PointerOf"/>).
/// </summary>
internal static class PropertyData
{
    /// <summary>
    /// The name the device knows a node by: the resource of a root, the <c>resId</c> of an entry, the key of a member
    /// (<c>midi.key</c>, else the name of the node).
    /// </summary>
    public static string KeyOf(NodeInfo node) => MidiProperties.Find(node, MidiProperties.Key) ?? node.Name;

    /// <summary>The choice <c>resId</c> of a root of a resource with entries; null for a resource without entries.</summary>
    public static ChoiceInfo? EntriesOf(GroupInfo root)
        => root.Members is [ChoiceInfo { Name: PropertySchemaGenerator.ResIdName } entries] ? entries : null;

    /// <summary>Gives the data to the sink, as a read of the whole root.</summary>
    /// <exception cref="FormatException">A value does not fit its field.</exception>
    public static void CopyTo(GroupInfo root, JsonNode? data, IValueSink sink)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(sink);
        Frame(root, null, sink, (group, scope) => Content(group, data, scope));
    }

    /// <summary>
    /// Gives the data of one entry to the sink, as a read of the root with that alternative of the choice <c>resId</c>.
    /// The entry is known from the request, so the choice is entered with its index (as a choice with a selected index).
    /// </summary>
    /// <exception cref="FormatException">A value does not fit its field.</exception>
    public static void CopyTo(GroupInfo root, GroupInfo entry, JsonNode? data, IValueSink sink)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(sink);
        Frame(root, entry, sink, (group, scope) => Content(group, data, scope));
    }

    /// <summary>
    /// Gives a block of a root to the sink as a read of the root: the root itself (<paramref name="entry"/> null), or one
    /// entry as the selected alternative of the choice <c>resId</c> (the entry is known from the request, so the choice is
    /// entered with its index). <paramref name="content"/> gives the members of the root or the entry to its scope.
    /// </summary>
    public static void Frame(GroupInfo root, GroupInfo? entry, IValueSink sink, Action<GroupInfo, IValueSink> content)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(content);
        if (entry is null)
        {
            content(root, sink);
        }
        else
        {
            if (EntriesOf(root) is not { } entries || !entries.Members.Contains(entry))
                throw new ArgumentException($"'{entry.Path}' is not an entry (resId) of '{root.Path}'.", nameof(entry));

            var choiceContext = new ChoiceContext { Node = entries, Services = NoServices.Instance, Instance = InstancePath.Empty };
            var choice = sink.EnterChoice(choiceContext, entry.Index);
            var entryContext = new GroupContext { Node = entry, Services = NoServices.Instance, Instance = InstancePath.Empty };
            var scope = choice.EnterGroup(entryContext);
            content(entry, scope);
            scope.ExitGroup(entryContext);
            choice.ExitChoice(choiceContext, entry.Index);
        }
        sink.ExitGroup(new GroupContext { Node = root, Services = NoServices.Instance, Instance = InstancePath.Empty });
    }

    // the data of a resource (or of an entry): a resource that is an array or a single value has one member, which takes the data
    private static void Content(GroupInfo group, JsonNode? data, IValueSink sink)
    {
        if (data is not JsonObject && group.Members is [var only])
            Member(only, data, sink, InstancePath.Empty);
        else
            Members(group, data, sink, InstancePath.Empty);
    }

    private static void Members(GroupInfo group, JsonNode? data, IValueSink sink, InstancePath instance)
    {
        if (data is not JsonObject json)
            return;
        foreach (var member in group.Members)
        {
            if (json.TryGetPropertyValue(KeyOf(member), out var value))
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
                // the data gives the number of items
                var repeatScope = sink.EnterRepeat(context, items.Count);
                for (var index = 0; index < items.Count; index++)
                {
                    var item = repeatScope.EnterItem(context, index, items.Count);
                    var itemInstance = instance.Append(index);
                    // an array of values: the item's one field takes the element
                    if (items[index] is not JsonObject && repeat.Members is [FieldInfo element])
                        Member(element, items[index], item, itemInstance);
                    else
                        Members(repeat, items[index], item, itemInstance);
                    item.ExitItem(context, index, items.Count);
                }
                repeatScope.ExitRepeat(context, items.Count);
                break;

            case GroupInfo group:
                var groupContext = new GroupContext { Node = group, Services = NoServices.Instance, Instance = instance };
                var scope = sink.EnterGroup(groupContext);
                Members(group, data, scope, instance);
                scope.ExitGroup(groupContext);
                break;
        }
    }

    /// <summary>
    /// The JSON data of a block (a root, or an entry of the choice <c>resId</c>) from the values of a model, as a Set of
    /// the whole resource sends it: the reverse of <see cref="CopyTo(GroupInfo, JsonNode?, IValueSink)"/>. A group is an
    /// object of its members by key, a repeat an array (of values, when its one member is the field <c>item</c>), a field
    /// its value; a field without a value and a repeat without a number of items are left out. A block whose one member
    /// is the repeat <c>items</c> or the field <c>value</c> is a resource that is an array or a value: its data is that
    /// member.
    /// </summary>
    /// <param name="value">The value of a field occurrence; null when the model has none.</param>
    /// <param name="count">The number of items of a repeat occurrence; null when the model has none.</param>
    public static JsonNode? ToJson(GroupInfo block, Func<FieldInfo, InstancePath, LogicalField?> value, Func<RepeatInfo, InstancePath, int?> count)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(count);
        return OnlyMember(block) is { } only
            ? Node(only, InstancePath.Empty, value, count)
            : Object(block, InstancePath.Empty, value, count);
    }

    /// <summary>
    /// The JSON Pointer of a field occurrence in the data of its block (a root, or an entry of the choice <c>resId</c>),
    /// as a partial Set addresses it (M2-103 section 10), e.g. <c>/pitchEnvelope/rates/0</c>.
    /// </summary>
    public static string PointerOf(GroupInfo block, FieldInfo field, InstancePath instance)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(field);
        var chain = new List<NodeInfo>();
        for (NodeInfo? node = field; node is not null && node != block; node = node.Parent)
        {
            chain.Add(node);
        }
        chain.Reverse();

        var only = OnlyMember(block);
        var pointer = new StringBuilder();
        var depth = 0;
        foreach (var node in chain)
        {
            // the one member of a block that is an array or a value, and the field of an array of values, are the data itself
            var isData = node == only || (node is FieldInfo && node.Parent is RepeatInfo parent && IsValueArray(parent));
            if (!isData)
            {
                pointer.Append('/').Append(KeyOf(node).Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal));
            }
            if (node is RepeatInfo)
            {
                pointer.Append('/').Append(instance[depth++].ToString(CultureInfo.InvariantCulture));
            }
        }
        return pointer.ToString();
    }

    /// <summary>The JSON value of a logical value.</summary>
    /// <exception cref="FormatException">The value has no JSON form.</exception>
    public static JsonNode? ToJson(FieldInfo field, LogicalField value)
    {
        ArgumentNullException.ThrowIfNull(field);
        return value.Value switch
        {
            null => null,
            string text => JsonValue.Create(text),
            bool flag => JsonValue.Create(flag),
            sbyte or byte or short or ushort or int or uint or long => JsonValue.Create(Convert.ToInt64(value.Value, CultureInfo.InvariantCulture)),
            ulong number => JsonValue.Create(number),
            float or double or decimal => JsonValue.Create(Convert.ToDecimal(value.Value, CultureInfo.InvariantCulture)),
            var other => throw new FormatException($"'{field.Path}': a {other.GetType().Name} has no JSON value."),
        };
    }

    // the one member of a block of a resource that is an array (the repeat items) or a value (the field value); null for an object
    private static NodeInfo? OnlyMember(GroupInfo block)
        => block.Members is [var only]
            && only is RepeatInfo { Name: PropertySchemaGenerator.ItemsName } or FieldInfo { Name: PropertySchemaGenerator.ValueName }
            && MidiProperties.Find(only, MidiProperties.Key) is null
            ? only : null;

    // an array of values: the one member of its items is the field item
    private static bool IsValueArray(RepeatInfo repeat)
        => repeat.Members is [FieldInfo { Name: PropertySchemaGenerator.ItemName } element] && MidiProperties.Find(element, MidiProperties.Key) is null;

    private static JsonObject Object(GroupInfo group, InstancePath instance, Func<FieldInfo, InstancePath, LogicalField?> value, Func<RepeatInfo, InstancePath, int?> count)
    {
        var json = new JsonObject();
        foreach (var member in group.Members)
        {
            if (member is not ChoiceInfo && Node(member, instance, value, count) is { } node)
                json[KeyOf(member)] = node;
        }
        return json;
    }

    // a node occurrence as JSON; null when it has no data (no value, no number of items)
    private static JsonNode? Node(NodeInfo node, InstancePath instance, Func<FieldInfo, InstancePath, LogicalField?> value, Func<RepeatInfo, InstancePath, int?> count)
    {
        switch (node)
        {
            case FieldInfo field:
                return value(field, instance) is { } logical ? ToJson(field, logical) : null;

            case RepeatInfo repeat:
                if ((repeat.Count is int fixedCount ? fixedCount : count(repeat, instance)) is not int items)
                    return null;
                var array = new JsonArray();
                for (var index = 0; index < items; index++)
                {
                    var itemInstance = instance.Append(index);
                    array.Add(IsValueArray(repeat)
                        ? Node(repeat.Members[0], itemInstance, value, count)
                        : Object(repeat, itemInstance, value, count));
                }
                return array;

            case GroupInfo group:
                return Object(group, instance, value, count);

            default:
                return null;
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
    internal sealed class NoServices : IServiceProvider
    {
        public static NoServices Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}
