using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// Validates the use of the <c>midi.*</c> tags in all schemas of a serializer, for tools that show the problems while a
/// schema is written (e.g. the Schema Editor): the tags on their own (<see cref="AddressMapTags.Validate"/>), the roles
/// in their context, every address map with its envelopes (as when the map is first used), and whether each role is
/// fulfilled: a data message needs a map root that conforms to its contract, and a <c>device.&lt;name&gt;</c> role is
/// either fixed by the map root (<c>midi.device.&lt;name&gt;</c>) or supplied by the client.
/// </summary>
public static class AddressMapValidator
{
    /// <summary>All problems found, without duplicates; empty when the schemas have no tags or no problems.</summary>
    public static IReadOnlyList<AddressMapProblem> Validate(Serializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        var problems = new AddressMapProblems();
        var dataChoices = new List<(string Document, SchemaPath Path, SchemaName Contract)>();
        foreach (var document in serializer.Schemas.Documents)
        {
            foreach (var root in document.Roots)
            {
                ValidateTags(document.Name, root, new SchemaPath(root.Name), inMessage: false, dataChoices, problems);
            }
        }

        var mapRoots = AddressProtocol.FindMapRoots(serializer.Schemas);
        foreach (var contract in dataChoices.Select(choice => choice.Contract).Distinct())
        {
            var roots = mapRoots.Where(root => ConformsTo(serializer.Schemas, root, contract)).ToList();
            if (roots.Count == 0)
            {
                foreach (var (document, path, _) in dataChoices.Where(choice => choice.Contract == contract))
                {
                    problems.Information(document, path,
                        $"no group conforms to the contract '{contract}' (a root with nodeDef=\"{contract}\"): the roles of this message are not fulfilled by an address map.");
                }
                Guarded(problems, () => AddressProtocol.Create(serializer.Schemas, serializer, contract, problems));
                continue;
            }
            foreach (var root in roots)
            {
                ValidateMap(serializer, root, problems);
            }
        }
        return [.. problems.Distinct()];
    }

    private static void ValidateTags(string document, SchemaNode node, SchemaPath path, bool inMessage,
        List<(string, SchemaPath, SchemaName)> dataChoices, AddressMapProblems problems)
    {
        foreach (var property in node.Properties.Where(p => AddressMapTags.IsTag(p.Name)))
        {
            if (AddressMapTags.Validate(property.Name, property.Value) is { } problem)
            {
                problems.Error(document, path, problem);
            }
        }

        if (MidiProperties.Find(node, MidiProperties.Message) is not null)
        {
            if (node is not SchemaGroup || node is SchemaRepeat or SchemaChoice)
            {
                problems.Error(document, path, $"'{MidiProperties.Message}' only applies to groups.");
            }
            else if (path.Parent.IsEmpty)
            {
                problems.Error(document, path, $"'{MidiProperties.Message}' is not allowed on a root: a message is a group inside an envelope root.");
            }
            else if (inMessage)
            {
                problems.Error(document, path, $"'{MidiProperties.Message}' is not allowed inside another message.");
            }
            inMessage = true;
        }

        if (AddressMapTags.RoleOf(node) is { } role && AddressMapTags.IsRole(role))
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
            if (!inMessage)
            {
                problems.Warning(document, path, $"the role '{role}' is not used: the node is not inside a message ({MidiProperties.Message}).");
            }
        }

        if (node is SchemaGroup group)
        {
            foreach (var member in group.Members)
            {
                ValidateTags(document, member, path.Append(member.Name), inMessage, dataChoices, problems);
            }
        }
    }

    // the map with its envelopes, and how its device roles are fulfilled
    private static void ValidateMap(Serializer serializer, SchemaName root, AddressMapProblems problems)
    {
        AddressMap map;
        try
        {
            map = AddressMap.Create(serializer, root);
        }
        catch (AddressMapException exception)
        {
            problems.AddRange(exception.Problems);
            return;
        }
        catch (Exception exception) when (exception is ExecutionPlanException or InvalidOperationException or ArgumentException)
        {
            problems.Error(root.Namespace, new SchemaPath(root.Name), $"the address map cannot be built: {exception.Message}");
            return;
        }

        var protocol = map.Protocol;
        var messages = protocol.Requests.Concat(protocol.DataMessages).Concat(protocol.Replies).ToList();
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
                $"'{MidiProperties.DevicePrefix}{name}' is not used: no message of the contract '{protocol.Contract}' has the role '{MidiProperties.RoleDevicePrefix}{name}'.");
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

    private static void Guarded(AddressMapProblems problems, Action validate)
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
