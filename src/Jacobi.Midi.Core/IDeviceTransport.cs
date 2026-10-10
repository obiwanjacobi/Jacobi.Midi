namespace Jacobi.Midi.Core;

/// <summary>
/// Abstract transport used by applications.
/// Implementations hide MIDI details (v1 SysEx or v2 UMP).
/// </summary>
public interface IDeviceTransport : IAsyncDisposable
{
    /// <summary>
    /// Connect to the device.
    /// </summary>
    Task Connect(CancellationToken ct);

    /// <summary>
    /// Disconnect from the device.
    /// </summary>
    Task Disconnect(CancellationToken ct);
}
