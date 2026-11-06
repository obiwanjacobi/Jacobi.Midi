using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// How serious a <see cref="MessageMapProblem"/> is: an error makes the map unusable, a warning is a tag that has no
/// effect (probably a mistake), information tells how a tag is fulfilled (e.g. a device value the client supplies).
/// </summary>
public enum MessageMapProblemSeverity
{
    Error,
    Warning,
    Information,
}

/// <summary>
/// One problem found in the schemas of a message map (e.g. the <c>midi.*</c> tags of an address map or its envelopes),
/// located at a schema node: the name of the schema document and the path of the node from a root of that document
/// (node defs expanded, as in a plan).
/// </summary>
/// <param name="Document">The name of the schema document that holds the root of <paramref name="Path"/>.</param>
/// <param name="Path">The path of the node from a root of the document, e.g. 'Message16.RQ1.Address'; empty for the document.</param>
/// <param name="Message">What is wrong, without the path.</param>
/// <param name="Severity">How serious the problem is.</param>
public sealed record MessageMapProblem(string Document, SchemaPath Path, string Message,
    MessageMapProblemSeverity Severity = MessageMapProblemSeverity.Error)
{
    public override string ToString() => Path.IsEmpty ? Message : $"{Path.Value}: {Message}";
}

/// <summary>
/// Collects the <see cref="MessageMapProblem"/>s while a map and its envelopes are built and validated.
/// </summary>
internal sealed class MessageMapProblems : List<MessageMapProblem>
{
    public bool HasErrors => this.Any(problem => problem.Severity == MessageMapProblemSeverity.Error);

    public void Error(string document, SchemaPath path, string message)
        => Add(new MessageMapProblem(document, path, message));

    public void Warning(string document, SchemaPath path, string message)
        => Add(new MessageMapProblem(document, path, message, MessageMapProblemSeverity.Warning));

    public void Information(string document, SchemaPath path, string message)
        => Add(new MessageMapProblem(document, path, message, MessageMapProblemSeverity.Information));
}
