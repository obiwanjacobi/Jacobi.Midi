namespace Jacobi.Midi.Console.Core.Libraries;

/// <summary>
/// A library of user controls: one file with many <see cref="UserControlDefinition"/>s, referred to by the rig. Its
/// user controls are placed on dashboards as copies, so a dashboard does not depend on the library.
/// </summary>
public sealed class ControlLibrary
{
    /// <summary>The user controls, each with a unique name (case-insensitive).</summary>
    public IReadOnlyList<UserControlDefinition> Controls { get; private set; } = [];

    /// <summary>The user control with the name (case-insensitive), or null.</summary>
    public UserControlDefinition? Find(string name)
        => Controls.FirstOrDefault(control => String.Equals(control.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Adds the user control, or replaces the one with the same name (case-insensitive) in its place.
    /// </summary>
    /// <returns>True when a user control was replaced.</returns>
    public bool Put(UserControlDefinition control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var existing = Find(control.Name);
        Controls = existing is null
            ? [.. Controls, control]
            : [.. Controls.Select(other => other == existing ? control : other)];
        return existing is not null;
    }
}
