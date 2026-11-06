using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// The roots that conform to an open choice contract (the bodies a data message can carry), with the device values
/// each fixes (<c>midi.device.*</c>), and the selection of the body for the device values read from a message.
/// </summary>
internal sealed class ConformingBodies
{
    private readonly IReadOnlyList<(SchemaName Name, DeviceValues Device)> _bodies;

    private ConformingBodies(SchemaName contract, IReadOnlyList<(SchemaName Name, DeviceValues Device)> bodies)
    {
        Contract = contract;
        _bodies = bodies;
    }

    /// <summary>The open choice contract (the nodeDef the bodies conform to).</summary>
    public SchemaName Contract { get; }

    /// <summary>Finds the roots that conform to the contract (with the serializer's lookup); invalid device values are added to <paramref name="problems"/>.</summary>
    public static ConformingBodies Find(SchemaSet schemas, SchemaName contract, MessageMapProblems problems)
    {
        var bodies = new List<(SchemaName Name, DeviceValues Device)>();
        foreach (var body in schemas.FindConformingGroups(contract))
        {
            var document = DeviceMessages.DocumentOf(schemas, body).Name;
            bodies.Add((new SchemaName($"{document}.{body.Name}"), DeviceValues.FromProperties(body, document, new SchemaPath(body.Name), problems)));
        }
        return new ConformingBodies(contract, bodies);
    }

    /// <summary>
    /// Selects the body for the device values read from a message: of the roots that conform to the contract, the one
    /// whose <c>midi.device.*</c> values all match. None or more than one is an error.
    /// </summary>
    public SchemaName Select(DeviceValues values)
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

    private static string Format(DeviceValues values)
        => "{" + String.Join(", ", values.Select(v => $"{v.Key}=0x{v.Value:X}")) + "}";
}
