using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// The <c>midi.*</c> tags of the address map mechanism as a tool sees them (e.g. the Schema Editor): which property
/// names are tags, their descriptions, the check of a tag's value on its own and the role of a node. The checks that
/// need the whole map and its envelopes are done by <see cref="AddressMapValidator"/>.
/// </summary>
public static class AddressMapTags
{
    private const string Prefix = "midi.";
    private const string TextType = "sys.string";

    /// <summary>The tag that says which value an envelope node holds (<c>midi.role</c>).</summary>
    public const string Role = MidiProperties.Role;
    /// <summary>The tag that marks a message group of an envelope (<c>midi.message</c>).</summary>
    public const string Message = MidiProperties.Message;
    /// <summary>The prefix of a device value fixed by a map root (<c>midi.device.&lt;name&gt;</c>).</summary>
    public const string DevicePrefix = MidiProperties.DevicePrefix;

    /// <summary>The tags with a fixed name (a device value, <see cref="DevicePrefix"/>, has a name of its own).</summary>
    public static IReadOnlyList<PropertyDescriptor> Descriptors { get; } =
    [
        Describe(MidiProperties.Address, "Map: the absolute start address of the node, in wire form (e.g. 0x40-00)."),
        Describe(MidiProperties.Stride, "Map: the number of addresses the node (or one repeat item) occupies."),
        Describe(MidiProperties.Repeat, "Map: the number of items of a repeat in the address space."),
        Describe(MidiProperties.Atomic, "Map: true when the group is always transferred as a whole."),
        Describe(MidiProperties.Access, "Map: readwrite (default), read or write; inherited."),
        Describe(MidiProperties.Unit, "Message or map root: what one message covers: range (default), field or atomic."),
        Describe(MidiProperties.MaxDataSize, "Message or map root: the largest number of data bytes in one message."),
        Describe(MidiProperties.MessageInterval, "Message or map root: milliseconds to wait after sending the message."),
        Describe(MidiProperties.Exchange, "Envelope or map root: the exchange protocol: oneWay or acknowledged."),
        Describe(MidiProperties.Message, "Envelope: the message group is a request, data, ack, nak or error."),
        Describe(MidiProperties.Role, "Envelope: the value the node holds: address, size, data or device.<name>."),
        Describe(MidiProperties.AddressBits, "Envelope: the bits per address byte of the address field (default 7)."),
    ];

    /// <summary>True when <paramref name="name"/> is in the namespace of the tags: <c>midi.&lt;name&gt;</c> or <c>midi.device.&lt;name&gt;</c>.</summary>
    /// <remarks>Longer names (<c>midi.&lt;processor&gt;.&lt;property&gt;</c>) are properties of a <c>midi</c> processor, not tags.</remarks>
    public static bool IsTag(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase)
            || (name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && name.IndexOf(SchemaPath.Separator, Prefix.Length) < 0);
    }

    /// <summary>
    /// Checks a tag and its value on their own; null when valid. The rules that relate tags (e.g. a role is used inside a
    /// message, a device value is fixed by the map root) are checked by <see cref="AddressMapValidator"/>.
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
            return $"'{name}' is not an address map tag.";
        }

        return descriptor.Name switch
        {
            MidiProperties.Address or MidiProperties.Stride or MidiProperties.MaxDataSize or MidiProperties.MessageInterval
                or MidiProperties.AddressBits or MidiProperties.Repeat
                => IsNumber(value) ? null : $"'{name}' must be a number, not '{value}'.",
            MidiProperties.Atomic
                => value is not null && (MidiProperties.IsTrue(value) || value == "0" || String.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                    ? null : $"'{name}' must be true or false, not '{value}'.",
            MidiProperties.Access
                => Enum.TryParse<AddressAccess>(value, ignoreCase: true, out _) ? null : $"'{name}' must be readwrite, read or write, not '{value}'.",
            MidiProperties.Unit
                => Enum.TryParse<AddressUnit>(value, ignoreCase: true, out _) ? null : $"'{name}' must be range, field or atomic, not '{value}'.",
            MidiProperties.Message
                => Enum.TryParse<AddressMessageKind>(value, ignoreCase: true, out _) ? null : $"'{name}' must be request, data, ack, nak or error, not '{value}'.",
            MidiProperties.Exchange
                => String.Equals(value, AddressProtocol.OneWay, StringComparison.OrdinalIgnoreCase)
                    || String.Equals(value, AddressProtocol.Acknowledged, StringComparison.OrdinalIgnoreCase)
                    ? null : $"'{name}' must be {AddressProtocol.OneWay} or {AddressProtocol.Acknowledged}, not '{value}'.",
            MidiProperties.Role
                => IsRole(value) ? null : $"'{name}' must be {MidiProperties.RoleAddress}, {MidiProperties.RoleSize}, {MidiProperties.RoleData} or {MidiProperties.RoleDevicePrefix}<name>, not '{value}'.",
            _ => null,
        };
    }

    /// <summary>The role of a node (<c>midi.role</c>); null when it has none.</summary>
    public static string? RoleOf(SchemaNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return MidiProperties.Find(node, MidiProperties.Role);
    }

    /// <summary>True when the node holds the data of a message (<c>midi.role="data"</c>, an open choice).</summary>
    public static bool IsData(SchemaNode node)
        => String.Equals(RoleOf(node), MidiProperties.RoleData, StringComparison.OrdinalIgnoreCase);

    /// <summary>The kind of message the node is (<c>midi.message</c>); null when it is not a message group.</summary>
    public static string? MessageOf(SchemaNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return MidiProperties.Find(node, MidiProperties.Message);
    }

    /// <summary>True for a valid role: <c>address</c>, <c>size</c>, <c>data</c> or <c>device.&lt;name&gt;</c>.</summary>
    internal static bool IsRole(string? role)
        => role is not null
        && (String.Equals(role, MidiProperties.RoleAddress, StringComparison.OrdinalIgnoreCase)
            || String.Equals(role, MidiProperties.RoleSize, StringComparison.OrdinalIgnoreCase)
            || String.Equals(role, MidiProperties.RoleData, StringComparison.OrdinalIgnoreCase)
            || (role.StartsWith(MidiProperties.RoleDevicePrefix, StringComparison.OrdinalIgnoreCase) && role.Length > MidiProperties.RoleDevicePrefix.Length));

    private static bool IsNumber(string? value) => AddressSpace.TryParseNumber(value, out _);

    private static PropertyDescriptor Describe(string name, string description)
        => new(name, new SchemaName(TextType), isRequired: false, description: description);
}
