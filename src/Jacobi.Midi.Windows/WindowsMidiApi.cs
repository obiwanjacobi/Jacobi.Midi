using System.Runtime.InteropServices;
using Jacobi.Midi.Core;
using Native = Windows.Devices.Midi2;

namespace Jacobi.Midi.Windows;

/// <summary>
/// Windows MIDI Services implementation of the platform-neutral MIDI API.
/// </summary>
public sealed class WindowsMidiApi : IMidiApi
{
    public ulong TimestampFrequency => Native.MidiClock.TimestampFrequency;

    public MidiTimestamp GetTimestamp() => new(Native.MidiClock.Now);

    public Task<bool> EnsureIsAvailable() => Task.Run(() =>
    {
        try
        {
            return Native.MidiApi.EnsureServiceAvailable()
                && Native.MidiApi.GetCurrentlySelectedApiMode() == Native.MidiApiMode.FullWindowsMidiServicesMode;
        }
        catch (COMException error) when (error.HResult == unchecked((int)0x80040154))
        {
            return false;
        }
    });

    public async Task<IMidiSession> CreateSession(MidiSessionOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Name);
        ct.ThrowIfCancellationRequested();
        await RequireAvailable().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var native = Native.MidiSession.Create(options.Name);
        if (native is null)
            throw new MidiException("Windows MIDI Services could not create the session.");

        if (!native.IsOpen || ct.IsCancellationRequested)
        {
            native.Dispose();
            ct.ThrowIfCancellationRequested();
            throw new MidiException("Windows MIDI Services returned a closed session.");
        }

        return new WindowsMidiSession(native);
    }

    public async Task<IMidiEndpointObserver> WatchMidiEndpoints(EndpointChangedHandler handler, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ct.ThrowIfCancellationRequested();
        await RequireAvailable().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return await WindowsMidiEndpointObserver.Create(handler, ct).ConfigureAwait(false);
    }

    private async Task RequireAvailable()
    {
        if (!await EnsureIsAvailable().ConfigureAwait(false))
            throw new MidiException("Windows MIDI Services is not available in full service mode. Install or enable Windows MIDI Services before opening sessions or watching endpoints.");
    }
}
