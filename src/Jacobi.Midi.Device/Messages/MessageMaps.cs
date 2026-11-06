using System.Collections.Concurrent;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// The message maps of the roots of a serializer's schemas: the map of a plan is created on first use by the first
/// provider of a fixed, ordered chain that claims the root, and cached per plan, so a client only needs a
/// <see cref="PlanRange"/>. <see cref="Prepare"/> and <see cref="PrepareAll"/> create and validate maps up front so
/// schema errors surface early; <see cref="Validate"/> checks a transfer before anything is sent. Thread-safe.
/// </summary>
internal sealed class MessageMaps(Serializer serializer)
{
    // the providers in the order they get to claim a root
    private static readonly IReadOnlyList<IMessageMapProvider> Providers = [new AddressMapProvider(), new DumpMapProvider()];

    private readonly ConcurrentDictionary<ExecutionPlan, Lazy<IMessageMap>> _maps = new(ReferenceEqualityComparer.Instance);

    public Serializer Serializer { get; } = serializer ?? throw new ArgumentNullException(nameof(serializer));

    /// <summary>The map of the plan the range belongs to; created and validated on first use.</summary>
    public IMessageMap Get(PlanRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        return Get(range.ExecutionPlan);
    }

    /// <summary>The map of a plan; created and validated on first use.</summary>
    /// <exception cref="InvalidOperationException">No provider claims the root.</exception>
    public IMessageMap Get(ExecutionPlan plan)
        => _maps.GetOrAdd(plan, p => new Lazy<IMessageMap>(() => Create(p))).Value;

    /// <summary>Creates and validates the map of a root; throws the provider's exception when it is not valid.</summary>
    public void Prepare(SchemaName root)
        => Get(Serializer.GetPlan(root));

    /// <summary>
    /// Creates and validates the maps of all roots a data message can carry; throws the provider's exception for the
    /// first one that is not valid.
    /// </summary>
    public void PrepareAll()
    {
        foreach (var root in DeviceMessages.FindCarriedRoots(Serializer.Schemas))
        {
            Prepare(root);
        }
    }

    /// <summary>
    /// The problems that prevent transferring the range with the messages, split by <paramref name="splitBy"/>, for the
    /// device; empty when it can: the device values the messages need are known (the root's with the client's over
    /// them), and the map can split the range for the message.
    /// </summary>
    public IReadOnlyList<string> Validate(PlanRange range, DeviceMessage splitBy, IEnumerable<DeviceMessage> messages, DeviceValues device)
    {
        ArgumentNullException.ThrowIfNull(range);
        var map = Get(range);
        var problems = new List<string>();
        var missing = DeviceValues.Missing(messages, map.Device.With(device));
        if (missing.Count > 0)
        {
            problems.Add($"The device values {String.Join(", ", missing)} are required by the messages; supply them with the device.");
        }
        problems.AddRange(map.Validate(range, splitBy));
        return problems;
    }

    /// <summary>
    /// The device values of the map's root with the client's values over them; all values the data messages need must be known.
    /// </summary>
    /// <exception cref="InvalidOperationException">A device value the data messages need is not known.</exception>
    public static DeviceValues DeviceFor(IMessageMap map, DeviceValues client)
    {
        var device = map.Device.With(client);
        var missing = DeviceValues.Missing(map.DataMessages, device);
        return missing.Count == 0
            ? device
            : throw new InvalidOperationException($"The device values {String.Join(", ", missing)} are required by the messages of '{map.Root}'; supply them with the device.");
    }

    private IMessageMap Create(ExecutionPlan plan)
    {
        foreach (var provider in Providers)
        {
            if (provider.TryCreate(Serializer, plan) is { } map)
            {
                return map;
            }
        }
        throw new InvalidOperationException($"No message map can be made for '{plan.Name?.FullName ?? plan.Root.Path.Value}': no provider recognises the root.");
    }
}
