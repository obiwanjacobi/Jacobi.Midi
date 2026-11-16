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
    /// repeat item, on its own). Any group or repeat: of an address map, a Property Exchange resource (atomic: set whole).
    /// </summary>
    public const string Transfer = "midi.transfer";
    /// <summary>
    /// The access of a node: <c>readwrite</c> (default), <c>read</c> or <c>write</c> (inherited). Any node: an addressed
    /// node of an address map, a Property Exchange root.
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
    /// The role of a node: which value an envelope field or open choice holds (<c>address</c>, <c>size</c>,
    /// <c>device.&lt;name&gt;</c> or <c>data</c>), or on a root, that it is a command root (<c>cmd1</c>, <c>cmd2</c>).
    /// </summary>
    public const string Role = "midi.role";
    /// <summary>
    /// The channel message a field of a command root is transferred with: <c>&lt;kind&gt;#&lt;number&gt;</c>, e.g.
    /// <c>cc#74</c>, <c>nrpn#3/12</c>, or <c>pc</c> (see <see cref="Commands.Command"/>).
    /// </summary>
    public const string Cmd = "midi.cmd";
    /// <summary>
    /// The name the Property Exchange device knows a node of a <c>pe</c> root by, when it differs from the node's name
    /// (a name that is not a valid schema name): the resource of the root (e.g. <c>X-ProgramEdit</c>), the
    /// <c>resId</c> of an entry, the JSON key of a member.
    /// </summary>
    public const string Key = "midi.key";
    /// <summary>
    /// The bits per address byte of the <c>address</c> field (envelope); default 7.
    /// </summary>
    public const string AddressBits = "midi.addressBits";
    /// <summary>
    /// The documentation of the node it is on: plain text, possibly multi-line (any node, not inherited).
    /// </summary>
    public const string Doc = "midi.doc";

    // values of midi.access

    /// <summary>The value of <see cref="Access"/> for a node that can only be read.</summary>
    public const string AccessRead = "read";
    /// <summary>The value of <see cref="Access"/> for a node that can only be written.</summary>
    public const string AccessWrite = "write";

    // values of midi.transfer

    /// <summary>The value of <see cref="Transfer"/> for a group or repeat that is always transferred whole.</summary>
    public const string TransferAtomic = "atomic";

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
    /// The role of a root whose fields are MIDI 1.0 channel messages (<see cref="Cmd"/>).
    /// </summary>
    public const string RoleCommand1 = "cmd1";
    /// <summary>
    /// The role of a root whose fields are MIDI 2.0 channel messages (<see cref="Cmd"/>).
    /// </summary>
    public const string RoleCommand2 = "cmd2";
    /// <summary>
    /// The role of a root that stands for a Property Exchange resource (<see cref="Resource"/>), generated from what
    /// the device describes.
    /// </summary>
    public const string RolePropertyExchange = "pe";

    /// <summary>
    /// The tags with a fixed name (a device value, <see cref="DevicePrefix"/>, has a name of its own).
    /// </summary>
    public static IReadOnlyList<PropertyDescriptor> Descriptors { get; } =
    [
        Describe(Address, "Map: the absolute start address of the node, in wire form (e.g. 0x40-00)."),
        Describe(Stride, "Map: the number of addresses the node (or one repeat item) occupies."),
        Describe(Repeat, "Map: the number of items of a repeat in the address space."),
        Describe(Transfer, "Group or repeat: how a group or repeat is cut into blocks: atomic (always whole) or individual (each member or item on its own)."),
        Describe(Access, "Any node: readwrite (default), read or write; inherited."),
        Describe(Unit, "Message or map root: what one message covers: range (default), field or atomic."),
        Describe(MaxDataSize, "Message or map root: the largest number of data bytes in one message."),
        Describe(Role, "Envelope: the value the node holds: address, size, data or device.<name>. Root: cmd1 or cmd2, a command root."),
        Describe(Cmd, "Command root: the channel message of the field: cc#<number>, rpn#<number>, nrpn#<number> (or <msb>/<lsb>) or pc."),
        Describe(Key, "Property Exchange root: the name the device knows the node by (the resource of the root, the resId of an entry, the JSON key of a member), when it is not the node's name."),
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
                => Enum.TryParse<NodeTransfer>(value, ignoreCase: true, out _) ? null : $"'{Transfer}' must be atomic or individual, not '{value}'.",
            Access
                => Enum.TryParse<NodeAccess>(value, ignoreCase: true, out _) ? null : $"'{Access}' must be readwrite, read or write, not '{value}'.",
            Unit
                => Enum.TryParse<AddressUnit>(value, ignoreCase: true, out _) ? null : $"'{name}' must be range, field or atomic, not '{value}'.",
            Role
                => IsRole(value) || IsRootRole(value) ? null
                    : $"'{name}' must be {RoleAddress}, {RoleSize}, {RoleData}, {RoleDevicePrefix}<name>, {RoleCommand1}, {RoleCommand2} or {RolePropertyExchange}, not '{value}'.",
            Cmd
                => Commands.Command.TryParse(value, out _, out var error) ? null : $"'{name}': {error}",
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
    /// The MIDI protocol of a command root (<c>midi.role</c> <c>cmd1</c>: 1, <c>cmd2</c>: 2); null for any other node.
    /// The single place that decides whether a root is a command root: the message maps leave it alone, the
    /// command map refuses any other root.
    /// </summary>
    public static int? CommandProtocolOf(SchemaNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var role = RoleOf(node);
        return String.Equals(role, RoleCommand1, StringComparison.OrdinalIgnoreCase) ? 1
            : String.Equals(role, RoleCommand2, StringComparison.OrdinalIgnoreCase) ? 2
            : null;
    }

    /// <summary>
    /// True when the node is a command root (<c>midi.role</c> <c>cmd1</c> or <c>cmd2</c>), see <see cref="CommandProtocolOf"/>.
    /// </summary>
    public static bool IsCommandRoot(SchemaNode node) => CommandProtocolOf(node) is not null;

    /// <summary>
    /// True when the node is a Property Exchange root (<c>midi.role</c> <c>pe</c>): it stands for a resource of the
    /// device (<see cref="Resource"/>), not for SysEx data, so it has no message map.
    /// </summary>
    public static bool IsPropertyRoot(SchemaNode node)
        => String.Equals(RoleOf(node), RolePropertyExchange, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// False for a root that never has a message map: a command root (<see cref="IsCommandRoot"/>) or a Property
    /// Exchange root (<see cref="IsPropertyRoot"/>); true for any other root (whether its map is valid or not).
    /// </summary>
    public static bool CanHaveMessageMap(SchemaNode root) => !IsCommandRoot(root) && !IsPropertyRoot(root);

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
    /// The access of a node of a plan (<c>midi.access</c>): its own or that of the nearest enclosing node with one;
    /// readwrite by default. A value that is not valid counts as none.
    /// </summary>
    public static NodeAccess AccessOf(NodeInfo node)
        => AccessOf(node, node.Parent is { } parent ? AccessOf(parent) : NodeAccess.ReadWrite, out _);

    /// <summary>
    /// The access of a node of a plan (<c>midi.access</c>): its own, else <paramref name="inherited"/> (also when its
    /// own is not valid: <paramref name="error"/> says why).
    /// </summary>
    internal static NodeAccess AccessOf(NodeInfo node, NodeAccess inherited, out string? error)
    {
        var text = Find(node, Access);
        error = text is null ? null : Validate(Access, text);
        return error is null && Enum.TryParse<NodeAccess>(text, ignoreCase: true, out var access) ? access : inherited;
    }

    /// <summary>
    /// The transfer of a node of a plan (<c>midi.transfer</c>): only its own, not inherited; null when it has none or
    /// it is not valid (<paramref name="error"/> says why). Where it may be is checked by its user.
    /// </summary>
    internal static NodeTransfer? TransferOf(NodeInfo node, out string? error)
    {
        var text = Find(node, Transfer);
        error = text is null ? null : Validate(Transfer, text);
        return error is null && Enum.TryParse<NodeTransfer>(text, ignoreCase: true, out var transfer) ? transfer : null;
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

    /// <summary>
    /// True for a role of a command root: <c>cmd1</c> or <c>cmd2</c>.
    /// </summary>
    internal static bool IsCommandRole(string? role)
        => String.Equals(role, RoleCommand1, StringComparison.OrdinalIgnoreCase)
        || String.Equals(role, RoleCommand2, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True for a role that only applies to a root: <c>cmd1</c>, <c>cmd2</c> or <c>pe</c>.
    /// </summary>
    internal static bool IsRootRole(string? role)
        => IsCommandRole(role) || String.Equals(role, RolePropertyExchange, StringComparison.OrdinalIgnoreCase);

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
