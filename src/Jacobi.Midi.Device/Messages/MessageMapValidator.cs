using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// Validates the use of the <c>midi.*</c> tags in all schemas of a serializer, for tools that show the problems while a
/// schema is written (e.g. the Schema Editor): the tags on their own (<see cref="MidiProperties.Validate"/>), the roles
/// in their context, the message map of every root, whatever its kind (the provider chain decides: an address map with
/// its envelopes, a dump carried by a data message, a dump that is a message itself), as when the map is first used,
/// and whether each role is fulfilled: a data message needs a root that conforms to its contract, and a
/// <c>device.&lt;name&gt;</c> role is either fixed by the root (<c>midi.device.&lt;name&gt;</c>) or supplied by the client.
/// </summary>
public static class MessageMapValidator
{
    /// <summary>All problems found, without duplicates; empty when the schemas have no tags or no problems.</summary>
    public static IReadOnlyList<MessageMapProblem> Validate(Serializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        var problems = new MessageMapProblems();
        var dataChoices = new List<(string Document, SchemaPath Path, SchemaName Contract)>();
        foreach (var document in serializer.Schemas.Documents)
        {
            foreach (var root in document.Roots)
            {
                ValidateTags(document.Name, root, new SchemaPath(root.Name), dataChoices, problems);
            }
        }

        var maps = new MessageMaps(serializer);
        var mapRoots = DeviceMessages.FindCarriedRoots(serializer.Schemas);
        foreach (var contract in dataChoices.Select(choice => choice.Contract).Distinct())
        {
            var roots = mapRoots.Where(root => ConformsTo(serializer.Schemas, root, contract)).ToList();
            if (roots.Count == 0)
            {
                foreach (var (document, path, _) in dataChoices.Where(choice => choice.Contract == contract))
                {
                    problems.Information(document, path,
                        $"no group conforms to the contract '{contract}' (a root with nodeDef=\"{contract}\"): the roles of this message are not fulfilled by a map root.");
                }
                // without a root, only the envelopes of an address map can be checked (as the messages of a map)
                Guarded(problems, () =>
                {
                    if (AddressMapProvider.CarriesAddress(serializer, contract))
                    {
                        AddressProtocol.Create(serializer.Schemas, serializer, contract, problems);
                    }
                });
                continue;
            }
            foreach (var root in roots)
            {
                ValidateMap(maps, root, contract, problems);
            }
        }

        // the dumps that are a message themselves (no data message carries them)
        foreach (var document in serializer.Schemas.Documents)
        {
            foreach (var group in document.Roots)
            {
                var root = new SchemaName($"{document.Name}{SchemaName.Separator}{group.Name}");
                bool isMessageDump;
                try
                {
                    isMessageDump = DumpMap.IsMessageDump(serializer, serializer.GetPlan(root));
                }
                catch (Exception exception) when (exception is ExecutionPlanException or InvalidOperationException or ArgumentException)
                {
                    // a root without a plan is no message (the plan's problems are not message map problems)
                    continue;
                }
                if (isMessageDump)
                {
                    ValidateMap(maps, root, contract: null, problems);
                }
            }
        }
        return [.. problems.Distinct()];
    }

    private static void ValidateTags(string document, SchemaNode node, SchemaPath path,
        List<(string, SchemaPath, SchemaName)> dataChoices, MessageMapProblems problems)
    {
        foreach (var property in node.Properties.Where(p => MidiProperties.IsTag(p.Name)))
        {
            if (MidiProperties.Validate(property.Name, property.Value) is { } problem)
            {
                problems.Error(document, path, problem);
            }
        }

        if (MidiProperties.RoleOf(node) is { } role && MidiProperties.IsRole(role))
        {
            var isData = String.Equals(role, MidiProperties.RoleData, StringComparison.OrdinalIgnoreCase);
            if (isData && node is SchemaChoice { MemberNodeDef: { } contract })
            {
                dataChoices.Add((document, path, String.IsNullOrEmpty(contract.Namespace) ? new SchemaName($"{document}.{contract.Name}") : contract));
            }
            else if (isData)
            {
                problems.Error(document, path, $"the role '{role}' only applies to an open choice (with a memberNodeDef).");
            }
            else if (node is not SchemaField)
            {
                problems.Error(document, path, $"the role '{role}' only applies to fields.");
            }
        }

        if (node is SchemaGroup group)
        {
            foreach (var member in group.Members)
            {
                ValidateTags(document, member, path.Append(member.Name), dataChoices, problems);
            }
        }
    }

    // the map (of whatever kind its root is: the provider chain decides) with its envelopes, and how its device roles are fulfilled
    // contract: of the data messages that carry the root; null for a root that is a message itself
    private static void ValidateMap(MessageMaps maps, SchemaName root, SchemaName? contract, MessageMapProblems problems)
    {
        IMessageMap map;
        try
        {
            map = maps.Get(maps.Serializer.GetPlan(root));
        }
        catch (MessageMapException exception)
        {
            problems.AddRange(exception.Problems);
            return;
        }
        catch (Exception exception) when (exception is ExecutionPlanException or InvalidOperationException or ArgumentException)
        {
            problems.Error(root.Namespace, new SchemaPath(root.Name), $"the message map cannot be built: {exception.Message}");
            return;
        }

        var messages = map.DataMessages;
        foreach (var message in messages)
        {
            var document = message.Range.ExecutionPlan.Name?.Namespace ?? String.Empty;
            foreach (var (role, node) in message.Roles)
            {
                if (role.StartsWith(MidiProperties.RoleDevicePrefix, StringComparison.OrdinalIgnoreCase)
                    && !map.Device.TryGet(role[MidiProperties.RoleDevicePrefix.Length..], out _))
                {
                    problems.Information(document, node.Path,
                        $"the role '{role}' is not fixed by the map '{root}' ({MidiProperties.DevicePrefix}{role[MidiProperties.RoleDevicePrefix.Length..]}): the client supplies the value.");
                }
            }
        }

        var used = messages.SelectMany(message => message.DeviceValueNames).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _) in map.Device.Where(value => !used.Contains(value.Key)))
        {
            problems.Warning(root.Namespace, new SchemaPath(root.Name),
                contract is { } carriers
                    ? $"'{MidiProperties.DevicePrefix}{name}' is not used: no message of the contract '{carriers}' has the role '{MidiProperties.RoleDevicePrefix}{name}'."
                    : $"'{MidiProperties.DevicePrefix}{name}' is not used: the message has no role '{MidiProperties.RoleDevicePrefix}{name}'.");
        }
    }

    private static bool ConformsTo(SchemaSet schemas, SchemaName root, SchemaName contract)
    {
        var group = schemas.FindRoot(root);
        if (group.NodeDef is not { } nodeDef)
        {
            return false;
        }
        var qualified = String.IsNullOrEmpty(nodeDef.Namespace) ? new SchemaName($"{root.Namespace}.{nodeDef.Name}") : nodeDef;
        return qualified == contract;
    }

    private static void Guarded(MessageMapProblems problems, Action validate)
    {
        try
        {
            validate();
        }
        catch (Exception exception) when (exception is ExecutionPlanException or InvalidOperationException or ArgumentException)
        {
            problems.Error(String.Empty, default, $"the envelopes cannot be validated: {exception.Message}");
        }
    }
}
