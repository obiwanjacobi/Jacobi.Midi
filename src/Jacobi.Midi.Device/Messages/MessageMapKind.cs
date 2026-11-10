namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// How the data of a root is carried in messages: the kind of its message map (see <see cref="MessageMapInfo.Kind"/>).
/// A client decides with it what to offer for a root (e.g. the parts of an address map); how the messages are found,
/// written and read stays with the map. A new kind of map (provider) adds a value.
/// </summary>
public enum MessageMapKind
{
    /// <summary>An address map: carried in parts (blocks), each located by its address (e.g. Roland RQ1/DT1).</summary>
    Address,

    /// <summary>
    /// A dump carried whole as the body of a data message's open choice (<c>midi.role="data"</c>); the device values
    /// in the message select the body.
    /// </summary>
    CarriedDump,

    /// <summary>A dump that is a message itself: the root is its own (only) data message.</summary>
    MessageDump,
}
