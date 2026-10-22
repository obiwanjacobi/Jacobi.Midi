namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// Reports all problems found while creating an <see cref="AddressMap"/> from its schemas: the address layout of the
/// map and the tags of the envelope messages that carry it.
/// </summary>
public sealed class AddressMapException(string root, IReadOnlyList<string> errors)
    : Exception($"The address map '{root}' is not valid:{Environment.NewLine}{String.Join(Environment.NewLine, errors)}")
{
    public string Root { get; } = root;
    public IReadOnlyList<string> Errors { get; } = errors;
}
