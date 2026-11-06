using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// How serious an <see cref="AddressMapProblem"/> is: an error makes the map unusable, a warning is a tag that has no
/// effect (probably a mistake), information tells how a tag is fulfilled (e.g. a device value the client supplies).
/// </summary>
public enum AddressMapProblemSeverity
{
    Error,
    Warning,
    Information,
}

/// <summary>
/// One problem found in the <c>midi.*</c> tags of an address map or its envelopes, located at a schema node: the name
/// of the schema document and the path of the node from a root of that document (node defs expanded, as in a plan).
/// </summary>
/// <param name="Document">The name of the schema document that holds the root of <paramref name="Path"/>.</param>
/// <param name="Path">The path of the node from a root of the document, e.g. 'Message16.RQ1.Address'; empty for the document.</param>
/// <param name="Message">What is wrong, without the path.</param>
/// <param name="Severity">How serious the problem is.</param>
public sealed record AddressMapProblem(string Document, SchemaPath Path, string Message,
    AddressMapProblemSeverity Severity = AddressMapProblemSeverity.Error)
{
    public override string ToString() => Path.IsEmpty ? Message : $"{Path.Value}: {Message}";
}

/// <summary>
/// Collects the <see cref="AddressMapProblem"/>s while a map and its envelopes are built and validated.
/// </summary>
internal sealed class AddressMapProblems : List<AddressMapProblem>
{
    public bool HasErrors => this.Any(problem => problem.Severity == AddressMapProblemSeverity.Error);

    public void Error(string document, SchemaPath path, string message)
        => Add(new AddressMapProblem(document, path, message));

    public void Warning(string document, SchemaPath path, string message)
        => Add(new AddressMapProblem(document, path, message, AddressMapProblemSeverity.Warning));

    public void Information(string document, SchemaPath path, string message)
        => Add(new AddressMapProblem(document, path, message, AddressMapProblemSeverity.Information));
}
