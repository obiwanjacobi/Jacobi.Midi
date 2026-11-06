using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// Creates the <see cref="AddressMap"/> of a root. It claims a root that has the tags it owns: an address-map tag
/// (<c>midi.address</c>, <c>midi.stride</c>, <c>midi.repeat</c>, <c>midi.atomic</c>, <c>midi.access</c>) on a node of
/// the root, or a data message with the role <c>address</c> that carries the root. A claimed root that is not a valid
/// address map is reported with a <see cref="MessageMapException"/>; it is never passed on to another provider.
/// </summary>
internal sealed class AddressMapProvider : IMessageMapProvider
{
    private static readonly string[] MapTags =
        [MidiProperties.Address, MidiProperties.Stride, MidiProperties.Repeat, MidiProperties.Atomic, MidiProperties.Access];

    public IMessageMap? TryCreate(Serializer serializer, ExecutionPlan plan)
        => Claims(serializer, plan) ? AddressMap.Create(serializer, plan) : null;

    private static bool Claims(Serializer serializer, ExecutionPlan plan)
    {
        if (HasMapTag(plan.Root))
        {
            return true;
        }
        if (plan.Name is not { } root || plan.Root.Group.NodeDef is not { } nodeDef)
        {
            return false;
        }
        var contract = nodeDef.Qualify(root.Namespace);
        return DeviceMessages.FindDataMessageGroups(serializer, contract).Any(message => HasAddressRole(message.Group));
    }

    private static bool HasMapTag(NodeInfo node)
        => MapTags.Any(tag => MidiProperties.Find(node, tag) is not null)
        || (node is GroupInfo group && group.Members.Any(HasMapTag));

    private static bool HasAddressRole(GroupInfo group)
        => group.Members.Any(member =>
            String.Equals(MidiProperties.Find(member, MidiProperties.Role), MidiProperties.RoleAddress, StringComparison.OrdinalIgnoreCase)
            || (member is GroupInfo child and not ChoiceInfo && HasAddressRole(child)));
}
