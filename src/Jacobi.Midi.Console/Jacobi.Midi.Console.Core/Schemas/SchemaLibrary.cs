using System.Reflection;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Console.Core.Schemas;

/// <summary>
/// The schemas and processors the console works with, compiled into one <see cref="SchemaSet"/> and
/// <see cref="Serializer"/> with the serializer's own objects. Built from the built-in assemblies and the sources of
/// the rig, its devices and a dashboard (see <see cref="SchemaLibraryBuilder"/>). Problems with a source do not stop
/// the others; all are reported in <see cref="Problems"/>, and a set that does not compile has no serializer.
/// </summary>
public sealed class SchemaLibrary
{
    internal SchemaLibrary(ProcessorManager processors, SchemaSet? schemas, Serializer? serializer, IReadOnlyList<SchemaLibraryProblem> problems,
        IReadOnlyDictionary<string, string> documentSources)
    {
        Processors = processors;
        Schemas = schemas;
        Serializer = serializer;
        Maps = serializer is null ? null : new AddressMapAdapter(serializer);
        Problems = problems;
        DocumentSources = documentSources;
    }

    /// <summary>
    /// Where each schema document of the library comes from, by its name: the path of a schema file, or
    /// <c>assembly/resource</c> for a schema embedded in an assembly. A document that replaced another has the source of
    /// the one that replaced it.
    /// </summary>
    public IReadOnlyDictionary<string, string> DocumentSources { get; }

    /// <summary>
    /// The root groups of the schemas (<c>document.group</c>), any of which can be the root of a device: its groups and
    /// fields are the device's parameters. The address maps (<see cref="Maps"/>) come first: the roots data messages
    /// carry; whether and how a device's values are sent and received is up to them (the Device project), not to the
    /// console. Empty when the schemas do not compile.
    /// </summary>
    public IReadOnlyList<SchemaName> Roots
    {
        get
        {
            if (Schemas is null)
                return [];
            var roots = Schemas.Documents.SelectMany(document => document.Roots.Select(root => new SchemaName($"{document.Name}{SchemaName.Separator}{root.Name}"))).ToList();
            return [.. roots.OrderBy(root => Maps?.IsMap(root) == true ? 0 : 1)];
        }
    }

    /// <summary>True when the name is a root group of the schemas (see <see cref="Roots"/>).</summary>
    public bool HasRoot(SchemaName name) => Roots.Contains(name);

    /// <summary>
    /// The number of items of a repeat of a root: in the address space of an address map (<c>midi.repeat</c>), else the
    /// repeat's count in the schema; null for a node that is not a repeat, or a repeat whose count is a value.
    /// </summary>
    /// <exception cref="AddressMapException">The root is an address map that is not valid.</exception>
    public int? ItemCount(SchemaName root, string repeat)
    {
        ArgumentException.ThrowIfNullOrEmpty(repeat);
        if (Maps?.IsMap(root) == true)
            return Maps.ItemCount(root, new SchemaPath(repeat));
        return Serializer?.GetPlan(root).Find(new SchemaPath(repeat)) is RepeatInfo { Count: int count } ? count : null;
    }

    /// <summary>
    /// The roots (see <see cref="Roots"/>, address maps first) of the documents a schema file brought, whether or not they
    /// replaced documents of the same name (a schema built into the console).
    /// </summary>
    public IReadOnlyList<SchemaName> RootsFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var fullPath = Path.GetFullPath(path);
        return RootsWhere(source => Path.IsPathRooted(source) && String.Equals(Path.GetFullPath(source), fullPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The roots of the documents an assembly embeds (see <see cref="RootsFromFile"/>).</summary>
    public IReadOnlyList<SchemaName> RootsFromAssembly(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyName);
        return RootsWhere(source => source.StartsWith(assemblyName + "/", StringComparison.OrdinalIgnoreCase));
    }

    // the roots whose document (the namespace of the root) comes from a matching source
    private IReadOnlyList<SchemaName> RootsWhere(Func<string, bool> isFrom)
        => [.. Roots.Where(root => DocumentSources.TryGetValue(root.Namespace, out var source) && isFrom(source))];

    public ProcessorManager Processors { get; }

    /// <summary>The compiled schemas; null when they do not compile (see <see cref="Problems"/>).</summary>
    public SchemaSet? Schemas { get; }

    /// <summary>The serializer of the compiled schemas and the processors; null when the schemas do not compile.</summary>
    public Serializer? Serializer { get; }

    /// <summary>The address maps of the schemas (item counts of map repeats); null when the schemas do not compile.</summary>
    public AddressMapAdapter? Maps { get; }

    public IReadOnlyList<SchemaLibraryProblem> Problems { get; }

    public bool IsCompiled => Serializer is not null;
}

/// <summary>
/// A problem with a source of a <see cref="SchemaLibrary"/>: an assembly or schema file that cannot be loaded, a
/// schema that replaces another, or schemas that do not compile.
/// </summary>
/// <param name="Source">The assembly, file or resource; 'compile' for the compilation of the whole set.</param>
/// <param name="IsError">False for information that does not prevent the use of the library (a replaced schema).</param>
public sealed record SchemaLibraryProblem(string Source, string Message, bool IsError = true);

/// <summary>
/// Collects assemblies and schema files in order and builds a <see cref="SchemaLibrary"/>. A schema document of a
/// later source replaces an earlier one with the same name: a device or dashboard can customise a built-in schema.
/// </summary>
public sealed class SchemaLibraryBuilder
{
    private static readonly string[] SchemaExtensions = [".xml", ".json"];

    private readonly ProcessorManager _processors = new();
    private readonly Dictionary<string, (SchemaDocument Document, string Source)> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = [];
    private readonly HashSet<string> _assemblies = new(StringComparer.Ordinal);
    private readonly List<SchemaLibraryProblem> _problems = [];

    /// <summary>
    /// The assemblies every library has: the serializer's processors (<c>sys</c>) and the base <c>midi</c> schema
    /// with its processors. Device assemblies (Roland, Yamaha, ...) are added by the host.
    /// </summary>
    public static IReadOnlyList<Assembly> CoreAssemblies { get; } =
    [
        typeof(Jacobi.BinarySerializer.Processors.ProcessorFactory).Assembly,
        typeof(Jacobi.Midi.Device.Processors.ProcessorFactory).Assembly,
    ];

    /// <summary>Adds the embedded schemas and the processor factories of an assembly; an assembly is added once.</summary>
    public SchemaLibraryBuilder AddAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var source = assembly.GetName().Name ?? assembly.FullName ?? "assembly";
        if (!_assemblies.Add(assembly.FullName ?? source))
            return this;

        try
        {
            _processors.LoadFromAssembly(assembly);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ReflectionTypeLoadException
            or TargetInvocationException or MissingMethodException or TypeLoadException)
        {
            _problems.Add(new(source, $"processors: {(exception as TargetInvocationException)?.InnerException?.Message ?? exception.Message}"));
        }

        foreach (var resource in assembly.GetManifestResourceNames().Where(IsSchema))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            AddDocument($"{source}/{resource}", () => new SchemaSetBuilder().LoadFromBinary(resource, stream));
        }
        return this;
    }

    /// <summary>Loads an assembly from a file and adds it (see <see cref="AddAssembly(Assembly)"/>).</summary>
    public SchemaLibraryBuilder AddAssembly(string path)
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or ArgumentException
            or NotSupportedException or System.Security.SecurityException)
        {
            _problems.Add(new(path, $"the assembly cannot be loaded: {exception.Message}"));
            return this;
        }
        return AddAssembly(assembly);
    }

    /// <summary>Adds a schema file (.xml or .json).</summary>
    public SchemaLibraryBuilder AddSchemaFile(string path)
    {
        if (!File.Exists(path))
        {
            _problems.Add(new(path, "the schema file does not exist."));
            return this;
        }
        AddDocument(path, () => new SchemaSetBuilder().LoadFile(path));
        return this;
    }

    /// <summary>Adds the assemblies and schema files of a rig, device or dashboard; relative paths are relative to <paramref name="baseDirectory"/>.</summary>
    public SchemaLibraryBuilder Add(SchemaSources sources, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        foreach (var assembly in sources.Assemblies)
            AddAssembly(Resolve(assembly, baseDirectory));
        foreach (var schema in sources.Schemas)
            AddSchemaFile(Resolve(schema, baseDirectory));
        return this;
    }

    public SchemaLibrary Build()
    {
        var problems = new List<SchemaLibraryProblem>(_problems);
        var builder = new SchemaSetBuilder();
        foreach (var name in _order)
            builder.AddDocument(_documents[name].Document);

        try
        {
            var schemas = builder.Compile();
            var serializer = new SerializerBuilder().AddSchemas(schemas).AddProcessors(_processors).Build();
            return new SchemaLibrary(_processors, schemas, serializer, problems, DocumentSources());
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException
            or KeyNotFoundException)
        {
            problems.Add(new("compile", exception.Message));
            return new SchemaLibrary(_processors, null, null, problems, DocumentSources());
        }
    }

    private Dictionary<string, string> DocumentSources()
        => _documents.ToDictionary(document => document.Key, document => document.Value.Source, StringComparer.OrdinalIgnoreCase);

    private void AddDocument(string source, Func<SchemaDocument> load)
    {
        SchemaDocument document;
        try
        {
            document = load();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException
            or IOException or System.Xml.XmlException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            _problems.Add(new(source, $"the schema cannot be read: {exception.Message}"));
            return;
        }

        if (_documents.TryGetValue(document.Name, out var existing))
            _problems.Add(new(source, $"the schema '{document.Name}' replaces the one of {existing.Source}.", IsError: false));
        else
            _order.Add(document.Name);
        _documents[document.Name] = (document, source);
    }

    private static bool IsSchema(string resource)
        => SchemaExtensions.Any(extension => resource.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static string Resolve(string path, string? baseDirectory)
        => baseDirectory is null || Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path);
}
