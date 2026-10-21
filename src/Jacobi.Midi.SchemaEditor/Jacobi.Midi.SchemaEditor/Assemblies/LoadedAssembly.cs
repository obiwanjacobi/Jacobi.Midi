using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.Assemblies;

/// <summary>
/// An assembly that was opened in the editor: what it provides (embedded schemas and processor factories)
/// and what could not be loaded. Read-only; shown as a node of the assembly tree.
/// The schemas are also in the <see cref="AssemblyWorkspace.SchemaSet"/>.
/// </summary>
internal sealed class LoadedAssembly
{
    public LoadedAssembly(string fullName, string name, string version, string location,
        IReadOnlyList<LoadedSchema> schemas, IReadOnlyList<(string Namespace, IReadOnlyList<ProcessorInfo> Processors)> processorNamespaces,
        IReadOnlyList<string> problems)
    {
        FullName = fullName;
        Name = name;
        Version = version;
        Location = location;
        Schemas = schemas;
        ProcessorNamespaces = processorNamespaces.Select(item => new ProcessorNamespaceItem(this, item.Namespace, item.Processors)).ToList();
        Problems = problems;
        Children = [.. Schemas, .. ProcessorNamespaces];
        foreach (var schema in schemas)
        {
            schema.Assembly = this;
        }
    }

    /// <summary>
    /// The full name of the assembly; identifies it.
    /// </summary>
    public string FullName { get; }

    /// <summary>
    /// The simple name of the assembly.
    /// </summary>
    public string Name { get; }

    public string Version { get; }

    /// <summary>
    /// The file name or path the assembly was opened from.
    /// </summary>
    public string Location { get; }

    /// <summary>
    /// The embedded schemas that were added to the schema set.
    /// </summary>
    public IReadOnlyList<LoadedSchema> Schemas { get; }

    /// <summary>
    /// The processor namespaces (one per processor factory) that the assembly added to the processor manager.
    /// </summary>
    public IReadOnlyList<ProcessorNamespaceItem> ProcessorNamespaces { get; }

    /// <summary>
    /// The resources or processors that could not be loaded, with the reason.
    /// </summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>
    /// The items below the assembly in the assembly tree: the schemas and the processor namespaces.
    /// </summary>
    public IReadOnlyList<object> Children { get; }

    public IReadOnlyList<string> SchemaNames => Schemas.Select(schema => schema.Name).ToList();
    public IReadOnlyList<string> ProcessorNamespaceNames => ProcessorNamespaces.Select(item => item.Namespace).ToList();
    public bool HasSchemas => Schemas.Count > 0;
    public bool HasProcessorNamespaces => ProcessorNamespaces.Count > 0;
    public bool HasProblems => Problems.Count > 0;

    /// <summary>
    /// True when the assembly provides nothing: no embedded schemas, no processors and no problems to show.
    /// Such an assembly is not added to the <see cref="AssemblyWorkspace.Assemblies"/>.
    /// </summary>
    public bool IsEmpty => !HasSchemas && !HasProcessorNamespaces && !HasProblems;

    /// <summary>
    /// A one-line description of what the assembly provides.
    /// </summary>
    public string Summary
        => $"{Schemas.Count} schema(s), {ProcessorNamespaces.Count} processor namespace(s)"
            + (HasProblems ? $", {Problems.Count} problem(s)" : String.Empty);
}

/// <summary>
/// An embedded schema of an opened assembly: a read-only copy of the authored schema that is shown in the assembly tree
/// and the edit view. It is not one of the open schema documents.
/// </summary>
/// <param name="name">The name of the schema.</param>
/// <param name="resource">The name of the embedded resource the schema was read from.</param>
/// <param name="document">The read-only document with the contents of the schema.</param>
internal sealed class LoadedSchema(string name, string resource, SchemaWorkspaceDocument document)
{
    public string Name { get; } = name;
    public string Resource { get; } = resource;
    public SchemaWorkspaceDocument Document { get; } = document;

    /// <summary>
    /// The assembly the schema is embedded in.
    /// </summary>
    public LoadedAssembly Assembly { get; internal set; } = null!;
}

/// <summary>
/// A processor namespace of an opened assembly (the namespace of one of its processor factories), shown in the assembly tree.
/// </summary>
/// <param name="assembly">The assembly that provides the processors.</param>
/// <param name="namespace">The processor namespace, e.g. 'roland'.</param>
/// <param name="processorInfos">What the processors of the namespace's factory publish, ordered by id.</param>
internal sealed class ProcessorNamespaceItem(LoadedAssembly assembly, string @namespace, IReadOnlyList<ProcessorInfo> processorInfos)
{
    public LoadedAssembly Assembly { get; } = assembly;
    public string Namespace { get; } = @namespace;

    /// <summary>
    /// The processors of the namespace with their stage and published properties.
    /// </summary>
    public IReadOnlyList<ProcessorInfo> ProcessorInfos { get; } = processorInfos;

    /// <summary>
    /// The ids of the processors of the namespace's factory, e.g. 'checksum'.
    /// </summary>
    public IReadOnlyList<string> Processors { get; } = processorInfos.Select(info => info.Key[(@namespace.Length + 1)..]).ToList();

    /// <summary>
    /// The processors with their namespace as used in schemas, e.g. 'roland.checksum'.
    /// </summary>
    public IReadOnlyList<string> ProcessorKeys => Processors.Select(id => $"{Namespace}.{id}").ToList();

    public bool HasProcessors => Processors.Count > 0;

    /// <summary>
    /// A one-line description of the namespace.
    /// </summary>
    public string Summary => $"{Processors.Count} processor(s) in {Assembly.Name}";
}
