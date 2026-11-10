using System.Globalization;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// The names of the <c>midi.*</c> schema properties of the address map mechanism and helpers to read them from
/// schema and plan nodes.
/// </summary>
internal static class MidiProperties
{
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

    public static SchemaNode SchemaNodeOf(NodeInfo node) => node switch
    {
        FieldInfo field => field.Field,
        GroupInfo group => group.Group,
        _ => throw new ArgumentException($"Unknown node type {node.GetType().Name}.", nameof(node)),
    };

    public static string? Find(SchemaNode node, string name)
        => node.Properties.FirstOrDefault(p => String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    public static string? Find(NodeInfo node, string name)
        => Find(SchemaNodeOf(node), name);

    public static bool IsTrue(string? value)
        => String.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";

    /// <summary>
    /// Parses a number in the notation of the <c>midi.*</c> properties: '-' and '_' are removed, a '0x' prefix or 'H'
    /// postfix means hex, otherwise decimal.
    /// </summary>
    public static bool TryParseNumber(string? text, out ulong value)
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
