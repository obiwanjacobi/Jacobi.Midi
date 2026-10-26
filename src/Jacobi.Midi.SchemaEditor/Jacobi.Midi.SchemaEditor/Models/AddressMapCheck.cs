using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Processor;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.SchemaEditor.Models;

/// <summary>
/// One problem with the address map tags (<c>midi.*</c>) of the compiled schemas, with the authored node it is about.
/// </summary>
/// <param name="Problem">The problem as the Device addressing code reports it.</param>
/// <param name="Node">The authored node of the problem; null when it is not in an authored document (or has no node).</param>
internal sealed record AddressMapIssue(AddressMapProblem Problem, EditNode? Node)
{
    public bool IsError => Problem.Severity == AddressMapProblemSeverity.Error;

    public bool IsWarning => Problem.Severity == AddressMapProblemSeverity.Warning;

    public bool IsInformation => Problem.Severity == AddressMapProblemSeverity.Information;

    /// <summary>The severity as shown: error, warning or info.</summary>
    public string SeverityText => Problem.Severity switch
    {
        AddressMapProblemSeverity.Error => "error",
        AddressMapProblemSeverity.Warning => "warning",
        _ => "info",
    };

    /// <summary>Where the problem is: the document and the path of the node.</summary>
    public string Location => Problem.Path.IsEmpty ? Problem.Document : $"{Problem.Document}: {Problem.Path.Value}";

    public string Message => Problem.Message;

    /// <summary>True when the issue leads to a node that can be selected.</summary>
    public bool CanNavigate => Node is not null;
}

/// <summary>
/// Checks the use of the address map tags in a compiled schema set with the Device addressing code
/// (<see cref="AddressMapValidator"/>) and relates each problem to its authored node
/// (<see cref="CompiledNodeLocator.FindAuthored"/>). The editor only shows the results; the rules live in the Device.
/// </summary>
internal static class AddressMapCheck
{
    /// <summary>
    /// The issues in the compiled document and the documents it includes, errors first; empty when they have no tags or
    /// no problems. The other compiled documents take part (e.g. the device maps that conform to an envelope of the
    /// document), but their own issues are left out.
    /// </summary>
    /// <param name="processors">The processors the schemas use (the plans are built with them).</param>
    public static IReadOnlyList<AddressMapIssue> Run(SchemaSetCompilation compilation, IProcessorProvider processors)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(processors);
        var serializer = new SerializerBuilder()
            .AddSchemas(compilation.Schemas)
            .AddProcessors(processors)
            .Build();
        var shown = IncludeClosure(compilation.Document, compilation.Documents);
        return [.. AddressMapValidator.Validate(serializer)
            .Where(problem => shown.Contains(problem.Document))
            .Select(problem => new AddressMapIssue(problem,
                CompiledNodeLocator.FindAuthored(compilation.Documents, problem.Document, problem.Path)))
            .OrderBy(issue => issue.Problem.Severity)];
    }

    /// <summary>True when a node of the document (or of its node defs) has an address map tag.</summary>
    public static bool HasTags(EditDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Members.Concat(document.NodeDefs).Any(HasTags);

        static bool HasTags(EditNode node)
            => node.Properties.Any(property => AddressMapTags.IsTag(property.Name))
            || (node is EditGroup group && group.Members.Any(HasTags));
    }

    // the names of the document and the documents it includes (transitively)
    private static HashSet<string> IncludeClosure(EditDocument document, IReadOnlyList<EditDocument> documents)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<EditDocument>([document]);
        while (queue.TryDequeue(out var current))
        {
            if (!names.Add(current.Name))
            {
                continue;
            }
            foreach (var include in current.Includes)
            {
                if (documents.FirstOrDefault(other => String.Equals(other.Name, include.Schema, StringComparison.OrdinalIgnoreCase)) is { } included)
                {
                    queue.Enqueue(included);
                }
            }
        }
        return names;
    }
}
