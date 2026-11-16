namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// How a group or repeat is cut into blocks (<c>midi.transfer</c>), at its own level: its members (the items of a
/// repeat). Without it, the unit and maximum data size of the message decide.
/// </summary>
internal enum NodeTransfer
{
    /// <summary>Never cut inside: the node is always transferred as a whole.</summary>
    Atomic,
    /// <summary>Always cut at the boundaries of its members (and its own start and end): each is transferred on its own.</summary>
    Individual,
}
