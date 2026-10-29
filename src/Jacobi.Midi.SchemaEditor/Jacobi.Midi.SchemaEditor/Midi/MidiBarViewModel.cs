using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.Core;

namespace Jacobi.Midi.SchemaEditor.Midi;

/// <summary>
/// View model behind <see cref="MidiBarControl"/>.
/// Lists the available MIDI endpoints, holds the selected endpoint and its UMP groups (to send on and to receive on)
/// and starts/stops the connection to the selected endpoint.
/// </summary>
/// <remarks>
/// When no <see cref="IMidiApi"/> is provided (e.g. in the browser host) the bar stays disabled.
/// Midi.Core callbacks are marshalled to the UI thread before touching bound state.
/// </remarks>
internal sealed partial class MidiBarViewModel : ObservableObject, IAsyncDisposable
{
    private const string SessionName = "MIDI Schema Studio";

    private readonly MidiConnectionService? _service;
    private Task? _initializeTask;

    public MidiBarViewModel(IMidiApi? api)
    {
        if (api is not null)
            _service = new MidiConnectionService(api, SessionName);

        UpdateGroups(null);

        StatusText = api is null
            ? "MIDI is not available on this platform."
            : "MIDI not started.";
    }

    /// <summary>
    /// Available endpoints, sorted by name.
    /// </summary>
    public ObservableCollection<MidiEndpointInfo> Endpoints { get; } = [];

    /// <summary>
    /// The UMP groups the selected endpoint declares for sending, with their names.
    /// All 16 groups (without names) when no endpoint is selected or the endpoint declares no blocks for sending.
    /// </summary>
    public ObservableCollection<MidiGroupItem> SendGroups { get; } = [];

    /// <summary>
    /// The UMP groups the selected endpoint declares for receiving, with their names.
    /// All 16 groups (without names) when no endpoint is selected or the endpoint declares no blocks for receiving.
    /// </summary>
    public ObservableCollection<MidiGroupItem> ReceiveGroups { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleConnectionCommand))]
    public partial MidiEndpointInfo? SelectedEndpoint { get; set; }

    partial void OnSelectedEndpointChanged(MidiEndpointInfo? value) => UpdateGroups(value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Groups))]
    public partial MidiGroupItem? SelectedSendGroup { get; set; }

    // the receive group follows the send group as long as they are the same
    partial void OnSelectedSendGroupChanged(MidiGroupItem? oldValue, MidiGroupItem? newValue)
    {
        if (newValue is not null && oldValue?.Group == SelectedReceiveGroup?.Group
            && ReceiveGroups.FirstOrDefault(item => item.Group == newValue.Group) is { } receive)
            SelectedReceiveGroup = receive;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Groups))]
    public partial MidiGroupItem? SelectedReceiveGroup { get; set; }

    /// <summary>
    /// The selected UMP groups to send on and to receive on (values 0-15).
    /// </summary>
    public MidiGroupPair Groups => new(SelectedSendGroup?.Group ?? default, SelectedReceiveGroup?.Group ?? default);

    // keeps the selected groups when the new endpoint declares them, otherwise selects its first groups
    private void UpdateGroups(MidiEndpointInfo? endpoint)
    {
        var current = Groups;
        Fill(SendGroups, endpoint, MidiDirections.Send);
        Fill(ReceiveGroups, endpoint, MidiDirections.Receive);
        SelectedSendGroup = SendGroups.FirstOrDefault(item => item.Group == current.Send) ?? SendGroups[0];
        SelectedReceiveGroup = ReceiveGroups.FirstOrDefault(item => item.Group == current.Receive) ?? ReceiveGroups[0];
    }

    private static void Fill(ObservableCollection<MidiGroupItem> items, MidiEndpointInfo? endpoint, MidiDirections direction)
    {
        var groups = endpoint?.Capabilities.GetDeclaredGroups(direction) ?? [];
        if (groups.IsEmpty)
            groups = [.. Enumerable.Range(0, 16).Select(value => new MidiGroup((byte)value))];

        items.Clear();
        foreach (var group in groups)
            items.Add(new MidiGroupItem(group, endpoint?.Capabilities.GetGroupName(group, direction)));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectEndpoint))]
    [NotifyCanExecuteChangedFor(nameof(ToggleConnectionCommand))]
    public partial bool IsAvailable { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectEndpoint), nameof(ConnectButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ToggleConnectionCommand))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectEndpoint))]
    [NotifyCanExecuteChangedFor(nameof(ToggleConnectionCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    /// <summary>
    /// The endpoint can only be changed while disconnected.
    /// </summary>
    public bool CanSelectEndpoint => IsAvailable && !IsConnected && !IsBusy;

    public string ConnectButtonText => IsConnected ? "Stop" : "Start";

    /// <summary>
    /// The active connection (for sending), or null when not connected.
    /// </summary>
    public IMidiConnection? Connection => _service?.Connection;

    /// <summary>
    /// Raised for received packets on a background thread. The span is only valid during the call.
    /// </summary>
    public event MidiPacketReceivedHandler? PacketsReceived;

    /// <summary>
    /// Starts endpoint discovery. Safe to call more than once. Call on the UI thread.
    /// </summary>
    public Task Initialize() => _initializeTask ??= InitializeCore();

    private async Task InitializeCore()
    {
        if (_service is null)
            return;

        IsBusy = true;
        try
        {
            IsAvailable = await _service.StartWatching(OnEndpointChanged, CancellationToken.None);
            if (IsAvailable)
            {
                StatusText = "Select a MIDI endpoint and press Start.";
                _ = MonitorWatching();
            }
            else
            {
                StatusText = "The MIDI service is not available.";
            }
        }
        catch (Exception error)
        {
            IsAvailable = false;
            StatusText = $"MIDI is unavailable: {error.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MonitorWatching()
    {
        try
        {
            await _service!.WaitForWatchCompletion(CancellationToken.None);
        }
        catch (Exception error)
        {
            StatusText = $"MIDI endpoint discovery stopped: {error.Message}";
        }
    }

    private bool CanToggleConnection()
        => IsAvailable && !IsBusy && (IsConnected || SelectedEndpoint is not null);

    [RelayCommand(CanExecute = nameof(CanToggleConnection))]
    private async Task ToggleConnection()
    {
        if (_service is null)
            return;

        IsBusy = true;
        try
        {
            if (IsConnected)
            {
                await Disconnect("Disconnected.");
            }
            else if (SelectedEndpoint is { } endpoint)
            {
                await Connect(endpoint);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task Connect(MidiEndpointInfo endpoint)
    {
        try
        {
            var connection = await _service!.Connect(endpoint, OnPacketsReceived, CancellationToken.None);
            connection.ConnectionStateChanged += OnConnectionStateChanged;
            IsConnected = true;
            StatusText = $"Connected to {endpoint.Name} ({connection.Directions}).";

            // the connection may have faulted before we subscribed
            if (connection.ConnectionState == MidiConnectionState.Faulted)
                await Disconnect($"Connection to {endpoint.Name} lost: {connection.Error?.Message}");
        }
        catch (Exception error)
        {
            StatusText = $"Could not connect to {endpoint.Name}: {error.Message}";
        }
    }

    private async Task Disconnect(string status)
    {
        if (_service?.Connection is { } connection)
            connection.ConnectionStateChanged -= OnConnectionStateChanged;

        try
        {
            await _service!.Disconnect();
            StatusText = status;
        }
        catch (Exception error)
        {
            StatusText = $"Error while disconnecting: {error.Message}";
        }
        finally
        {
            IsConnected = false;
        }
    }

    private void OnConnectionStateChanged(object? sender, MidiConnectionStateChangedArgs args)
    {
        if (args.NewState != MidiConnectionState.Faulted)
            return;

        Dispatcher.UIThread.Post(async () =>
        {
            if (!ReferenceEquals(sender, _service?.Connection))
                return;

            var name = (sender as IMidiConnection)?.EndpointInfo.Name;
            IsBusy = true;
            try
            {
                await Disconnect($"Connection to {name} lost: {args.Error?.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        });
    }

    private void OnPacketsReceived(ReadOnlySpan<StampedPacket> packets)
        => PacketsReceived?.Invoke(packets);

    private void OnEndpointChanged(MidiEndpointChangedArgs args)
        => Dispatcher.UIThread.Post(() => ApplyEndpointChange(args));

    private void ApplyEndpointChange(MidiEndpointChangedArgs args)
    {
        var endpoint = args.Endpoint;
        var index = IndexOf(endpoint.Id);
        var wasSelected = SelectedEndpoint is not null && IsSameEndpoint(SelectedEndpoint, endpoint);

        if (args.ChangeType == MidiEndpointChangeType.Removed)
        {
            if (index >= 0)
                Endpoints.RemoveAt(index);
            if (wasSelected)
                SelectedEndpoint = null;
            return;
        }

        // Added or Updated
        if (index >= 0)
            Endpoints.RemoveAt(index);
        Endpoints.Insert(InsertIndexOf(endpoint), endpoint);

        if (wasSelected || (SelectedEndpoint is null && !IsConnected))
            SelectedEndpoint = endpoint;
    }

    private int IndexOf(string id)
    {
        for (var i = 0; i < Endpoints.Count; i++)
        {
            if (String.Equals(Endpoints[i].Id, id, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private int InsertIndexOf(MidiEndpointInfo endpoint)
    {
        var index = 0;
        while (index < Endpoints.Count
            && StringComparer.CurrentCultureIgnoreCase.Compare(Endpoints[index].Name, endpoint.Name) <= 0)
        {
            index++;
        }
        return index;
    }

    private static bool IsSameEndpoint(MidiEndpointInfo left, MidiEndpointInfo right)
        => String.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        if (_service is null)
            return;

        if (_service.Connection is { } connection)
            connection.ConnectionStateChanged -= OnConnectionStateChanged;

        await _service.DisposeAsync();
        _initializeTask = null;
        IsConnected = false;
        IsAvailable = false;
        Endpoints.Clear();
        StatusText = "MIDI stopped.";
    }
}
