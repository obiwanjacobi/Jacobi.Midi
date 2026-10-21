using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Core;
using Jacobi.Midi.Messages.V2;
using Jacobi.Midi.SchemaEditor.Midi;
using Jacobi.Midi.SchemaEditor.ViewModels;

namespace Jacobi.Midi.SchemaEditor.Instantiate;

/// <summary>
/// The view model of the instantiate dialog: lets the user provide the values of a group (the <see cref="Root"/> instance tree),
/// serialize them to a SysEx file (Save As) or send them to the active MIDI connection (Send),
/// or shows the <see cref="Error"/> that prevented the instantiation.
/// </summary>
internal sealed partial class InstantiateSchemaViewModel : ViewModelBase
{
    private readonly Action<InstantiateSchemaViewModel> _close;
    private readonly Func<byte[]>? _serialize;
    private readonly Func<string, byte[], Task<string?>>? _saveFile;
    private readonly MidiBarViewModel? _midi;

    /// <param name="root">The instance tree to fill out.</param>
    /// <param name="serialize">Writes the values of <paramref name="root"/> with the serializer; null when the instance has no plan (a node def).</param>
    /// <param name="saveFile">
    /// Lets the user pick a file (the suggested name is passed) and saves the bytes to it;
    /// returns the name of the saved file or null when the user cancelled.
    /// </param>
    /// <param name="midi">The MIDI connection the values are sent to.</param>
    /// <param name="close">Called with this view model when the dialog closes.</param>
    public InstantiateSchemaViewModel(InstanceNode root, Func<byte[]>? serialize, Func<string, byte[], Task<string?>> saveFile,
        MidiBarViewModel midi, Action<InstantiateSchemaViewModel> close)
    {
        Root = root;
        Title = $"Instantiate {root.Name}";
        _serialize = serialize;
        _saveFile = saveFile;
        _midi = midi;
        _close = close;
        _midi.PropertyChanged += OnMidiPropertyChanged;
        if (serialize is null)
        {
            StatusMessage = "A node def cannot be serialized: instantiate a group of a root to save or send it.";
        }
    }

    /// <summary>Creates a dialog that only shows why <paramref name="name"/> could not be instantiated.</summary>
    public InstantiateSchemaViewModel(string name, string error, Action<InstantiateSchemaViewModel> close)
    {
        Title = $"Cannot instantiate {name}";
        Error = error;
        _close = close;
    }

    /// <summary>The node to fill out; null when the dialog shows an <see cref="Error"/>.</summary>
    public InstanceNode? Root { get; }

    public string Title { get; }

    /// <summary>The reason the instantiation failed; empty when the dialog shows the <see cref="Root"/>.</summary>
    public string Error { get; } = String.Empty;

    public bool HasError => Error.Length > 0;

    public bool HasRoot => Root is not null;

    /// <summary>True when the values can be serialized (saved or sent).</summary>
    public bool CanSerialize => _serialize is not null;

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
        _close(this);
    }

    /// <summary>Closes the popup with the <see cref="Problem"/>.</summary>
    [RelayCommand]
    private void DismissProblem() => Problem = String.Empty;

    /// <summary>
    /// Serializes the values; a problem (the plan cannot be built or a value cannot be written) is shown in the popup.
    /// </summary>
    /// <returns>The bytes; null when serializing failed.</returns>
    private byte[]? Serialize(string action)
    {
        try
        {
            return _serialize!();
        }
        catch (Exception exception) when (exception is ExecutionPlanException
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
            if (Serialize("save") is not { } bytes)
            {
                return;
            }
            var fileName = await _saveFile!($"{Root!.Name}.syx", bytes);
            if (fileName is not null)
            {
                StatusMessage = $"Saved {bytes.Length} byte(s) to {fileName}.";
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
            if (Serialize("send") is not { } bytes)
            {
                return;
            }
            var packets = SysEx7Message.FromSystemExclusive(_midi.Group.Value, bytes)
                .Select(packet => new UniversalPacket([packet[0], packet[1]]))
                .ToArray();
            await connection.Send(packets, CancellationToken.None);
            StatusMessage = $"Sent {bytes.Length} byte(s) in {packets.Length} packet(s) to {connection.EndpointInfo.Name}.";
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

    private void OnMidiPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MidiBarViewModel.IsConnected))
        {
            SendCommand.NotifyCanExecuteChanged();
        }
    }
}
