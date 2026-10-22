using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// The addressed-data messages that can carry one open choice contract (memberNodeDef), found through their
/// <c>midi.*</c> tags: the requests, data messages and replies, how to fill and recognise them, which body group
/// (device map) a data message carries, and which exchange protocol the envelope uses.
/// Generic: all vendor specifics are in the schemas.
/// </summary>
internal sealed class AddressProtocol
{
    /// <summary>The exchange protocol of an envelope without replies: requests are answered with data, data is not acknowledged.</summary>
    public const string OneWay = "oneWay";
    /// <summary>The exchange protocol of an envelope with ack/nak replies: received data is acknowledged by the device.</summary>
    public const string Acknowledged = "acknowledged";

    private readonly Serializer _serializer;
    private readonly IReadOnlyList<BodyCandidate> _bodies;

    private AddressProtocol(Serializer serializer, SchemaName contract, AddressSpace addressSpace, string exchange,
        IReadOnlyList<AddressMessage> requests, IReadOnlyList<AddressMessage> dataMessages, IReadOnlyList<AddressMessage> replies,
        IReadOnlyList<BodyCandidate> bodies)
    {
        _serializer = serializer;
        Contract = contract;
        AddressSpace = addressSpace;
        Exchange = exchange;
        Requests = requests;
        DataMessages = dataMessages;
        Replies = replies;
        _bodies = bodies;
    }

    /// <summary>The open choice contract (the nodeDef the map roots conform to).</summary>
    public SchemaName Contract { get; }
    public AddressSpace AddressSpace { get; }
    /// <summary>
    /// The name of the exchange protocol (<c>midi.exchange</c> on the envelope root or the map root); without the tag
    /// it is <see cref="Acknowledged"/> when the envelope has ack or nak replies, else <see cref="OneWay"/>.
    /// </summary>
    public string Exchange { get; }
    public IReadOnlyList<AddressMessage> Requests { get; }
    public IReadOnlyList<AddressMessage> DataMessages { get; }
    /// <summary>The device's replies: the ack, nak and error messages.</summary>
    public IReadOnlyList<AddressMessage> Replies { get; }

    /// <summary>
    /// Finds the messages of a contract in the schemas: the data messages (<c>midi.message="data"</c>) that contain an
    /// open choice with <c>midi.role="data"</c> whose memberNodeDef is the contract, and the requests and replies
    /// (<c>midi.message="request"</c>, <c>ack</c>, <c>nak</c>, <c>error</c>) in the same envelope roots.
    /// Problems are added to <paramref name="errors"/>.
    /// </summary>
    internal static AddressProtocol Create(SchemaSet schemas, Serializer serializer, SchemaName contract, List<string> errors)
    {
        var requests = new List<AddressMessage>();
        var dataMessages = new List<AddressMessage>();
        var replies = new List<AddressMessage>();
        var exchanges = new List<string>();
        var bodies = new List<BodyCandidate>();

        foreach (var document in schemas.Documents)
        {
            foreach (var root in document.Roots)
            {
                if (root.NodeDef is { } nodeDef && SameName(Qualify(nodeDef, document.Name), contract))
                {
                    bodies.Add(new BodyCandidate(new SchemaName($"{document.Name}.{root.Name}"), DeviceValues.FromProperties(root, errors)));
                }

                if (!CarriesContract(root, document.Name, contract))
                {
                    continue;
                }

                var plan = serializer.GetPlan(new SchemaName($"{document.Name}.{root.Name}"));
                var found = new List<AddressMessage>();
                CollectMessages(plan, plan.Root, document.Name, contract, found, errors);
                if (found.Any(m => m.Kind == AddressMessageKind.Data))
                {
                    requests.AddRange(found.Where(m => m.Kind == AddressMessageKind.Request));
                    dataMessages.AddRange(found.Where(m => m.Kind == AddressMessageKind.Data));
                    replies.AddRange(found.Where(m => IsReply(m.Kind)));
                    if (MidiProperties.Find(root, MidiProperties.Exchange) is { } exchange)
                    {
                        exchanges.Add(exchange);
                    }
                }
            }
        }

        if (dataMessages.Count == 0)
        {
            errors.Add($"There is no data message (midi.message=\"data\" with an open choice midi.role=\"data\") for the contract '{contract}'.");
        }
        if (exchanges.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            errors.Add($"The envelopes of the contract '{contract}' declare different exchange protocols: {String.Join(", ", exchanges)}.");
        }

        var inferred = replies.Any(r => r.Kind is AddressMessageKind.Ack or AddressMessageKind.Nak) ? Acknowledged : OneWay;
        var addressSpace = AddressSpaceOf(requests.Concat(dataMessages), errors);
        return new AddressProtocol(serializer, contract, addressSpace, exchanges.FirstOrDefault() ?? inferred,
            requests, dataMessages, replies, bodies);
    }

    /// <summary>
    /// A copy with the settings of a map root applied (<c>midi.unit</c>, <c>midi.maxDataSize</c>,
    /// <c>midi.messageInterval</c>, <c>midi.exchange</c>).
    /// </summary>
    internal AddressProtocol WithOverrides(SchemaNode mapRoot, List<string> errors)
    {
        var unit = Unit(mapRoot, errors);
        var maxDataSize = Number(mapRoot, MidiProperties.MaxDataSize, errors);
        var interval = Number(mapRoot, MidiProperties.MessageInterval, errors);
        var exchange = MidiProperties.Find(mapRoot, MidiProperties.Exchange);
        if (unit is null && maxDataSize is null && interval is null && exchange is null)
        {
            return this;
        }

        AddressMessage Apply(AddressMessage message) => message with
        {
            Unit = unit ?? message.Unit,
            MaxDataSize = maxDataSize is { } size ? (int)size : message.MaxDataSize,
            Interval = interval is { } ms ? TimeSpan.FromMilliseconds(ms) : message.Interval,
        };

        // a unit override only applies to data messages: the requests keep what the device answers to
        return new AddressProtocol(_serializer, Contract, AddressSpace, exchange ?? Exchange,
            [.. Requests.Select(r => Apply(r) with { Unit = r.Unit })], [.. DataMessages.Select(Apply)], Replies, _bodies);
    }

    /// <summary>
    /// Recognises a reply of the device (ack, nak or error): the constants match and the device values in it agree with
    /// <paramref name="device"/>. Returns false for any other message.
    /// </summary>
    public bool TryReadReply(ReadOnlyMemory<byte> sysEx, DeviceValues device, out AddressMessage reply)
    {
        foreach (var candidate in Replies)
        {
            try
            {
                var sink = new EnvelopeSink(_ => throw new InvalidOperationException($"'{candidate.Name}': a reply carries no data."));
                var read = _serializer.Deserialize(candidate.Range, new ReadOnlySequence<byte>(sysEx), sink);
                if (read == sysEx.Length && sink.Device.All(v => !device.TryGet(v.Key, out var expected) || expected == v.Value))
                {
                    reply = candidate;
                    return true;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // not this reply
            }
        }
        reply = null!;
        return false;
    }

    private static bool IsReply(AddressMessageKind kind)
        => kind is AddressMessageKind.Ack or AddressMessageKind.Nak or AddressMessageKind.Error;

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

    /// <summary>A request for the addresses, as a complete SysEx message.</summary>
    public byte[] CreateRequest(AddressMessage request, AddressRange addresses, DeviceValues device)
    {
        if (request.Kind != AddressMessageKind.Request)
        {
            throw new ArgumentException($"'{request.Name}' is not a request.", nameof(request));
        }

        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(request.Range, new EnvelopeSource(addresses, device), writer);
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

    private static bool CarriesContract(SchemaGroup group, string documentName, SchemaName contract)
        => group.Members.Any(member => member switch
        {
            SchemaChoice { MemberNodeDef: { } nodeDef } choice
                when IsRole(choice, MidiProperties.RoleData) && SameName(Qualify(nodeDef, documentName), contract) => true,
            SchemaGroup child => CarriesContract(child, documentName, contract),
            _ => false,
        });

    private static void CollectMessages(ExecutionPlan plan, GroupInfo group, string documentName, SchemaName contract,
        List<AddressMessage> messages, List<string> errors)
    {
        foreach (var member in group.Members.OfType<GroupInfo>())
        {
            if (MidiProperties.Find(member, MidiProperties.Message) is not { } kindText)
            {
                CollectMessages(plan, member, documentName, contract, messages, errors);
                continue;
            }

            if (!Enum.TryParse<AddressMessageKind>(kindText, ignoreCase: true, out var kind))
            {
                errors.Add($"{member.Path}: '{MidiProperties.Message}' must be request, data, ack, nak or error, not '{kindText}'.");
                continue;
            }

            var roles = new Dictionary<string, NodeInfo>(StringComparer.OrdinalIgnoreCase);
            CollectRoles(member, roles, errors);

            if (kind == AddressMessageKind.Data)
            {
                // a data message of another contract is not ours
                if (!roles.TryGetValue(MidiProperties.RoleData, out var data)
                    || data is not ChoiceInfo { IsOpen: true } choice
                    || !SameName(Qualify(((SchemaChoice)choice.Group).MemberNodeDef!.Value, documentName), contract))
                {
                    continue;
                }
            }

            var message = new AddressMessage
            {
                Name = member.Path,
                Kind = kind,
                Unit = Unit(member.Group, errors) ?? AddressUnit.Range,
                MaxDataSize = (int)(Number(member.Group, MidiProperties.MaxDataSize, errors) ?? 0),
                Interval = TimeSpan.FromMilliseconds(Number(member.Group, MidiProperties.MessageInterval, errors) ?? 0),
                Range = plan.CreateRange(member.Path),
                Roles = roles,
            };
            Validate(message, errors);
            messages.Add(message);
        }
    }

    private static void CollectRoles(GroupInfo group, Dictionary<string, NodeInfo> roles, List<string> errors)
    {
        foreach (var member in group.Members)
        {
            if (MidiProperties.Find(member, MidiProperties.Role) is { } role && !roles.TryAdd(role, member))
            {
                errors.Add($"{member.Path}: the role '{role}' is already used by '{roles[role].Path}'.");
            }
            if (member is GroupInfo child and not ChoiceInfo)
            {
                CollectRoles(child, roles, errors);
            }
        }
    }

    private static void Validate(AddressMessage message, List<string> errors)
    {
        if (IsReply(message.Kind))
        {
            // a reply only identifies itself (and the device): no address, size or data
            foreach (var role in new[] { MidiProperties.RoleAddressStart, MidiProperties.RoleAddressSize, MidiProperties.RoleData })
            {
                if (message.Roles.ContainsKey(role))
                {
                    errors.Add($"{message.Name}: a reply ({message.Kind}) has no role '{role}'.");
                }
            }
            return;
        }

        if (!message.Roles.TryGetValue(MidiProperties.RoleAddressStart, out var address) || address is not FieldInfo)
        {
            errors.Add($"{message.Name}: the message has no field with the role '{MidiProperties.RoleAddressStart}'.");
        }

        var hasSize = message.Roles.ContainsKey(MidiProperties.RoleAddressSize);
        if (message.Kind == AddressMessageKind.Request && message.Unit == AddressUnit.Range && !hasSize)
        {
            errors.Add($"{message.Name}: a request for a range needs a field with the role '{MidiProperties.RoleAddressSize}' (or a '{MidiProperties.Unit}').");
        }
        if (hasSize && (message.Kind == AddressMessageKind.Data || message.Unit != AddressUnit.Range))
        {
            errors.Add($"{message.Name}: the role '{MidiProperties.RoleAddressSize}' is only used on a request for a range.");
        }
        if (message.Kind == AddressMessageKind.Request && message.Roles.ContainsKey(MidiProperties.RoleData))
        {
            errors.Add($"{message.Name}: a request carries no data (role '{MidiProperties.RoleData}').");
        }
    }

    private static AddressSpace AddressSpaceOf(IEnumerable<AddressMessage> messages, List<string> errors)
    {
        AddressSpace? space = null;
        foreach (var message in messages)
        {
            if (!message.Roles.TryGetValue(MidiProperties.RoleAddressStart, out var node) || node is not FieldInfo field)
            {
                continue;
            }
            if (field.ByteLength is not int byteCount)
            {
                errors.Add($"{field.Path}: the address field needs a constant byteLength (the number of address bytes).");
                continue;
            }

            var bits = Number(field.Field, MidiProperties.AddressBits, errors) is { } b ? (int)b : 7;
            var current = new AddressSpace(byteCount, bits);
            if (space is { } previous && previous != current)
            {
                errors.Add($"{field.Path}: the address ({byteCount} x {bits} bits) differs from the other messages ({previous.ByteCount} x {previous.BitsPerByte} bits).");
                continue;
            }
            space = current;
        }
        return space ?? new AddressSpace(0);
    }

    private static AddressUnit? Unit(SchemaNode node, List<string> errors)
    {
        if (MidiProperties.Find(node, MidiProperties.Unit) is not { } text)
        {
            return null;
        }
        if (Enum.TryParse<AddressUnit>(text, ignoreCase: true, out var unit))
        {
            return unit;
        }
        errors.Add($"{node.Name}: '{MidiProperties.Unit}' must be range, field or atomic, not '{text}'.");
        return null;
    }

    private static long? Number(SchemaNode node, string property, List<string> errors)
    {
        if (MidiProperties.Find(node, property) is not { } text)
        {
            return null;
        }
        if (AddressSpace.TryParseNumber(text, out var value) && value <= Int32.MaxValue)
        {
            return (long)value;
        }
        errors.Add($"{node.Name}: '{property}' has no valid number: '{text}'.");
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
