using System.Globalization;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// The <c>midi.*</c> schema properties (tags) of the Device API as a tool sees them (e.g. the Schema Editor): their
/// names and descriptions, the check of a tag's value on its own and the role and documentation of a node (of a schema
/// or a plan); and
/// helpers to read them from schema and plan nodes. The checks that need the whole map and its envelopes are done by
/// <see cref="MessageMapValidator"/>.
/// </summary>
public static class MidiProperties
{
    private const string Prefix = "midi.";
    private const string TextType = "sys.string";

    /// <summary>
    /// The absolute start address of an addressed node (address map), in address notation.
    /// </summary>
    public const string Address = "midi.address";
    /// <summary>
    /// The number of addresses an addressed node occupies (address map); on a repeat the size of one item.
    /// </summary>
    public const string Stride = "midi.stride";
    /// <summary>
    /// The number of items a repeat has in the address space (address map). Only on repeats.
    /// </summary>
    public const string Repeat = "midi.repeat";
    /// <summary>
    /// How a group or repeat is cut into blocks: <c>atomic</c> (always whole) or <c>individual</c> (each member, or
    /// repeat item, on its own) (address map).
    /// </summary>
    public const string Transfer = "midi.transfer";
    /// <summary>
    /// The access of an addressed node: <c>readwrite</c> (default), <c>read</c> or <c>write</c> (address map, inherited).
    /// </summary>
    public const string Access = "midi.access";
    /// <summary>
    /// The prefix of <c>midi.device.&lt;name&gt;</c>: a device value fixed for the device type, on the map root.
    /// </summary>
    public const string DevicePrefix = "midi.device.";
    /// <summary>
    /// What one message covers: <c>range</c> (default), <c>field</c> or <c>atomic</c> (envelope, overridable on the map root).
    /// </summary>
    public const string Unit = "midi.unit";
    /// <summary>
    /// The largest number of data bytes in one message (envelope, overridable on the map root).
    /// </summary>
    public const string MaxDataSize = "midi.maxDataSize";
    /// <summary>
    /// Which value an envelope field or open choice holds: <c>address</c>, <c>size</c>, <c>device.&lt;name&gt;</c> or <c>data</c>.
    /// </summary>
    public const string Role = "midi.role";
    /// <summary>
    /// The bits per address byte of the <c>address</c> field (envelope); default 7.
    /// </summary>
    public const string AddressBits = "midi.addressBits";
    /// <summary>
    /// The documentation of the node it is on: plain text, possibly multi-line (any node, not inherited).
    /// </summary>
    public const string Doc = "midi.doc";

    // values of midi.role

    /// <summary>
    /// The role of the field that holds the transferred block's absolute start address.
    /// </summary>
    public const string RoleAddress = "address";
    /// <summary>
    /// The role of the field that holds the transferred block's size (request only).
    /// </summary>
    public const string RoleSize = "size";
    /// <summary>
    /// The role of the open choice that holds the values of the block.
    /// </summary>
    public const string RoleData = "data";
    /// <summary>
    /// The prefix of the <c>device.&lt;name&gt;</c> role: a field that holds a device value (<see cref="DevicePrefix"/>).
    /// </summary>
    public const string RoleDevicePrefix = "device.";

    /// <summary>
    /// The tags with a fixed name (a device value, <see cref="DevicePrefix"/>, has a name of its own).
    /// </summary>
    public static IReadOnlyList<PropertyDescriptor> Descriptors { get; } =
    [
        Describe(Address, "Map: the absolute start address of the node, in wire form (e.g. 0x40-00)."),
        Describe(Stride, "Map: the number of addresses the node (or one repeat item) occupies."),
        Describe(Repeat, "Map: the number of items of a repeat in the address space."),
        Describe(Transfer, "Map: how a group or repeat is cut into blocks: atomic (always whole) or individual (each member or item on its own)."),
        Describe(Access, "Map: readwrite (default), read or write; inherited."),
        Describe(Unit, "Message or map root: what one message covers: range (default), field or atomic."),
        Describe(MaxDataSize, "Message or map root: the largest number of data bytes in one message."),
        Describe(Role, "Envelope: the value the node holds: address, size, data or device.<name>."),
        Describe(AddressBits, "Envelope: the bits per address byte of the address field (default 7)."),
        Describe(Doc, "Any node: the documentation of the node, plain text (multi-line); not inherited."),
    ];

    /// <summary>
    /// True when <paramref name="name"/> is in the namespace of the tags: <c>midi.&lt;name&gt;</c> or <c>midi.device.&lt;name&gt;</c>.
    /// </summary>
    /// <remarks>Longer names (<c>midi.&lt;processor&gt;.&lt;property&gt;</c>) are properties of a <c>midi</c> processor, not tags.</remarks>
    public static bool IsTag(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase)
            || (name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && name.IndexOf(SchemaPath.Separator, Prefix.Length) < 0);
    }

    /// <summary>
    /// Checks a tag and its value on their own; null when valid. The rules that relate tags (e.g. a device value is fixed
    /// by the map root) are checked by <see cref="MessageMapValidator"/>.
    /// </summary>
    public static string? Validate(string name, string? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        name = name.Trim();
        value = value?.Trim();
        if (name.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return name.Length == DevicePrefix.Length
                ? $"'{name}' needs the name of the device value, e.g. '{DevicePrefix}modelId'."
                : IsNumber(value) ? null : $"'{name}' must be a number, not '{value}'.";
        }
        if (Descriptors.FirstOrDefault(d => String.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } descriptor)
        {
            return $"'{name}' is not a midi property.";
        }

        return descriptor.Name switch
        {
            Address or Stride or MaxDataSize or AddressBits or Repeat
                => IsNumber(value) ? null : $"'{name}' must be a number, not '{value}'.",
            Transfer
                => Enum.TryParse<NodeTransfer>(value, ignoreCase: true, out _) ? null : $"'{name}' must be atomic or individual, not '{value}'.",
            Access
                => Enum.TryParse<NodeAccess>(value, ignoreCase: true, out _) ? null : $"'{name}' must be readwrite, read or write, not '{value}'.",
            Unit
                => Enum.TryParse<AddressUnit>(value, ignoreCase: true, out _) ? null : $"'{name}' must be range, field or atomic, not '{value}'.",
            Role
                => IsRole(value) ? null : $"'{name}' must be {RoleAddress}, {RoleSize}, {RoleData} or {RoleDevicePrefix}<name>, not '{value}'.",
            _ => null,
        };
    }

    /// <summary>
    /// The role of a node (<c>midi.role</c>); null when it has none.
    /// </summary>
    public static string? RoleOf(SchemaNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Find(node, Role);
    }

    /// <summary>
    /// True when the node holds the data of a message (<c>midi.role="data"</c>, an open choice).
    /// </summary>
    public static bool IsData(SchemaNode node)
        => String.Equals(RoleOf(node), RoleData, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The documentation of a node (<c>midi.doc</c>); null when it has none. Only the node's own: not inherited.
    /// </summary>
    public static string? DocOf(SchemaNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Find(node, Doc);
    }

    /// <summary>
    /// The documentation of a node of a plan (<c>midi.doc</c>): a field, group, repeat or choice; null when it has none.
    /// Only the node's own: not inherited. A doc on a nodeDef is the doc of every node that uses it, unless the node
    /// gives its own.
    /// </summary>
    public static string? DocOf(NodeInfo node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Find(node, Doc);
    }

    /// <summary>
    /// True for a valid role: <c>address</c>, <c>size</c>, <c>data</c> or <c>device.&lt;name&gt;</c>.
    /// </summary>
    internal static bool IsRole(string? role)
        => role is not null
        && (String.Equals(role, RoleAddress, StringComparison.OrdinalIgnoreCase)
            || String.Equals(role, RoleSize, StringComparison.OrdinalIgnoreCase)
            || String.Equals(role, RoleData, StringComparison.OrdinalIgnoreCase)
            || (role.StartsWith(RoleDevicePrefix, StringComparison.OrdinalIgnoreCase) && role.Length > RoleDevicePrefix.Length));

    private static bool IsNumber(string? value) => TryParseNumber(value, out _);

    private static PropertyDescriptor Describe(string name, string description)
        => new(name, new SchemaName(TextType), isRequired: false, description: description);

    internal static SchemaNode SchemaNodeOf(NodeInfo node) => node switch
    {
        FieldInfo field => field.Field,
        GroupInfo group => group.Group,
        _ => throw new ArgumentException($"Unknown node type {node.GetType().Name}.", nameof(node)),
    };

    internal static string? Find(SchemaNode node, string name)
        => node.Properties.FirstOrDefault(p => String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    internal static string? Find(NodeInfo node, string name)
        => Find(SchemaNodeOf(node), name);

    internal static bool IsTrue(string? value)
        => String.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";

    /// <summary>
    /// Parses a number in the notation of the <c>midi.*</c> properties: '-' and '_' are removed, a '0x' prefix or 'H'
    /// postfix means hex, otherwise decimal.
    /// </summary>
    internal static bool TryParseNumber(string? text, out ulong value)
    {
        value = 0;
        if (String.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var digits = text.Trim().Replace("-", String.Empty).Replace("_", String.Empty);
        if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return UInt64.TryParse(digits.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }
        if (digits.EndsWith('H') || digits.EndsWith('h'))
        {
            return UInt64.TryParse(digits.AsSpan(0, digits.Length - 1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }
        return UInt64.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
