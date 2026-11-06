using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// Creates the <see cref="AddressMap"/> of a root. For now it is the only provider and claims every root: a root that
/// is not a valid address map is reported with an <see cref="AddressMapException"/>. When the default dump provider
/// is added, it claims only the roots with address tags.
/// </summary>
internal sealed class AddressMapProvider : IMessageMapProvider
{
    public IMessageMap? TryCreate(Serializer serializer, ExecutionPlan plan)
        => AddressMap.Create(serializer, plan);
}
