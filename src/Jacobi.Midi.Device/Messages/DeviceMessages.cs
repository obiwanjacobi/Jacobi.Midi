using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// Finds the message groups of the schemas and reads and writes the messages that carry no data. Generic for every
/// kind of map: the data messages of a root are found through their open choice with <c>midi.role="data"</c> (with
/// the open-choice lookups of the serializer), any other message by its schema name. What a message means for a root
/// (e.g. its address settings) is up to the map.
/// </summary>
internal static class DeviceMessages
{
    /// <summary>
    /// The groups of the schemas that are data messages of a contract: in an envelope root that holds an open choice
    /// with <c>midi.role="data"</c> whose memberNodeDef is the contract, the member group of the root that holds the
    /// choice, or the root itself when the choice is one of its members.
    /// </summary>
    public static IReadOnlyList<(ExecutionPlan Plan, GroupInfo Group, string Document)> FindDataMessageGroups(Serializer serializer, SchemaName contract)
    {
        var schemas = serializer.Schemas;
        var groups = new List<(ExecutionPlan Plan, GroupInfo Group, string Document)>();
        var choices = schemas.FindOpenChoices(contract).Where(choice => IsRole(choice, MidiProperties.RoleData)).ToList();
        foreach (var document in choices.Select(choice => DocumentOf(schemas, choice)).Distinct())
        {
            foreach (var root in document.Roots)
            {
                var plan = serializer.GetPlan(new SchemaName($"{document.Name}.{root.Name}"));
                groups.AddRange(DataMessageGroups(plan, choices).Select(group => (plan, group, document.Name)));
            }
        }
        return groups;
    }

    /// <summary>
    /// The roots of the schemas that a data message can carry: the roots whose nodeDef is the contract (memberNodeDef)
    /// of an open choice with <c>midi.role="data"</c>.
    /// </summary>
    public static IReadOnlyList<SchemaName> FindCarriedRoots(SchemaSet schemas)
    {
        var roots = new List<SchemaName>();
        foreach (var document in schemas.Documents)
        {
            foreach (var root in document.Roots)
            {
                if (schemas.FindOpenChoices(root).Any(choice => IsRole(choice, MidiProperties.RoleData)))
                {
                    roots.Add(new SchemaName($"{document.Name}.{root.Name}"));
                }
            }
        }
        return roots;
    }

    /// <summary>The message group with a schema name (document, root and path, e.g. 'Roland.Message16.RQ1').</summary>
    /// <exception cref="InvalidOperationException">There is no such group, or the node is not a group.</exception>
    public static (ExecutionPlan Plan, GroupInfo Group, string Document) FindMessageGroup(Serializer serializer, SchemaName name)
    {
        var fullName = name.FullName;
        foreach (var document in serializer.Schemas.Documents
            .Where(document => fullName.StartsWith(document.Name + SchemaName.Separator, StringComparison.OrdinalIgnoreCase)))
        {
            var path = new SchemaPath(fullName[(document.Name.Length + 1)..]);
            var rootName = path.Value.Split(SchemaName.Separator)[0];
            if (!document.Roots.Any(root => String.Equals(root.Name, rootName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var plan = serializer.GetPlan(new SchemaName($"{document.Name}.{rootName}"));
            var node = plan.Find(path);
            if (node is null)
            {
                break;
            }
            if (node is not GroupInfo group || group is ChoiceInfo or RepeatInfo)
            {
                throw new InvalidOperationException($"'{name}' is not a message: it is not a group.");
            }
            return (plan, group, document.Name);
        }
        throw new InvalidOperationException($"There is no message '{name}' in the schemas (name it as document.root.group, e.g. 'Roland.Message16.RQ1').");
    }

    /// <summary>The message of a group, with the nodes that have a role; a role used twice is added to <paramref name="problems"/>.</summary>
    public static DeviceMessage Create(ExecutionPlan plan, GroupInfo group, string document, AddressMapProblems problems)
    {
        var roles = new Dictionary<string, NodeInfo>(StringComparer.OrdinalIgnoreCase);
        CollectRoles(document, group, roles, problems);
        return new DeviceMessage
        {
            Name = group.Path,
            Range = plan.CreateRange(group.Path),
            Roles = roles,
        };
    }

    /// <summary>
    /// A message without data as a complete SysEx message: the roles that depend on a block from
    /// <paramref name="blockRoles"/>, the device values from <paramref name="device"/>.
    /// </summary>
    public static byte[] WriteWithoutData(Serializer serializer, DeviceMessage message, IReadOnlyDictionary<string, long> blockRoles, DeviceValues device)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsData)
        {
            throw new ArgumentException($"'{message.Name}' carries data: it is built with the data of a block.", nameof(message));
        }

        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(message.Range, new EnvelopeSource(blockRoles, device), writer);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Recognises a message without data (e.g. an acknowledgement): the constants match and the device values in it
    /// agree with <paramref name="device"/>. Returns false for any other message.
    /// </summary>
    public static bool TryReadWithoutData(Serializer serializer, DeviceMessage message, ReadOnlyMemory<byte> sysEx, DeviceValues device)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            var sink = new EnvelopeSink(_ => throw new InvalidOperationException($"'{message.Name}': the message carries no data."));
            var read = serializer.Deserialize(message.Range, new ReadOnlySequence<byte>(sysEx), sink);
            return read == sysEx.Length && sink.Device.All(v => !device.TryGet(v.Key, out var expected) || expected == v.Value);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // not this message
            return false;
        }
    }

    /// <summary>True when the node has the <c>midi.role</c> <paramref name="role"/>.</summary>
    public static bool IsRole(SchemaNode node, string role)
        => String.Equals(MidiProperties.Find(node, MidiProperties.Role), role, StringComparison.OrdinalIgnoreCase);

    /// <summary>The document that holds a node of the schemas.</summary>
    public static SchemaDocument DocumentOf(SchemaSet schemas, SchemaNode node)
        => schemas.FindDocument(node) ?? throw new InvalidOperationException($"'{node.Name}' is not part of the schemas.");

    // per data open choice in the root's own tree (not in a body), the member group of the root that holds it, or the
    // root itself when the choice is one of its members (then it is the only message of the root)
    private static List<GroupInfo> DataMessageGroups(ExecutionPlan plan, IReadOnlyList<SchemaChoice> dataChoices)
    {
        var groups = new List<GroupInfo>();
        foreach (var choice in plan.OpenChoices.Where(choice => dataChoices.Any(data => ReferenceEquals(data, choice.Group))))
        {
            if (!ReferenceEquals(plan.Find(choice.Path), choice))
            {
                // an open choice inside a body of the plan: it does not make the root a message
                continue;
            }

            var segments = choice.Path.Segments.ToList();
            var group = segments.Count == 2
                ? plan.Root
                : plan.Find(new SchemaPath($"{segments[0]}{SchemaPath.Separator}{segments[1]}")) as GroupInfo;
            if (group is not null and not ChoiceInfo && !groups.Contains(group))
            {
                groups.Add(group);
            }
        }
        return groups.Contains(plan.Root) ? [plan.Root] : groups;
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
}
