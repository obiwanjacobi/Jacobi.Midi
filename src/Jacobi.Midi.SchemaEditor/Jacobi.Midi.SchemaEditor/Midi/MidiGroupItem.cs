using Jacobi.Midi.Core;

namespace Jacobi.Midi.SchemaEditor.Midi;

/// <summary>
/// One UMP group in a group list (send or receive) of the <see cref="MidiBarViewModel"/>: its display number (1-16)
/// and the name the selected endpoint declares for it in that direction (function block or group terminal block).
/// </summary>
internal sealed record MidiGroupItem(MidiGroup Group, string? Name)
{
    /// <summary>
    /// The one-based number shown to the user.
    /// </summary>
    public int Number => Group.Value + 1;

    public bool HasName => Name is not null;
}
