using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Xml;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.SchemaEditor.Services;
using YamlDotNet.Core;

namespace Jacobi.Midi.SchemaEditor.Assemblies;

/// <summary>
/// The assemblies (.dll files) that were opened in the editor, with the embedded schemas and the processors they provide.
/// The embedded schemas are managed by one shared <see cref="SchemaSetBuilder"/> and the processors by one shared
/// <see cref="ProcessorManager"/>, so references can be looked up and validated across all opened assemblies.
/// Embedded schemas are not editable and are not part of the open schema documents.
/// </summary>
internal sealed class AssemblyWorkspace : ObservableObject
{
    /// <summary>
    /// The embedded schemas of all opened assemblies. Not compiled: compile it when the references must be resolved.
    /// </summary>
    public SchemaSetBuilder SchemaSet { get; } = new();

    /// <summary>
    /// The processor factories of all opened assemblies, by namespace.
    /// </summary>
    public ProcessorManager Processors { get; } = new();

    public ObservableCollection<LoadedAssembly> Assemblies { get; } = [];

    public AssemblyWorkspace()
    {
        Assemblies.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAssemblies));
            OnPropertyChanged(nameof(HasNoAssemblies));
        };
    }

    public bool HasAssemblies => Assemblies.Count > 0;
    public bool HasNoAssemblies => !HasAssemblies;

    /// <summary>
    /// Returns the embedded schema that <paramref name="node"/> belongs to, or null when it is not part of one.
    /// </summary>
    public LoadedSchema? FindSchema(EditNode node)
    {
        while (node.Parent is not null)
        {
            node = node.Parent;
        }
        return Assemblies.SelectMany(assembly => assembly.Schemas).FirstOrDefault(schema => ReferenceEquals(schema.Document.Schema, node));
    }

    /// <summary>
    /// Adds the embedded schemas of <paramref name="assembly"/> to the <see cref="SchemaSet"/> and passes the same assembly
    /// to the <see cref="Processors"/>. A resource or processor that cannot be loaded is reported in
    /// <see cref="LoadedAssembly.Problems"/> and does not prevent the rest from loading.
    /// Returns the already opened entry when the assembly was opened before.
    /// </summary>
    /// <param name="assembly">The assembly to take the schemas and processors from.</param>
    /// <param name="location">The file name or path to show; empty to use the assembly name.</param>
    public LoadedAssembly Load(Assembly assembly, string location)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var existing = Assemblies.FirstOrDefault(loaded => loaded.FullName == assembly.FullName);
        if (existing is not null)
        {
            return existing;
        }

        var problems = new List<string>();
        var schemas = LoadSchemas(assembly, problems);
        var processorNamespaces = LoadProcessors(assembly, problems);

        var name = assembly.GetName();
        var loaded = new LoadedAssembly(assembly.FullName ?? name.Name ?? location, name.Name ?? location,
            name.Version?.ToString() ?? String.Empty, location, schemas, processorNamespaces, problems);
        Assemblies.Add(loaded);
        return loaded;
    }

    // SchemaSetBuilder.LoadFromAssembly adds the schemas to the set. It throws on a resource it cannot read (e.g. YAML);
    // the schemas added before that stay in the set but are not shown, and the exception is reported as a problem.
    private List<LoadedSchema> LoadSchemas(Assembly assembly, List<string> problems)
    {
        var schemas = new List<LoadedSchema>();
        // LoadFromAssembly reads the resources in this order
        var resources = assembly.GetManifestResourceNames();
        try
        {
            var documents = SchemaSet.LoadFromAssembly(assembly, String.Empty).ToList();
            for (var index = 0; index < documents.Count && index < resources.Length; index++)
            {
                var document = documents[index];
                var resource = resources[index];
                try
                {
                    // a read-only copy of the authored schema to show; taken before the set is compiled
                    var format = Path.GetExtension(resource).ToLowerInvariant() switch
                    {
                        ".json" => SchemaFormat.Json,
                        ".xml" => SchemaFormat.Xml,
                        _ => SchemaFormat.Yaml
                    };
                    var copy = new SchemaWorkspaceDocument(EditNodeMapper.ToEditDocument(document), format, resource) { IsReadOnly = true };
                    schemas.Add(new LoadedSchema(document.Name, resource, copy));
                }
                catch (ArgumentException exception)
                {
                    problems.Add($"{resource}: {exception.Message}");
                }
            }
        }
        catch (Exception exception) when (IsSchemaError(exception))
        {
            problems.Add($"Schemas: {exception.Message}");
        }
        return schemas;
    }

    // the processors are kept by namespace: the namespaces the assembly added to the processor manager, with their processors
    private List<(string Namespace, IReadOnlyList<string> Processors)> LoadProcessors(Assembly assembly, List<string> problems)
    {
        var known = Processors.Namespaces.ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<(string, IReadOnlyList<string>)> Added() => Processors.Namespaces
            .Where(@namespace => !known.Contains(@namespace))
            .Order()
            .Select(@namespace => (@namespace, GetProcessorIds(@namespace, problems)))
            .ToList();

        try
        {
            Processors.LoadFromAssembly(assembly);
            return Added();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var reason = exception.LoaderExceptions.FirstOrDefault()?.Message ?? exception.Message;
            problems.Add($"Processors: {reason}");
            return Added();
        }
        catch (Exception exception) when (exception is InvalidOperationException or TargetInvocationException
            or MissingMethodException or TypeLoadException or FileLoadException or FileNotFoundException)
        {
            problems.Add($"Processors: {(exception as TargetInvocationException)?.InnerException?.Message ?? exception.Message}");
            // the factories that were registered before the failure stay registered
            return Added();
        }
    }

    private IReadOnlyList<string> GetProcessorIds(string @namespace, List<string> problems)
    {
        try
        {
            return Processors.TryGetFactory(@namespace, out var factory) ? factory.Processors.Order().ToList() : [];
        }
        catch (Exception exception) when (exception is NotImplementedException or NotSupportedException or InvalidOperationException)
        {
            problems.Add($"Processors of '{@namespace}': {exception.Message}");
            return [];
        }
    }

    private static bool IsSchemaError(Exception exception)
        => exception is ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or IOException
            or JsonException
            or XmlException
            or YamlException;
}
