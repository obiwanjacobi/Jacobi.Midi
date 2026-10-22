using System.Collections.Concurrent;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// Deduces the address map of a plan from the schemas on first use and caches it per plan, so a client only needs a
/// <see cref="PlanRange"/>. <see cref="Prepare"/> and <see cref="PrepareAll"/> build and validate maps up front so
/// schema errors surface early. Thread-safe.
/// </summary>
internal sealed class AddressMaps(Serializer serializer)
{
    private readonly ConcurrentDictionary<ExecutionPlan, Lazy<AddressMap>> _maps = new(ReferenceEqualityComparer.Instance);

    public Serializer Serializer { get; } = serializer ?? throw new ArgumentNullException(nameof(serializer));

    /// <summary>The map of the plan the range belongs to; built and validated on first use.</summary>
    public AddressMap Get(PlanRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        return Get(range.ExecutionPlan);
    }

    /// <summary>The map of a plan; built and validated on first use.</summary>
    public AddressMap Get(ExecutionPlan plan)
        => _maps.GetOrAdd(plan, p => new Lazy<AddressMap>(() => AddressMap.Create(Serializer, p))).Value;

    /// <summary>Builds and validates the map of an address-map root; throws an <see cref="AddressMapException"/> when it is not valid.</summary>
    public void Prepare(SchemaName root)
        => Get(Serializer.GetPlan(root));

    /// <summary>
    /// Builds and validates the maps of all address-map roots in the schemas (the groups a data message can carry);
    /// throws an <see cref="AddressMapException"/> for the first one that is not valid.
    /// </summary>
    public void PrepareAll()
    {
        foreach (var root in AddressProtocol.FindMapRoots(Serializer.Schemas))
        {
            Prepare(root);
        }
    }
}
