namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// Reports all problems found while creating an <see cref="AddressMap"/> from its schemas: the address layout of the
/// map and the tags of the envelope messages that carry it.
/// </summary>
public sealed class AddressMapException(string root, IReadOnlyList<AddressMapProblem> problems)
    : Exception($"The address map '{root}' is not valid:{Environment.NewLine}{String.Join(Environment.NewLine, problems)}")
{
    public string Root { get; } = root;

    /// <summary>The problems found, each located at its schema node.</summary>
    public IReadOnlyList<AddressMapProblem> Problems { get; } = problems;

    /// <summary>The problems as text: the node path and what is wrong.</summary>
    public IReadOnlyList<string> Errors => [.. Problems.Select(problem => problem.ToString())];
}
