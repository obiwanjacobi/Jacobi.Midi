using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using Jacobi.Midi.Console.Core.Connections;
using Jacobi.Midi.Console.Core.Parameters;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Exchange;
using Jacobi.Midi.Messages;

namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// The MIDI side of the console, for the MIDI bar, the rig tree and the device panel: the connection of each device of
/// the rig (see <see cref="RigConnections"/>), the endpoints, and the tests of a device's connection. Edit mode is always
/// connected (to choose the endpoints and test the devices); Play mode has an explicit choice of where the values come
/// from: Offline (dump files) or MIDI (the devices). Without a MIDI backend (the browser host) nothing is connected.
/// What goes wrong is reported in the notifications.
/// </summary>
internal sealed class MidiViewModel : ViewModelBase, IAsyncDisposable
{
    private const string SessionName = "MIDI Console";

    private static readonly IBrush OnlineBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xC3, 0x8A));
    private static readonly IBrush ConnectingBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x40));
    private static readonly IBrush ProblemBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x5A));
    private static readonly IBrush NotConnectedBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));

    private readonly RigConnections? _connections;
    private readonly Action<string, string, bool> _notify;
    private bool _isMidi;
    private bool _isEditing;
    private bool _isBusy;

    /// <param name="api">The platform's MIDI backend; null when it has none.</param>
    /// <param name="notify">Tells the user what happened: what, the message and whether it is a problem.</param>
    public MidiViewModel(IMidiApi? api, Action<string, string, bool> notify)
    {
        _notify = notify;
        if (api is null)
            return;
        _connections = new RigConnections(api, SessionName);
        _connections.Changed += (_, _) => Dispatcher.UIThread.Post(OnConnectionsChanged);
    }

    /// <summary>MIDI can be used: the platform has a MIDI backend.</summary>
    public bool IsAvailable => _connections is not null;

    /// <summary>The choice of Play mode: the values come from the devices (MIDI); setting it connects or disconnects in Play mode.</summary>
    public bool IsMidi
    {
        get => _isMidi;
        set
        {
            if (value == _isMidi)
                return;
            _isMidi = value;
            OnConnectionsChanged();
            _ = Apply();
        }
    }

    /// <summary>The choice of Play mode: the values come from dump files.</summary>
    public bool IsOffline
    {
        get => !IsMidi;
        set => IsMidi = !value;
    }

    /// <summary>The devices are connected (or being connected): in Edit mode, and in Play mode with MIDI.</summary>
    public bool IsConnected => _connections?.IsStarted == true;

    /// <summary>MIDI is being started or stopped.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>The connections of the devices of the rig; none when not connected.</summary>
    public IReadOnlyList<DeviceConnection> Devices => _connections?.Devices ?? [];

    /// <summary>The connection of each device for the MIDI bar: its status as colour and text.</summary>
    public IReadOnlyList<ConnectionRow> Rows => [.. Devices.Select(connection => new ConnectionRow(connection.Alias, connection.Description, BrushOf(connection)))];

    /// <summary>The endpoints MIDI can reach; none when not connected.</summary>
    public IReadOnlyList<MidiEndpointInfo> Endpoints => _connections?.Endpoints ?? [];

    /// <summary>What the bar says: not connected (and why), or how many devices are online.</summary>
    public string Summary
    {
        get
        {
            if (!IsAvailable)
                return "Offline: MIDI is not available on this platform";
            if (IsBusy)
                return ShouldConnect ? "Starting MIDI…" : "Stopping MIDI…";
            if (!IsConnected)
                return _isEditing ? "MIDI is not started" : "Offline: values from dump files";
            var devices = Devices;
            return devices.Count == 0 ? "MIDI: no devices" : $"MIDI: {devices.Count(device => device.IsOnline)} of {devices.Count} devices online";
        }
    }

    /// <summary>The colour of the bar's indicator: all devices online, some, or none.</summary>
    public IBrush SummaryBrush
    {
        get
        {
            if (!IsConnected)
                return NotConnectedBrush;
            var devices = Devices;
            var online = devices.Count(device => device.IsOnline);
            return online == devices.Count && online > 0 ? OnlineBrush : online > 0 ? ConnectingBrush : ProblemBrush;
        }
    }

    /// <summary>The connections or the mode changed (on the UI thread).</summary>
    public event EventHandler? Changed;

    /// <summary>True when the controls of a device can be used in Play mode: always offline; with MIDI only when it is online.</summary>
    public bool IsUsable(string alias) => !IsMidi || _connections?.Find(alias)?.IsOnline == true;

    /// <summary>The connection of a device; null when not connected.</summary>
    public DeviceConnection? ConnectionOf(string alias) => IsConnected ? _connections?.Find(alias) : null;

    /// <summary>The colour of a connection status; grey when not connected.</summary>
    public static IBrush BrushOf(DeviceConnection? connection)
        => connection?.Status switch
        {
            DeviceConnectionStatus.Online => OnlineBrush,
            DeviceConnectionStatus.Connecting => ConnectingBrush,
            DeviceConnectionStatus.EndpointNotFound or DeviceConnectionStatus.Lost or DeviceConnectionStatus.Failed => ProblemBrush,
            _ => NotConnectedBrush,
        };

    /// <summary>Edit mode is always connected; Play mode as chosen (<see cref="IsMidi"/>).</summary>
    public void SetEditing(bool isEditing)
    {
        _isEditing = isEditing;
        OnConnectionsChanged();
        _ = Apply();
    }

    /// <summary>The rig was opened (again): its devices are connected, when MIDI is.</summary>
    public void SetRig(RigSession? rig)
    {
        if (_connections is null)
            return;
        _ = Update();

        async Task Update()
        {
            try
            {
                await _connections.Update(rig, CancellationToken.None);
            }
            catch (Exception exception)
            {
                // the MIDI backend fails with its own exception types
                Dispatcher.UIThread.Post(() => _notify("MIDI", exception.Message, true));
            }
        }
    }

    /// <summary>Sends an Identity Request on the device's endpoint and group; the replies (or none) in words. Changes nothing.</summary>
    public async Task<string> RequestIdentity(string alias)
    {
        if (_connections is null)
            return "MIDI is not available.";
        try
        {
            var replies = await _connections.RequestIdentity(alias, CancellationToken.None);
            return replies.Count == 0
                ? $"No Identity Reply within {RigConnections.IdentityWait.TotalSeconds:0.#} s: the device may not support it, or is not on this endpoint and group."
                : String.Join(Environment.NewLine, replies.Select(Describe));
        }
        catch (Exception exception)
        {
            _notify($"Identity Request to '{alias}'", exception.Message, true);
            return $"Failed: {exception.Message}";
        }
    }

    /// <summary>Checks that the device answers a request of the first parameter of its map; the outcome in words. Changes nothing.</summary>
    public async Task<string> ProbeData(string alias)
    {
        if (_connections is null)
            return "MIDI is not available.";
        try
        {
            var probe = await _connections.ProbeData(alias, CancellationToken.None);
            var blocks = probe.Result.Blocks;
            var outcome = probe.Result.IsComplete
                ? $"the device answered ({blocks.Count} block{(blocks.Count == 1 ? "" : "s")})"
                : String.Join("; ", blocks.Where(block => block.Status != BlockStatus.Completed)
                    .Select(block => (block.Reason ?? block.Error?.Message) is { } reason ? $"{block.Status}: {reason}" : block.Status.ToString()));
            return $"{probe.Path}{probe.Instance}: {outcome}";
        }
        catch (Exception exception)
        {
            _notify($"Data request to '{alias}'", exception.Message, true);
            return $"Failed: {exception.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connections is not null)
            await _connections.DisposeAsync();
    }

    private bool ShouldConnect => _isEditing || _isMidi;

    // starts or stops MIDI until it is as the modes want it; a mode switch while busy is taken in the next round
    private async Task Apply()
    {
        if (_connections is null || IsBusy)
            return;
        IsBusy = true;
        OnConnectionsChanged();
        try
        {
            while (ShouldConnect != _connections.IsStarted)
            {
                if (!ShouldConnect)
                {
                    await _connections.Stop();
                }
                else if (!await _connections.Start(CancellationToken.None))
                {
                    _notify("MIDI", "the MIDI service is not available.", true);
                    _isMidi = false;
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            _notify(ShouldConnect ? "Starting MIDI" : "Stopping MIDI", exception.Message, true);
            _isMidi = false;
        }
        finally
        {
            IsBusy = false;
            OnConnectionsChanged();
        }
    }

    private void OnConnectionsChanged()
    {
        OnPropertyChanged(nameof(IsMidi));
        OnPropertyChanged(nameof(IsOffline));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(Devices));
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Endpoints));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(SummaryBrush));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Describe(IdentityReplyMessage reply)
        => String.Create(CultureInfo.InvariantCulture,
            $"Manufacturer 0x{reply.ManufacturerId:X2}, family 0x{reply.DeviceFamily:X4}, member 0x{reply.DeviceFamilyMember:X4}, "
            + $"revision {Convert.ToHexString(reply.SoftwareRevision)}, device id 0x{reply.DeviceId:X2}");
}

/// <summary>
/// A device in the MIDI bar's list: its alias and connection status, in words and as a colour.
/// </summary>
internal sealed record ConnectionRow(string Alias, string Description, IBrush Brush);
