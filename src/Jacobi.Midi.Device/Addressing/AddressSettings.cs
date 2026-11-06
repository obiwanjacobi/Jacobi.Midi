namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// What one message covers (<c>midi.unit</c>): any contiguous addresses, exactly one field, or exactly one atomic group.
/// </summary>
public enum AddressUnit
{
    Range,
    Field,
    Atomic,
}

/// <summary>
/// The settings of a message for an address map: its unit (<c>midi.unit</c>) and the largest number of data bytes in
/// one message (<c>midi.maxDataSize</c>; 0 means no limit). Read from the message group and overridden per device by
/// the map root; they decide how the address map splits a range into blocks.
/// </summary>
public sealed record AddressSettings(AddressUnit Unit, int MaxDataSize);
