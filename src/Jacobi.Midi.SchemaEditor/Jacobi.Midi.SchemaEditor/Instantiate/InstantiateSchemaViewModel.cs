using System.ComponentModel;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Core;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Messages.V2;
using Jacobi.Midi.SchemaEditor.Midi;
using Jacobi.Midi.SchemaEditor.ViewModels;

namespace Jacobi.Midi.SchemaEditor.Instantiate;

/// <summary>
/// The view model of the instantiate dialog: lets the user provide the values of a group (the <see cref="Root"/> instance tree),
/// serialize them to a SysEx file (Save As) or send them to the active MIDI connection (Send),
/// or shows the <see cref="Error"/> that prevented the instantiation.
/// With an address map body, the included groups are serialized as one or more data messages (see <see cref="Transfer"/>).
/// In match mode (<see cref="Match"/>) the values are read from a SysEx file or a received message first.
/// The values of a group can be copied to the clipboard and pasted into another instance of the same group (see <see cref="InstanceValues"/>).
/// </summary>
internal sealed partial class InstantiateSchemaViewModel : ViewModelBase
{
    private readonly Action<InstantiateSchemaViewModel> _close;
    private readonly Func<ICollection<string>, IReadOnlyList<SerializedMessage>>? _serialize;
    private readonly Func<string?>? _describeTransfer;
    private readonly Func<string, byte[], Task<string?>>? _saveFile;
    private readonly MidiBarViewModel? _midi;
    private readonly IClipboard? _clipboard;

    /// <param name="root">The instance tree to fill out.</param>
    /// <param name="serialize">
    /// Writes the values of <paramref name="root"/> with the serializer as one or more messages, adding the situations the user
    /// may not expect to the warnings; null when the instance has no plan (a node def).
    /// </param>
    /// <param name="saveFile">
    /// Lets the user pick a file (the suggested name is passed) and saves the bytes to it;
    /// returns the name of the saved file or null when the user cancelled.
    /// </param>
    /// <param name="midi">The MIDI connection the values are sent to.</param>
    /// <param name="close">Called with this view model when the dialog closes.</param>
    /// <param name="match">The match part (match mode): reads the values from a SysEx buffer; null to instantiate.</param>
    /// <param name="warnings">The situations of the compilation the user may not expect (see <see cref="SchemaInstantiator.Warnings"/>).</param>
    /// <param name="describeTransfer">
    /// Describes the messages the included groups of an address map body are sent in (see <see cref="SchemaInstantiator.DescribeTransfer"/>);
    /// null, or returning null, when there is no address map.
    /// </param>
    /// <param name="clipboard">The clipboard the values of a group are copied to and pasted from; null when there is none.</param>
    public InstantiateSchemaViewModel(InstanceNode root, Func<ICollection<string>, IReadOnlyList<SerializedMessage>>? serialize,
        Func<string, byte[], Task<string?>> saveFile, MidiBarViewModel midi, Action<InstantiateSchemaViewModel> close,
        SysExMatchViewModel? match = null, IReadOnlyList<string>? warnings = null, Func<string?>? describeTransfer = null,
        IClipboard? clipboard = null)
    {
        Root = root;
        Match = match;
        Warnings = String.Join(Environment.NewLine, warnings ?? []);
        Title = match is null ? $"Instantiate {root.Name}" : $"Match {root.Name}";
        Heading = match is null ? "INSTANTIATE" : "MATCH";
        _serialize = serialize;
        _saveFile = saveFile;
        _midi = midi;
        _close = close;
        _describeTransfer = describeTransfer;
        _clipboard = clipboard;
        _midi.PropertyChanged += OnMidiPropertyChanged;
        root.InclusionChanged += OnInclusionChanged;
        RefreshTransfer();
        if (serialize is null)
        {
            StatusMessage = "A node def cannot be serialized: instantiate a group of a root to save or send it.";
        }
    }

    /// <summary>Creates a dialog that only shows why <paramref name="name"/> could not be instantiated (or matched).</summary>
    public InstantiateSchemaViewModel(string name, string error, Action<InstantiateSchemaViewModel> close, bool isMatch = false)
    {
        Title = isMatch ? $"Cannot match {name}" : $"Cannot instantiate {name}";
        Heading = isMatch ? "MATCH" : "INSTANTIATE";
        Error = error;
        _close = close;
    }

    /// <summary>The node to fill out; null when the dialog shows an <see cref="Error"/>.</summary>
    public InstanceNode? Root { get; }

    /// <summary>The match part in match mode: loads or receives a SysEx buffer and reads it into the <see cref="Root"/>; null to instantiate.</summary>
    public SysExMatchViewModel? Match { get; }

    public bool IsMatch => Match is not null;

    public string Title { get; }

    /// <summary>The caption above the title: INSTANTIATE or MATCH.</summary>
    public string Heading { get; }

    /// <summary>The instruction below the title.</summary>
    public string Instruction => IsMatch
        ? "Load a SysEx file or receive a message from MIDI: it is read with the schema into the values below. Select the body of an open choice before matching."
        : "Provide a value for each field. Select the body of an open choice and add copies of an open repeat.";

    /// <summary>The reason the instantiation failed; empty when the dialog shows the <see cref="Root"/>.</summary>
    public string Error { get; } = String.Empty;

    public bool HasError => Error.Length > 0;

    /// <summary>
    /// The situations of the compilation the user may not expect (one per line), e.g. a schema that was left out;
    /// empty when there are none.
    /// </summary>
    public string Warnings { get; } = String.Empty;

    public bool HasWarnings => Warnings.Length > 0;

    public bool HasRoot => Root is not null;

    /// <summary>True when the values can be serialized (saved or sent).</summary>
    public bool CanSerialize => _serialize is not null;

    /// <summary>
    /// How the included groups of an address map body are sent: the data messages with their address and size, or why they
    /// cannot be sent; empty when there is no address map. Follows the inclusion.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTransfer))]
    public partial string Transfer { get; private set; } = String.Empty;

    public bool HasTransfer => Transfer.Length > 0;

    private void OnInclusionChanged(object? sender, EventArgs e) => RefreshTransfer();

    private void RefreshTransfer() => Transfer = _describeTransfer?.Invoke() ?? String.Empty;

    /// <summary>The outcome of the last Save As or Send, or why it failed.</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = String.Empty;

    /// <summary>
    /// The problem that prevented the last Save As or Send (e.g. the execution plan could not be built), shown in a popup;
    /// empty when there is none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string Problem { get; private set; } = String.Empty;

    public bool HasProblem => Problem.Length > 0;

    /// <summary>True while saving or sending.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand), nameof(SendCommand))]
    public partial bool IsBusy { get; private set; }

    [RelayCommand]
    private void Close()
    {
        if (_midi is not null)
        {
            _midi.PropertyChanged -= OnMidiPropertyChanged;
        }
        if (Root is not null)
        {
            Root.InclusionChanged -= OnInclusionChanged;
        }
        Match?.Detach();
        _close(this);
    }

    /// <summary>Closes the popup with the <see cref="Problem"/>.</summary>
    [RelayCommand]
    private void DismissProblem() => Problem = String.Empty;

    /// <summary>
    /// Serializes the values; a problem (the plan cannot be built or a value cannot be written) is shown in the popup.
    /// </summary>
    /// <param name="warnings">Receives the situations the user may not expect.</param>
    /// <returns>The messages; null when serializing failed.</returns>
    private IReadOnlyList<SerializedMessage>? Serialize(string action, List<string> warnings)
    {
        try
        {
            return _serialize!(warnings);
        }
        catch (Exception exception) when (exception is ExecutionPlanException or AddressMapException
            or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            StatusMessage = $"Could not {action}: the values could not be serialized.";
            Problem = exception.Message;
            return null;
        }
    }

    private bool CanSaveAs() => CanSerialize && !IsBusy;

    /// <summary>
    /// Serializes the values and saves them to a SysEx (.syx) file the user picks.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveAs))]
    private async Task SaveAs()
    {
        IsBusy = true;
        try
        {
            var warnings = new List<string>();
            if (Serialize("save", warnings) is not { } messages)
            {
                return;
            }
            // the messages one after the other, as in a bulk dump
            var bytes = messages.SelectMany(message => message.Bytes).ToArray();
            var fileName = await _saveFile!($"{Root!.Name}.syx", bytes);
            if (fileName is not null)
            {
                StatusMessage = String.Join(Environment.NewLine,
                    [$"Saved {Count(messages)} ({bytes.Length} byte(s)) to {fileName}.", .. warnings]);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException
            or IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not save: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSend() => CanSerialize && !IsBusy && _midi is { IsConnected: true, Connection: not null };

    /// <summary>
    /// Serializes the values and sends them as SysEx7 packets to the active MIDI connection, on the group selected in the MIDI bar.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        if (_midi?.Connection is not { } connection)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var warnings = new List<string>();
            if (Serialize("send", warnings) is not { } messages)
            {
                return;
            }
            var byteCount = 0;
            var packetCount = 0;
            for (var index = 0; index < messages.Count; index++)
            {
                var message = messages[index];
                var packets = SysEx7Message.FromSystemExclusive(_midi.Groups.Send.Value, message.Bytes)
                    .Select(packet => new UniversalPacket([packet[0], packet[1]]))
                    .ToArray();
                await connection.Send(packets, CancellationToken.None);
                byteCount += message.Bytes.Length;
                packetCount += packets.Length;
                // the device needs time to process a message (midi.messageInterval)
                if (index < messages.Count - 1 && message.Interval > TimeSpan.Zero)
                {
                    await Task.Delay(message.Interval);
                }
            }
            StatusMessage = String.Join(Environment.NewLine,
                [$"Sent {Count(messages)} ({byteCount} byte(s) in {packetCount} packet(s)) to {connection.EndpointInfo.Name}.", .. warnings]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // the MIDI backend can fail with its own exception types
            StatusMessage = $"Could not send: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The most problems of a paste that are listed in the <see cref="StatusMessage"/>; the others are counted.</summary>
    public const int PasteProblemLimit = 10;

    private bool CanUseClipboard() => _clipboard is not null;

    /// <summary>Copies the values of <paramref name="group"/> to the clipboard (see <see cref="InstanceValues"/>).</summary>
    [RelayCommand(CanExecute = nameof(CanUseClipboard))]
    private async Task CopyValues(InstanceGroup? group)
    {
        if (group is null || _clipboard is null)
        {
            return;
        }
        try
        {
            await _clipboard.SetTextAsync(InstanceValues.Copy(group).ToText());
            StatusMessage = $"Copied the values of {group.DisplayPath}.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // the clipboard of the platform can fail with its own exception types
            StatusMessage = $"Could not copy the values of {group.DisplayPath}: {exception.Message}";
        }
    }

    /// <summary>
    /// Pastes the values on the clipboard into <paramref name="group"/> when they were copied from the same group
    /// (see <see cref="InstanceValues.Fits"/>); what could not be pasted is listed in the <see cref="StatusMessage"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUseClipboard))]
    private async Task PasteValues(InstanceGroup? group)
    {
        if (group is null || _clipboard is null)
        {
            return;
        }
        string? text;
        try
        {
            text = await _clipboard.TryGetTextAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"Could not read the clipboard: {exception.Message}";
            return;
        }
        StatusMessage = Paste(group, text);
    }

    /// <summary>Pastes the copied values in <paramref name="text"/> into <paramref name="group"/>; returns the outcome.</summary>
    internal static string Paste(InstanceGroup group, string? text)
    {
        if (!InstanceValues.TryParse(text, out var values))
        {
            return "Could not paste: the clipboard holds no copied values.";
        }
        if (!values!.Fits(group))
        {
            return $"Could not paste: the values of {values.Source} ({values.Shape}) do not fit {group.DisplayPath} ({InstanceValues.ShapeOf(group)}).";
        }
        var problems = new List<string>();
        var count = values.PasteInto(group, problems);
        var more = problems.Count > PasteProblemLimit ? [$"... and {problems.Count - PasteProblemLimit} more."] : Array.Empty<string>();
        return String.Join(Environment.NewLine,
            [$"Pasted {count} value(s) of {values.Source} into {group.DisplayPath}.", .. problems.Take(PasteProblemLimit), .. more]);
    }

    private static string Count(IReadOnlyList<SerializedMessage> messages)
        => messages.Count == 1 ? "1 message" : $"{messages.Count} messages";

    private void OnMidiPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MidiBarViewModel.IsConnected))
        {
            SendCommand.NotifyCanExecuteChanged();
        }
    }
}
