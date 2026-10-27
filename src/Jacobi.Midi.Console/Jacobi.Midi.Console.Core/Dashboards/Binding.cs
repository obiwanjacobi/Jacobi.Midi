using Jacobi.BinarySerializer.Execution;

namespace Jacobi.Midi.Console.Core.Dashboards;

/// <summary>
/// What fills a slot of a parameter control, as written in the dashboard: a schema path and the instance for a
/// parameter inside a repeat. Without its own <see cref="Device"/> the path is relative to the nearest base and the
/// device is the nearest one of the control or the layout controls around it (see <see cref="BindingResolver"/>).
/// </summary>
/// <param name="Path">The schema path: relative to the nearest base, or from the map root when there is none.</param>
public sealed record Binding(string Path)
{
    /// <summary>
    /// The device alias of this binding. With it, <see cref="Path"/> is a full path from the map root: a base belongs
    /// to the device it was given with.
    /// </summary>
    public string? Device { get; init; }

    /// <summary>The repeat items leading to the parameter; empty when it is not inside a repeat.</summary>
    public InstancePath Instance { get; init; }
}

/// <summary>
/// The identity of a device parameter: the device alias, the full schema path from the map root and the instance.
/// Device state and synchronisation work with this identity.
/// </summary>
public sealed record ParameterReference(string Device, string Path, InstancePath Instance);
