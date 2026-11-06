using System.Collections.Concurrent;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// The envelope messages of one open choice contract (memberNodeDef) for an address map: the data messages that can
/// carry it and any other message an exchange definition names (<see cref="Message"/>), with what they mean for the
/// map: their address settings (<c>midi.unit</c>, <c>midi.maxDataSize</c>), the <c>address</c> and <c>size</c> roles
/// and the address space. Finding the messages, selecting the body and the messages without data are generic
/// (<see cref="DeviceMessages"/>, <see cref="ConformingBodies"/>). All vendor specifics are in the schemas.
/// </summary>
internal sealed class AddressProtocol
{
    private readonly Serializer _serializer;
    private readonly ConformingBodies _bodies;
    private readonly int? _maxDataSize;
    private readonly ConcurrentDictionary<string, Lazy<DeviceMessage>> _named = new(StringComparer.OrdinalIgnoreCase);
    // the address settings of the data messages and of the named messages that were resolved
    private readonly ConcurrentDictionary<DeviceMessage, AddressSettings> _settings = new(ReferenceEqualityComparer.Instance);

    private AddressProtocol(Serializer serializer, AddressSpace addressSpace,
        IReadOnlyList<(DeviceMessage Message, AddressSettings Settings)> dataMessages, ConformingBodies bodies, int? maxDataSize)
    {
        _serializer = serializer;
        AddressSpace = addressSpace;
        DataMessages = [.. dataMessages.Select(data => data.Message)];
        foreach (var (message, settings) in dataMessages)
        {
            _settings[message] = settings;
        }
        _bodies = bodies;
        _maxDataSize = maxDataSize;
    }

    /// <summary>The open choice contract (the nodeDef the map roots conform to).</summary>
    public SchemaName Contract => _bodies.Contract;
    public AddressSpace AddressSpace { get; }
    /// <summary>The messages that carry the contract: the groups of an envelope root that hold its data open choice.</summary>
    public IReadOnlyList<DeviceMessage> DataMessages { get; }

    /// <summary>
    /// The address settings of a message of this protocol (a data message, or a message resolved with <see cref="Message"/>),
    /// with the map root's overrides applied.
    /// </summary>
    /// <exception cref="ArgumentException">The message is not a message of this protocol.</exception>
    public AddressSettings SettingsOf(DeviceMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _settings.TryGetValue(message, out var settings)
            ? settings
            : throw new ArgumentException($"'{message.FullName}' is not a message of the contract '{Contract}' (resolve it with Message).", nameof(message));
    }

    /// <summary>The values of the roles <c>address</c> and <c>size</c> for a block at <paramref name="addresses"/>, for writing a message.</summary>
    internal static IReadOnlyDictionary<string, long> RolesOf(AddressRange addresses)
        => new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            [MidiProperties.RoleAddress] = addresses.Start,
            [MidiProperties.RoleSize] = addresses.Size,
        };

    /// <summary>The address read from the <c>address</c> field of a message; null when it was not read (yet).</summary>
    internal static long? AddressOf(EnvelopeSink envelope)
        => envelope.BlockRoles.TryGetValue(MidiProperties.RoleAddress, out var address) ? address : null;

    /// <summary>True when the message asks for data: it has the role <c>address</c> and carries no data.</summary>
    public static bool IsRequest(DeviceMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return !message.IsData && message.Roles.ContainsKey(MidiProperties.RoleAddress);
    }

    /// <summary>
    /// Finds the data messages of a contract in the schemas (<see cref="DeviceMessages.FindDataMessageGroups"/>) and
    /// the groups that conform to it, and checks them as messages of an address map. Problems are added to
    /// <paramref name="problems"/>.
    /// </summary>
    internal static AddressProtocol Create(SchemaSet schemas, Serializer serializer, SchemaName contract, AddressMapProblems problems)
    {
        var bodies = ConformingBodies.Find(schemas, contract, problems);

        var dataMessages = new List<(DeviceMessage Message, AddressSettings Settings)>();
        foreach (var (plan, group, document) in DeviceMessages.FindDataMessageGroups(serializer, contract))
        {
            var message = DeviceMessages.Create(plan, group, document, problems);
            var settings = SettingsOf(document, group, problems);
            Validate(document, group.Path, message, settings, problems);
            dataMessages.Add((message, settings));
        }

        if (dataMessages.Count == 0)
        {
            problems.Error(contract.Namespace, default, $"There is no data message (a group with an open choice midi.role=\"data\") for the contract '{contract}'.");
        }

        var addressSpace = AddressSpaceOf(dataMessages.Select(data => data.Message), problems);
        return new AddressProtocol(serializer, addressSpace, dataMessages, bodies, maxDataSize: null);
    }

    /// <summary>
    /// A copy with the settings of a map root applied (<c>midi.unit</c>, <c>midi.maxDataSize</c>). The unit only applies
    /// to the data messages; the messages that ask for data keep what the device answers to.
    /// </summary>
    internal AddressProtocol WithOverrides(string document, GroupInfo mapRoot, AddressMapProblems problems)
    {
        var unit = Unit(document, mapRoot, problems);
        var maxDataSize = Number(document, mapRoot, MidiProperties.MaxDataSize, problems) is { } size ? (int)size : (int?)null;
        if (unit is null && maxDataSize is null)
        {
            return this;
        }

        AddressSettings Override(AddressSettings settings) => new(unit ?? settings.Unit, maxDataSize ?? settings.MaxDataSize);
        return new AddressProtocol(_serializer, AddressSpace,
            [.. DataMessages.Select(message => (message, Override(SettingsOf(message))))],
            _bodies, maxDataSize);
    }

    /// <summary>
    /// The message group an exchange definition names by its schema name (document, root and path, e.g.
    /// 'Roland.Message32.RQ1'), with the map root's <c>midi.maxDataSize</c> applied. A data message is one of the
    /// <see cref="DataMessages"/>: a message that carries data of another contract cannot carry this map.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no such message group, or it carries data of another contract.</exception>
    /// <exception cref="AddressMapException">The roles of the message are not valid.</exception>
    public DeviceMessage Message(SchemaName name)
        => _named.GetOrAdd(name.FullName, _ => new Lazy<DeviceMessage>(() => Resolve(name))).Value;

    private DeviceMessage Resolve(SchemaName name)
    {
        var fullName = name.FullName;
        if (DataMessages.FirstOrDefault(message => String.Equals(message.FullName, fullName, StringComparison.OrdinalIgnoreCase)) is { } data)
        {
            return data;
        }

        var (plan, group, document) = DeviceMessages.FindMessageGroup(_serializer, name);
        var problems = new AddressMapProblems();
        var message = DeviceMessages.Create(plan, group, document, problems);
        if (message.IsData)
        {
            throw new InvalidOperationException($"'{name}' carries data, but it cannot carry '{Contract}' (its data open choice has another memberNodeDef).");
        }
        var settings = SettingsOf(document, group, problems);
        Validate(document, group.Path, message, settings, problems);
        if (problems.HasErrors)
        {
            throw new AddressMapException(fullName, problems);
        }
        _settings[message] = _maxDataSize is { } size ? settings with { MaxDataSize = size } : settings;
        return message;
    }

    /// <summary>
    /// Recognises a message without data (e.g. an acknowledgement): the constants match and the device values in it
    /// agree with <paramref name="device"/>. Returns false for any other message.
    /// </summary>
    public bool TryRead(DeviceMessage message, ReadOnlyMemory<byte> sysEx, DeviceValues device)
        => DeviceMessages.TryReadWithoutData(_serializer, message, sysEx, device);

    /// <summary>
    /// Selects the body group (device map) for the device values read from an envelope (see <see cref="ConformingBodies.Select"/>).
    /// </summary>
    public SchemaName SelectBody(DeviceValues values) => _bodies.Select(values);

    /// <summary>
    /// A message without data as a complete SysEx message: a request for the addresses, or any other message (the
    /// addresses are only used for the roles <c>address</c> and <c>size</c>).
    /// </summary>
    public byte[] CreateMessage(DeviceMessage message, AddressRange addresses, DeviceValues device)
        => DeviceMessages.WriteWithoutData(_serializer, message, RolesOf(addresses), device);

    /// <summary>
    /// The address-map roots in the schemas: the roots whose nodeDef is the contract (memberNodeDef) of an open choice
    /// with <c>midi.role="data"</c>, i.e. the groups a data message can carry.
    /// </summary>
    internal static IReadOnlyList<SchemaName> FindMapRoots(SchemaSet schemas) => DeviceMessages.FindCarriedRoots(schemas);

    // the address settings of a message group, as the schema gives them
    private static AddressSettings SettingsOf(string document, GroupInfo group, AddressMapProblems problems)
        => new(Unit(document, group, problems) ?? AddressUnit.Range,
            (int)(Number(document, group, MidiProperties.MaxDataSize, problems) ?? 0));

    // a message that carries data needs an address and has no size; one that asks for a range needs both
    private static void Validate(string document, SchemaPath path, DeviceMessage message, AddressSettings settings, AddressMapProblems problems)
    {
        var hasAddress = message.Roles.TryGetValue(MidiProperties.RoleAddress, out var address);
        if (hasAddress && address is not FieldInfo)
        {
            problems.Error(document, address!.Path, $"the role '{MidiProperties.RoleAddress}' only applies to a field.");
        }

        var hasSize = message.Roles.ContainsKey(MidiProperties.RoleSize);
        if (message.IsData)
        {
            if (!hasAddress)
            {
                problems.Error(document, path, $"the message carries data but has no field with the role '{MidiProperties.RoleAddress}'.");
            }
            if (hasSize)
            {
                problems.Error(document, message.Roles[MidiProperties.RoleSize].Path, $"a message that carries data has no role '{MidiProperties.RoleSize}': the data gives the size.");
            }
            return;
        }

        if (hasAddress && settings.Unit == AddressUnit.Range && !hasSize)
        {
            problems.Error(document, path, $"a message that asks for a range needs a field with the role '{MidiProperties.RoleSize}' (or a '{MidiProperties.Unit}').");
        }
        if (hasSize && (!hasAddress || settings.Unit != AddressUnit.Range))
        {
            problems.Error(document, message.Roles[MidiProperties.RoleSize].Path, $"the role '{MidiProperties.RoleSize}' is only used on a message that asks for a range (with the role '{MidiProperties.RoleAddress}').");
        }
    }

    private static AddressSpace AddressSpaceOf(IEnumerable<DeviceMessage> messages, AddressMapProblems problems)
    {
        AddressSpace? space = null;
        foreach (var message in messages)
        {
            if (!message.Roles.TryGetValue(MidiProperties.RoleAddress, out var node) || node is not FieldInfo field)
            {
                continue;
            }
            var document = message.Range.ExecutionPlan.Name?.Namespace ?? String.Empty;
            if (field.ByteLength is not int byteCount)
            {
                problems.Error(document, field.Path, "the address field needs a constant byteLength (the number of address bytes).");
                continue;
            }

            var bits = Number(document, field, MidiProperties.AddressBits, problems) is { } b ? (int)b : 7;
            var current = new AddressSpace(byteCount, bits);
            if (space is { } previous && previous != current)
            {
                problems.Error(document, field.Path, $"the address ({byteCount} x {bits} bits) differs from the other messages ({previous.ByteCount} x {previous.BitsPerByte} bits).");
                continue;
            }
            space = current;
        }
        return space ?? new AddressSpace(0);
    }

    private static AddressUnit? Unit(string document, NodeInfo node, AddressMapProblems problems)
    {
        if (MidiProperties.Find(node, MidiProperties.Unit) is not { } text)
        {
            return null;
        }
        if (Enum.TryParse<AddressUnit>(text, ignoreCase: true, out var unit))
        {
            return unit;
        }
        problems.Error(document, node.Path, $"'{MidiProperties.Unit}' must be range, field or atomic, not '{text}'.");
        return null;
    }

    private static long? Number(string document, NodeInfo node, string property, AddressMapProblems problems)
    {
        if (MidiProperties.Find(node, property) is not { } text)
        {
            return null;
        }
        if (AddressSpace.TryParseNumber(text, out var value) && value <= Int32.MaxValue)
        {
            return (long)value;
        }
        problems.Error(document, node.Path, $"'{property}' has no valid number: '{text}'.");
        return null;
    }
}
