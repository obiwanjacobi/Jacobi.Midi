using System.Buffers;
using System.Collections.Concurrent;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// The envelope messages of one open choice contract (memberNodeDef): the data messages that can carry it (found
/// through their open choice with <c>midi.role="data"</c>), which body group (device map) a data message carries, and
/// any other message an exchange definition names (<see cref="Message"/>): how to fill and recognise them.
/// Generic: all vendor specifics are in the schemas.
/// </summary>
internal sealed class AddressProtocol
{
    private readonly Serializer _serializer;
    private readonly IReadOnlyList<BodyCandidate> _bodies;
    private readonly int? _maxDataSize;
    private readonly ConcurrentDictionary<string, Lazy<AddressMessage>> _named = new(StringComparer.OrdinalIgnoreCase);

    private AddressProtocol(Serializer serializer, SchemaName contract, AddressSpace addressSpace,
        IReadOnlyList<AddressMessage> dataMessages, IReadOnlyList<BodyCandidate> bodies, int? maxDataSize)
    {
        _serializer = serializer;
        Contract = contract;
        AddressSpace = addressSpace;
        DataMessages = dataMessages;
        _bodies = bodies;
        _maxDataSize = maxDataSize;
    }

    /// <summary>The open choice contract (the nodeDef the map roots conform to).</summary>
    public SchemaName Contract { get; }
    public AddressSpace AddressSpace { get; }
    /// <summary>The messages that carry the contract: the groups of an envelope root that hold its data open choice.</summary>
    public IReadOnlyList<AddressMessage> DataMessages { get; }

    /// <summary>
    /// Finds the data messages of a contract in the schemas: in an envelope root that holds an open choice with
    /// <c>midi.role="data"</c> whose memberNodeDef is the contract, the member group of the root that holds the choice
    /// (or the root itself when the choice is a member of the root). Problems are added to <paramref name="problems"/>.
    /// </summary>
    internal static AddressProtocol Create(SchemaSet schemas, Serializer serializer, SchemaName contract, AddressMapProblems problems)
    {
        var dataMessages = new List<AddressMessage>();
        var bodies = new List<BodyCandidate>();

        foreach (var document in schemas.Documents)
        {
            foreach (var root in document.Roots)
            {
                if (root.NodeDef is { } nodeDef && SameName(Qualify(nodeDef, document.Name), contract))
                {
                    bodies.Add(new BodyCandidate(new SchemaName($"{document.Name}.{root.Name}"), DeviceValues.FromProperties(root, document.Name, new SchemaPath(root.Name), problems)));
                }

                if (!CarriesContract(root, document.Name, contract))
                {
                    continue;
                }

                var plan = serializer.GetPlan(new SchemaName($"{document.Name}.{root.Name}"));
                foreach (var group in DataMessageGroups(plan.Root, document.Name, contract))
                {
                    var message = CreateMessage(plan, group, document.Name, problems);
                    Validate(document.Name, group.Path, message, problems);
                    dataMessages.Add(message);
                }
            }
        }

        if (dataMessages.Count == 0)
        {
            problems.Error(contract.Namespace, default, $"There is no data message (a group with an open choice midi.role=\"data\") for the contract '{contract}'.");
        }

        var addressSpace = AddressSpaceOf(dataMessages, problems);
        return new AddressProtocol(serializer, contract, addressSpace, dataMessages, bodies, maxDataSize: null);
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

        return new AddressProtocol(_serializer, Contract, AddressSpace,
            [.. DataMessages.Select(message => message with { Unit = unit ?? message.Unit, MaxDataSize = maxDataSize ?? message.MaxDataSize })],
            _bodies, maxDataSize);
    }

    /// <summary>
    /// The message group an exchange definition names by its schema name (document, root and path, e.g.
    /// 'Roland.Message32.RQ1'), with the map root's <c>midi.maxDataSize</c> applied. A data message is one of the
    /// <see cref="DataMessages"/>: a message that carries data of another contract cannot carry this map.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no such message group, or it carries data of another contract.</exception>
    /// <exception cref="AddressMapException">The roles of the message are not valid.</exception>
    public AddressMessage Message(SchemaName name)
        => _named.GetOrAdd(name.FullName, _ => new Lazy<AddressMessage>(() => Resolve(name))).Value;

    private AddressMessage Resolve(SchemaName name)
    {
        var fullName = name.FullName;
        if (DataMessages.FirstOrDefault(message => String.Equals(message.FullName, fullName, StringComparison.OrdinalIgnoreCase)) is { } data)
        {
            return data;
        }

        foreach (var document in _serializer.Schemas.Documents
            .Where(document => fullName.StartsWith(document.Name + SchemaName.Separator, StringComparison.OrdinalIgnoreCase)))
        {
            var path = new SchemaPath(fullName[(document.Name.Length + 1)..]);
            var rootName = path.Value.Split(SchemaName.Separator)[0];
            if (!document.Roots.Any(root => String.Equals(root.Name, rootName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var plan = _serializer.GetPlan(new SchemaName($"{document.Name}.{rootName}"));
            var node = plan.Find(path);
            if (node is null)
            {
                break;
            }
            if (node is not GroupInfo group || group is ChoiceInfo or RepeatInfo)
            {
                throw new InvalidOperationException($"'{name}' is not a message: it is not a group.");
            }

            var problems = new AddressMapProblems();
            var message = CreateMessage(plan, group, document.Name, problems);
            if (message.IsData)
            {
                throw new InvalidOperationException($"'{name}' carries data, but it cannot carry '{Contract}' (its data open choice has another memberNodeDef).");
            }
            Validate(document.Name, group.Path, message, problems);
            if (problems.HasErrors)
            {
                throw new AddressMapException(fullName, problems);
            }
            return _maxDataSize is { } size ? message with { MaxDataSize = size } : message;
        }
        throw new InvalidOperationException($"There is no message '{name}' in the schemas (name it as document.root.group, e.g. 'Roland.Message16.RQ1').");
    }

    /// <summary>
    /// Recognises a message without data (e.g. an acknowledgement): the constants match and the device values in it
    /// agree with <paramref name="device"/>. Returns false for any other message.
    /// </summary>
    public bool TryRead(AddressMessage message, ReadOnlyMemory<byte> sysEx, DeviceValues device)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            var sink = new EnvelopeSink(_ => throw new InvalidOperationException($"'{message.Name}': the message carries no data."));
            var read = _serializer.Deserialize(message.Range, new ReadOnlySequence<byte>(sysEx), sink);
            return read == sysEx.Length && sink.Device.All(v => !device.TryGet(v.Key, out var expected) || expected == v.Value);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // not this message
            return false;
        }
    }

    /// <summary>
    /// Selects the body group (device map) for the device values read from an envelope: of the groups that conform
    /// to the contract, the one whose <c>midi.device.*</c> values all match. None or more than one is an error.
    /// </summary>
    public SchemaName SelectBody(DeviceValues values)
    {
        var matches = _bodies
            .Where(body => body.Device.All(device => values.TryGet(device.Key, out var value) && value == device.Value))
            .ToList();

        return matches.Count switch
        {
            1 => matches[0].Name,
            0 => throw new InvalidOperationException(
                $"No group that conforms to '{Contract}' matches the device values {Format(values)}."),
            _ => throw new InvalidOperationException(
                $"More than one group that conforms to '{Contract}' matches the device values {Format(values)}: {String.Join(", ", matches.Select(m => m.Name))}."),
        };
    }

    /// <summary>
    /// A message without data as a complete SysEx message: a request for the addresses, or any other message (the
    /// addresses are only used for the roles <c>address</c> and <c>size</c>).
    /// </summary>
    public byte[] CreateMessage(AddressMessage message, AddressRange addresses, DeviceValues device)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsData)
        {
            throw new ArgumentException($"'{message.Name}' carries data: it is built with the data of a block.", nameof(message));
        }

        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(message.Range, new EnvelopeSource(addresses, device), writer);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// The address-map roots in the schemas: the roots whose nodeDef is the contract (memberNodeDef) of an open choice
    /// with <c>midi.role="data"</c>, i.e. the groups a data message can carry.
    /// </summary>
    internal static IReadOnlyList<SchemaName> FindMapRoots(SchemaSet schemas)
    {
        var contracts = new List<SchemaName>();
        foreach (var document in schemas.Documents)
        {
            foreach (var root in document.Roots)
            {
                CollectContracts(root, document.Name, contracts);
            }
        }

        var roots = new List<SchemaName>();
        foreach (var document in schemas.Documents)
        {
            foreach (var root in document.Roots)
            {
                if (root.NodeDef is { } nodeDef && contracts.Any(c => SameName(c, Qualify(nodeDef, document.Name))))
                {
                    roots.Add(new SchemaName($"{document.Name}.{root.Name}"));
                }
            }
        }
        return roots;
    }

    private static void CollectContracts(SchemaGroup group, string documentName, List<SchemaName> contracts)
    {
        foreach (var member in group.Members)
        {
            if (member is SchemaChoice { MemberNodeDef: { } nodeDef } choice && IsRole(choice, MidiProperties.RoleData))
            {
                contracts.Add(Qualify(nodeDef, documentName));
            }
            else if (member is SchemaGroup child)
            {
                CollectContracts(child, documentName, contracts);
            }
        }
    }

    // the groups of an envelope root that are data messages of the contract: the member group that holds the data
    // open choice, or the root itself when the choice is one of its members
    private static IEnumerable<GroupInfo> DataMessageGroups(GroupInfo root, string documentName, SchemaName contract)
    {
        if (root.Members.OfType<ChoiceInfo>().Any(member => member.Group is SchemaChoice choice && IsDataOf(choice, documentName, contract)))
        {
            return [root];
        }
        return root.Members.OfType<GroupInfo>()
            .Where(member => member is not ChoiceInfo && CarriesContract(member.Group, documentName, contract));
    }

    private static bool CarriesContract(SchemaGroup group, string documentName, SchemaName contract)
        => group.Members.Any(member => member switch
        {
            SchemaChoice choice when IsDataOf(choice, documentName, contract) => true,
            SchemaGroup child => CarriesContract(child, documentName, contract),
            _ => false,
        });

    private static bool IsDataOf(SchemaChoice choice, string documentName, SchemaName contract)
        => choice.MemberNodeDef is { } nodeDef && IsRole(choice, MidiProperties.RoleData) && SameName(Qualify(nodeDef, documentName), contract);

    private static AddressMessage CreateMessage(ExecutionPlan plan, GroupInfo group, string documentName, AddressMapProblems problems)
    {
        var roles = new Dictionary<string, NodeInfo>(StringComparer.OrdinalIgnoreCase);
        CollectRoles(documentName, group, roles, problems);
        return new AddressMessage
        {
            Name = group.Path,
            Unit = Unit(documentName, group, problems) ?? AddressUnit.Range,
            MaxDataSize = (int)(Number(documentName, group, MidiProperties.MaxDataSize, problems) ?? 0),
            Range = plan.CreateRange(group.Path),
            Roles = roles,
        };
    }

    private static void CollectRoles(string document, GroupInfo group, Dictionary<string, NodeInfo> roles, AddressMapProblems problems)
    {
        foreach (var member in group.Members)
        {
            if (MidiProperties.Find(member, MidiProperties.Role) is { } role && !roles.TryAdd(role, member))
            {
                problems.Error(document, member.Path, $"the role '{role}' is already used by '{roles[role].Path}'.");
            }
            if (member is GroupInfo child and not ChoiceInfo)
            {
                CollectRoles(document, child, roles, problems);
            }
        }
    }

    // a message that carries data needs an address and has no size; one that asks for a range needs both
    private static void Validate(string document, SchemaPath path, AddressMessage message, AddressMapProblems problems)
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

        if (hasAddress && message.Unit == AddressUnit.Range && !hasSize)
        {
            problems.Error(document, path, $"a message that asks for a range needs a field with the role '{MidiProperties.RoleSize}' (or a '{MidiProperties.Unit}').");
        }
        if (hasSize && (!hasAddress || message.Unit != AddressUnit.Range))
        {
            problems.Error(document, message.Roles[MidiProperties.RoleSize].Path, $"the role '{MidiProperties.RoleSize}' is only used on a message that asks for a range (with the role '{MidiProperties.RoleAddress}').");
        }
    }

    private static AddressSpace AddressSpaceOf(IEnumerable<AddressMessage> messages, AddressMapProblems problems)
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

    private static bool IsRole(SchemaNode node, string role)
        => String.Equals(MidiProperties.Find(node, MidiProperties.Role), role, StringComparison.OrdinalIgnoreCase);

    private static SchemaName Qualify(SchemaName name, string documentName)
        => String.IsNullOrEmpty(name.Namespace) ? new SchemaName($"{documentName}.{name.Name}") : name;

    private static bool SameName(SchemaName left, SchemaName right)
        => String.Equals(left.Namespace, right.Namespace, StringComparison.OrdinalIgnoreCase)
        && String.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);

    private static string Format(DeviceValues values)
        => "{" + String.Join(", ", values.Select(v => $"{v.Key}=0x{v.Value:X}")) + "}";

    /// <summary>A group that conforms to the contract, with the device values it declares.</summary>
    private sealed record BodyCandidate(SchemaName Name, DeviceValues Device);
}
