using CommunityToolkit.Mvvm.ComponentModel;
using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.ViewModels;

// The check of the message map tags (midi.*) of the selected document: run after a pause in editing, shown in the
// schema panel (all issues) and in the node editor (the issues of the selected node).
internal sealed partial class MainViewModel
{
    private static readonly TimeSpan MessageMapCheckDelay = TimeSpan.FromMilliseconds(400);
    private CancellationTokenSource? _messageMapCheck;

    /// <summary>The message map issues of the selected document, compiled with the loaded schemas (see <see cref="MessageMapCheck"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessageMapIssues), nameof(MessageMapSummary), nameof(SelectedNodeIssues), nameof(HasSelectedNodeIssues))]
    public partial IReadOnlyList<MessageMapIssue> MessageMapIssues { get; private set; } = [];

    /// <summary>Why the message map tags of the selected document are not checked (it does not compile); null when they are.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessageMapNote))]
    public partial string? MessageMapNote { get; private set; }

    public bool HasMessageMapIssues => MessageMapIssues.Count > 0;

    public bool HasMessageMapNote => MessageMapNote is not null;

    /// <summary>The number of issues by severity, e.g. "Message map: 1 error, 2 info".</summary>
    public string MessageMapSummary
        => "Message map: " + String.Join(", ", MessageMapIssues.GroupBy(issue => issue.SeverityText).Select(group => $"{group.Count()} {group.Key}"));

    /// <summary>The message map issues of the selected node.</summary>
    public IReadOnlyList<MessageMapIssue> SelectedNodeIssues
        => SelectedItem is EditNode node ? [.. MessageMapIssues.Where(issue => ReferenceEquals(issue.Node, node))] : [];

    public bool HasSelectedNodeIssues => SelectedNodeIssues.Count > 0;

    /// <summary>The issue chosen in the list; selects its node when that is in an open document.</summary>
    [ObservableProperty]
    public partial MessageMapIssue? SelectedMessageMapIssue { get; set; }

    partial void OnSelectedMessageMapIssueChanged(MessageMapIssue? value)
    {
        if (value is null)
        {
            return;
        }
        if (value.Node is { } node && FindDocument(node) is not null)
        {
            SelectedItem = node;
        }
        else
        {
            StatusMessage = $"'{value.Location}' is not in an open document.";
        }
    }

    /// <summary>Checks the message map tags after a pause: the edits of a typed value come in one by one.</summary>
    private async void ScheduleMessageMapCheck()
    {
        _messageMapCheck?.Cancel();
        var cancel = _messageMapCheck = new CancellationTokenSource();
        try
        {
            await Task.Delay(MessageMapCheckDelay, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        CheckMessageMaps();
    }

    private void CheckMessageMaps()
    {
        if (SelectedDocument is not { } document)
        {
            MessageMapIssues = [];
            MessageMapNote = null;
            return;
        }

        try
        {
            MessageMapIssues = MessageMapCheck.Run(LoadedSchemas.Compile(document.Schema), OpenedAssemblies.Processors);
            MessageMapNote = null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException
            or ExecutionPlanException or NotSupportedException)
        {
            MessageMapIssues = [];
            // only worth saying for a document that uses the tags
            MessageMapNote = MessageMapCheck.HasTags(document.Schema)
                ? $"The message map tags (midi.*) are not checked: {exception.Message}"
                : null;
        }
    }
}
