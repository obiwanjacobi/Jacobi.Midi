using CommunityToolkit.Mvvm.ComponentModel;
using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.ViewModels;

// The check of the address map tags (midi.*) of the selected document: run after a pause in editing, shown in the
// schema panel (all issues) and in the node editor (the issues of the selected node).
internal sealed partial class MainViewModel
{
    private static readonly TimeSpan AddressMapCheckDelay = TimeSpan.FromMilliseconds(400);
    private CancellationTokenSource? _addressMapCheck;

    /// <summary>The address map issues of the selected document, compiled with the loaded schemas (see <see cref="AddressMapCheck"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddressMapIssues), nameof(AddressMapSummary), nameof(SelectedNodeIssues), nameof(HasSelectedNodeIssues))]
    public partial IReadOnlyList<AddressMapIssue> AddressMapIssues { get; private set; } = [];

    /// <summary>Why the address map tags of the selected document are not checked (it does not compile); null when they are.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddressMapNote))]
    public partial string? AddressMapNote { get; private set; }

    public bool HasAddressMapIssues => AddressMapIssues.Count > 0;

    public bool HasAddressMapNote => AddressMapNote is not null;

    /// <summary>The number of issues by severity, e.g. "Address map: 1 error, 2 info".</summary>
    public string AddressMapSummary
        => "Address map: " + String.Join(", ", AddressMapIssues.GroupBy(issue => issue.SeverityText).Select(group => $"{group.Count()} {group.Key}"));

    /// <summary>The address map issues of the selected node.</summary>
    public IReadOnlyList<AddressMapIssue> SelectedNodeIssues
        => SelectedItem is EditNode node ? [.. AddressMapIssues.Where(issue => ReferenceEquals(issue.Node, node))] : [];

    public bool HasSelectedNodeIssues => SelectedNodeIssues.Count > 0;

    /// <summary>The issue chosen in the list; selects its node when that is in an open document.</summary>
    [ObservableProperty]
    public partial AddressMapIssue? SelectedAddressMapIssue { get; set; }

    partial void OnSelectedAddressMapIssueChanged(AddressMapIssue? value)
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

    /// <summary>Checks the address map tags after a pause: the edits of a typed value come in one by one.</summary>
    private async void ScheduleAddressMapCheck()
    {
        _addressMapCheck?.Cancel();
        var cancel = _addressMapCheck = new CancellationTokenSource();
        try
        {
            await Task.Delay(AddressMapCheckDelay, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        CheckAddressMaps();
    }

    private void CheckAddressMaps()
    {
        if (SelectedDocument is not { } document)
        {
            AddressMapIssues = [];
            AddressMapNote = null;
            return;
        }

        try
        {
            AddressMapIssues = AddressMapCheck.Run(LoadedSchemas.Compile(document.Schema), OpenedAssemblies.Processors);
            AddressMapNote = null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException
            or ExecutionPlanException or NotSupportedException)
        {
            AddressMapIssues = [];
            // only worth saying for a document that uses the tags
            AddressMapNote = AddressMapCheck.HasTags(document.Schema)
                ? $"The address map tags are not checked: {exception.Message}"
                : null;
        }
    }
}
