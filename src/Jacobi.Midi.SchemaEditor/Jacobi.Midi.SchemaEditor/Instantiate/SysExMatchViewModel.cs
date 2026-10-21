using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.Core;
using Jacobi.Midi.Messages.V2;
using Jacobi.Midi.SchemaEditor.Midi;

namespace Jacobi.Midi.SchemaEditor.Instantiate;

/// <summary>
/// A buffer (SysEx message) to match: loaded from a file or received from MIDI.
/// </summary>
/// <param name="Source">Where the bytes came from, e.g. the file name with the message number, or 'Received 12:34:56'.</param>
/// <param name="Bytes">The bytes to read with the schema.</param>
internal sealed record SysExBuffer(string Source, byte[] Bytes)
{
    /// <summary>The bytes as hex, shortened when there are many.</summary>
    public string BytesText => Bytes.Length <= MaxShown
        ? Convert.ToHexString(Bytes)
        : $"{Convert.ToHexString(Bytes.AsSpan(0, MaxShown))}… ({Bytes.Length} bytes)";

    private const int MaxShown = 64;

    public override string ToString() => $"{Source} ({Bytes.Length} bytes)";

    /// <summary>
    /// Splits the contents of a SysEx file into its messages (each F0 … F7). Data that does not start with F0
    /// (e.g. the bytes of a nested group) is one buffer. An unterminated last message is kept, so reading it shows the problem.
    /// </summary>
    public static IReadOnlyList<SysExBuffer> Split(string name, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0 || data[0] != 0xF0)
        {
            return [new SysExBuffer(name, data)];
        }

        var messages = new List<byte[]>();
        var start = 0;
        while (start < data.Length)
        {
            var end = Array.IndexOf(data, (byte)0xF7, start);
            var next = end < 0 ? data.Length : end + 1;
            messages.Add(data[start..next]);
            // skip anything between the messages
            start = Array.IndexOf(data, (byte)0xF0, next);
            if (start < 0)
            {
                break;
            }
        }
        return messages.Count == 1
            ? [new SysExBuffer(name, messages[0])]
            : messages.Select((bytes, index) => new SysExBuffer($"{name} #{index + 1}", bytes)).ToList();
    }
}

/// <summary>
/// The match part of the instantiate dialog: loads a SysEx file or receives a SysEx message from the active MIDI connection
/// and reads (deserializes) the selected buffer into the instance tree of the dialog, so the values of each field are shown.
/// A read problem (the bytes do not match the schema) is caught and shown in <see cref="Error"/>; the values read up to it remain.
/// </summary>
internal sealed partial class SysExMatchViewModel : ObservableObject
{
    private readonly Func<byte[], long>? _deserialize;
    private readonly Func<Task<(string Name, byte[] Bytes)?>> _openFile;
    private readonly MidiBarViewModel _midi;
    // only used on the thread that raises the received packets
    private SysEx7Assembler? _assembler;

    /// <param name="deserialize">
    /// Reads the bytes into the instance tree and returns the number of bytes read; null when the instance has no plan (a node def).
    /// </param>
    /// <param name="openFile">Lets the user pick a SysEx file and returns its name and contents; null when the user cancelled.</param>
    /// <param name="midi">The MIDI connection the messages are received from.</param>
    public SysExMatchViewModel(Func<byte[], long>? deserialize, Func<Task<(string Name, byte[] Bytes)?>> openFile, MidiBarViewModel midi)
    {
        _deserialize = deserialize;
        _openFile = openFile;
        _midi = midi;
        _midi.PropertyChanged += OnMidiPropertyChanged;
        Status = deserialize is null
            ? "A node def cannot be matched: match a group of a root."
            : "Load a SysEx file or receive a message to match it with the schema.";
    }

    /// <summary>True when buffers can be read (the instance has a plan).</summary>
    public bool CanMatch => _deserialize is not null;

    /// <summary>The loaded and received buffers; selecting one matches it.</summary>
    public ObservableCollection<SysExBuffer> Buffers { get; } = [];

    public bool HasBuffers => Buffers.Count > 0;

    /// <summary>The buffer that is matched.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MatchCommand))]
    public partial SysExBuffer? SelectedBuffer { get; set; }

    /// <summary>The outcome of the last match (or load/receive).</summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = String.Empty;

    /// <summary>A warning of the last match, e.g. trailing bytes that were not read; empty when there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    public partial string Warning { get; private set; } = String.Empty;

    public bool HasWarning => Warning.Length > 0;

    /// <summary>Why the last match failed (the read error); empty when it succeeded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; private set; } = String.Empty;

    public bool HasError => Error.Length > 0;

    /// <summary>True while waiting for a SysEx message from MIDI.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReceiveButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ReceiveCommand))]
    public partial bool IsReceiving { get; private set; }

    public string ReceiveButtonText => IsReceiving ? "Cancel" : "Receive";

    partial void OnSelectedBufferChanged(SysExBuffer? value)
    {
        if (value is not null)
        {
            Match();
        }
    }

    private bool CanMatchBuffer() => CanMatch && SelectedBuffer is not null;

    /// <summary>
    /// Reads the selected buffer into the instance tree again, e.g. after selecting the body of an open choice.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMatchBuffer))]
    private void Match()
    {
        if (_deserialize is null || SelectedBuffer is not { } buffer)
        {
            return;
        }

        Warning = String.Empty;
        Error = String.Empty;
        try
        {
            var read = _deserialize(buffer.Bytes);
            Status = $"Matched {buffer.Source}: read {read} of {buffer.Bytes.Length} byte(s).";
            if (read < buffer.Bytes.Length)
            {
                var trailing = buffer.Bytes.AsSpan((int)read);
                Warning = $"{trailing.Length} trailing byte(s) were not read: {Convert.ToHexString(trailing.Length <= 32 ? trailing : trailing[..32])}{(trailing.Length > 32 ? "…" : String.Empty)}";
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // the engine and the processors can fail with their own exception types
            Status = $"{buffer.Source} does not match the schema; the values read up to the problem are shown.";
            Error = exception.Message;
        }
    }

    /// <summary>Loads a SysEx file; each message in it becomes a buffer and the first one is matched.</summary>
    [RelayCommand]
    private async Task LoadFile()
    {
        try
        {
            if (await _openFile() is not { } file)
            {
                return;
            }
            var buffers = SysExBuffer.Split(file.Name, file.Bytes);
            Add(buffers);
            if (buffers.Count > 1)
            {
                Status = $"{file.Name} holds {buffers.Count} messages; select the one to match.";
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            Status = $"Could not load the file: {exception.Message}";
        }
    }

    private bool CanReceive() => IsReceiving || _midi.IsConnected;

    /// <summary>
    /// Waits for the next complete SysEx7 message on the group selected in the MIDI bar and matches it; pressed again it stops waiting.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReceive))]
    private void Receive()
    {
        if (IsReceiving)
        {
            StopReceiving();
            Status = "Stopped waiting for a SysEx message.";
            return;
        }

        _assembler = new SysEx7Assembler(_midi.Group.Value);
        _midi.PacketsReceived += OnPacketsReceived;
        IsReceiving = true;
        Status = $"Waiting for a SysEx message on group {_midi.Group.Value + 1}…";
    }

    /// <summary>Stops receiving; called when the dialog closes.</summary>
    public void Detach()
    {
        StopReceiving();
        _midi.PropertyChanged -= OnMidiPropertyChanged;
    }

    /// <summary>Adds a received SysEx message as a buffer and matches it. Call on the UI thread.</summary>
    internal void OnReceived(byte[] sysEx)
    {
        if (!IsReceiving)
        {
            return;
        }
        StopReceiving();
        Add([new SysExBuffer($"Received {DateTime.Now:HH:mm:ss}", sysEx)]);
    }

    private void Add(IReadOnlyList<SysExBuffer> buffers)
    {
        foreach (var buffer in buffers)
        {
            Buffers.Add(buffer);
        }
        OnPropertyChanged(nameof(HasBuffers));
        if (buffers.Count > 0)
        {
            SelectedBuffer = buffers[0];
        }
    }

    private void StopReceiving()
    {
        _midi.PacketsReceived -= OnPacketsReceived;
        IsReceiving = false;
    }

    // raised on a background thread; the span is only valid during the call
    private void OnPacketsReceived(ReadOnlySpan<StampedPacket> packets)
    {
        if (_assembler is not { } assembler)
        {
            return;
        }
        foreach (var packet in packets)
        {
            if (packet.Packet.HasValue && assembler.TryAdd(packet.Packet, out var sysEx))
            {
                Dispatcher.UIThread.Post(() => OnReceived(sysEx));
                return;
            }
        }
    }

    private void OnMidiPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MidiBarViewModel.IsConnected))
        {
            if (!_midi.IsConnected && IsReceiving)
            {
                StopReceiving();
                Status = "The MIDI connection was closed while waiting for a SysEx message.";
            }
            ReceiveCommand.NotifyCanExecuteChanged();
        }
    }
}
