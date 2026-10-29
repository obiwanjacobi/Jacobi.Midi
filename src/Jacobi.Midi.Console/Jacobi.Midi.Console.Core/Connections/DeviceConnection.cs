using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Core;

namespace Jacobi.Midi.Console.Core.Connections;

/// <summary>
/// Where the connection of a device of the rig stands (see <see cref="RigConnections"/>).
/// </summary>
public enum DeviceConnectionStatus
{
    /// <summary>The device has no endpoint: it is not connected to MIDI.</summary>
    NoEndpoint,
    /// <summary>No endpoint has the name of the device's endpoint; the device connects when it appears.</summary>
    EndpointNotFound,
    /// <summary>The connection is being opened.</summary>
    Connecting,
    /// <summary>Connected: the device can be exchanged with.</summary>
    Online,
    /// <summary>The connection was lost (e.g. the cable was pulled); the device connects again when its endpoint comes back.</summary>
    Lost,
    /// <summary>The connection could not be opened, or the device cannot be connected (e.g. the schemas do not compile).</summary>
    Failed,
}

/// <summary>
/// The connection of one device of the rig as it is now: its endpoint (as the rig names it, and the endpoint found
/// for it), its status and why it is not online.
/// </summary>
/// <param name="Alias">The device's alias in the rig.</param>
/// <param name="Endpoint">The endpoint of the device in the rig; null when it has none.</param>
/// <param name="Status">Where the connection stands.</param>
/// <param name="EndpointInfo">The endpoint found by the name; null when none is found.</param>
/// <param name="Message">Why the device is not online (lost or failed); null otherwise.</param>
public sealed record DeviceConnection(string Alias, EndpointReference? Endpoint, DeviceConnectionStatus Status,
    MidiEndpointInfo? EndpointInfo = null, string? Message = null)
{
    public bool IsOnline => Status == DeviceConnectionStatus.Online;

    /// <summary>The status in words, with the reason when there is one.</summary>
    public string Description
    {
        get
        {
            var text = Status switch
            {
                DeviceConnectionStatus.NoEndpoint => "no endpoint",
                DeviceConnectionStatus.EndpointNotFound => $"endpoint '{Endpoint?.Name}' not found",
                DeviceConnectionStatus.Connecting => "connecting…",
                DeviceConnectionStatus.Online => $"online on '{EndpointInfo?.Name ?? Endpoint?.Name}', {Endpoint?.GroupsText}",
                DeviceConnectionStatus.Lost => "connection lost",
                DeviceConnectionStatus.Failed => "not connected",
                _ => Status.ToString(),
            };
            return Message is null ? text : $"{text}: {Message}";
        }
    }
}
