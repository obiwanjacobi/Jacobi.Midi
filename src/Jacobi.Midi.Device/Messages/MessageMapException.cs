namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// Reports all problems found while creating the message map of a root from its schemas (e.g. the address layout of
/// an address map and the tags of the envelope messages that carry it), each located at its schema node.
/// </summary>
public sealed class MessageMapException(string root, IReadOnlyList<MessageMapProblem> problems)
    : Exception($"The message map '{root}' is not valid:{Environment.NewLine}{String.Join(Environment.NewLine, problems)}")
{
    public string Root { get; } = root;

    /// <summary>The problems found, each located at its schema node.</summary>
    public IReadOnlyList<MessageMapProblem> Problems { get; } = problems;

    /// <summary>The problems as text: the node path and what is wrong.</summary>
    public IReadOnlyList<string> Errors => [.. Problems.Select(problem => problem.ToString())];
}
