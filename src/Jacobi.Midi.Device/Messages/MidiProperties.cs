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
    public const string Address = "midi.address";
    public const string Stride = "midi.stride";
    public const string Repeat = "midi.repeat";
    public const string Atomic = "midi.atomic";
    public const string Access = "midi.access";
    public const string DevicePrefix = "midi.device.";
    public const string Unit = "midi.unit";
    public const string MaxDataSize = "midi.maxDataSize";
    public const string Role = "midi.role";
    public const string AddressBits = "midi.addressBits";

    // values of midi.role
    public const string RoleAddress = "address";
    public const string RoleSize = "size";
    public const string RoleData = "data";
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
