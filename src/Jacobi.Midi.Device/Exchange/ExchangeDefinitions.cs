using System.Reflection;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Exchange;

/// <summary>
/// The exchange files an application knows, by the map root they are for: embedded in the assemblies it registers
/// and read from files. An exchange file is named <c>*.exchange.xml</c> or <c>*.exchange.json</c> (by convention in
/// an <c>Exchange</c> folder). A later file for the same root replaces an earlier one, as a later schema document
/// replaces one with the same name. Thread-safe.
/// </summary>
public sealed class ExchangeDefinitions
{
    private static readonly string[] Extensions = [".exchange.xml", ".exchange.json"];

    private readonly Lock _lock = new();
    private readonly Dictionary<SchemaName, ExchangeDefinitionSet> _sets = [];

    /// <summary>The files, one per map root.</summary>
    public IReadOnlyList<ExchangeDefinitionSet> Sets
    {
        get
        {
            lock (_lock)
            {
                return [.. _sets.Values];
            }
        }
    }

    /// <summary>
    /// True when an embedded resource (or a file) is an exchange file: <c>*.exchange.xml</c> or <c>*.exchange.json</c>.
    /// Loaders of schema resources skip these.
    /// </summary>
    public static bool IsDefinitionResource(string resourceName)
    {
        ArgumentNullException.ThrowIfNull(resourceName);
        return Extensions.Any(extension => resourceName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Adds a file; it replaces the file for the same root, which is returned (null when there was none).</summary>
    public ExchangeDefinitionSet? Add(ExchangeDefinitionSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        lock (_lock)
        {
            _sets.Remove(set.For, out var replaced);
            _sets[set.For] = set;
            return replaced;
        }
    }

    /// <summary>
    /// Reads and adds the exchange files embedded in an assembly (see <see cref="IsDefinitionResource"/>). A file that
    /// cannot be read does not stop the others: its problems are returned.
    /// </summary>
    public IReadOnlyList<ExchangeDefinitionException> AddAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var source = assembly.GetName().Name ?? assembly.FullName ?? "assembly";
        var problems = new List<ExchangeDefinitionException>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(IsDefinitionResource).Order(StringComparer.Ordinal))
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(resource)
                    ?? throw new ExchangeDefinitionException($"{source}/{resource}", ["the resource cannot be read."]);
                Add(ExchangeDefinitionReader.Read(stream, $"{source}/{resource}"));
            }
            catch (ExchangeDefinitionException exception)
            {
                problems.Add(exception);
            }
        }
        return problems;
    }

    /// <summary>Reads and adds an exchange file (.xml or .json).</summary>
    /// <exception cref="ExchangeDefinitionException">The file is not valid.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public ExchangeDefinitionSet AddFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        var set = ExchangeDefinitionReader.Read(stream, path);
        Add(set);
        return set;
    }

    /// <summary>The file for a map root; null when there is none.</summary>
    public ExchangeDefinitionSet? Find(SchemaName root)
    {
        lock (_lock)
        {
            return _sets.GetValueOrDefault(root);
        }
    }

    /// <summary>The exchange with the name in the file for a map root; null when there is none.</summary>
    public ExchangeDefinition? Find(SchemaName root, string name) => Find(root)?.Find(name);
}
