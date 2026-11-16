namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// Which way data may be transferred for a node (<c>midi.access</c>). Inherited by the members of a node.
/// </summary>
public enum NodeAccess
{
    ReadWrite,
    /// <summary>The device only transmits the data (e.g. Yamaha System Information, a Property Exchange resource that cannot be set).</summary>
    Read,
    /// <summary>The device only receives the data (trigger addresses such as XG System On).</summary>
    Write,
}
