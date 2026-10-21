using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.Instantiate;

/// <summary>
/// Compiles an edited document together with the documents it includes (taken from the open documents of the workspace)
/// and creates the <see cref="InstanceNode"/> trees ("instances") of its roots.
/// It walks the compiled schema, so node defs are expanded and open choices can look up their bodies in the whole set.
/// </summary>
internal sealed class SchemaInstantiator
{
    private readonly SchemaSet _schemas;
    private readonly IReadOnlyList<EditDocument> _edits;

    private SchemaInstantiator(SchemaSet schemas, IReadOnlyList<EditDocument> edits, string documentName)
    {
        _schemas = schemas;
        _edits = edits;
        DocumentName = documentName;
    }

    /// <summary>The name of the document that was instantiated.</summary>
    public string DocumentName { get; }

    /// <summary>
    /// Compiles <paramref name="document"/> and (transitively) the included documents found in <paramref name="workspace"/>.
    /// Other documents of the workspace that include (a document of) that set are added when they can be compiled with it:
    /// they can hold the bodies of its open choices (a device schema defines the bodies for the open choice of a vendor schema).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// An included document is not loaded or the schema does not compile; the message describes the problem.
    /// </exception>
    public static SchemaInstantiator Create(EditDocument document, IEnumerable<EditDocument> workspace)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(workspace);
        var candidates = workspace.ToList();
        var documents = GetClosure(document, candidates, out var missing);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The schema '{document.Name}' cannot be instantiated: the included schema(s) {String.Join(", ", missing.Select(name => $"'{name}'"))} are not loaded.");
        }

        var dependents = new List<EditDocument>(documents);
        foreach (var candidate in candidates.Where(candidate => !documents.Contains(candidate)))
        {
            var closure = GetClosure(candidate, candidates, out var candidateMissing);
            if (candidateMissing.Count == 0 && closure.Any(documents.Contains))
            {
                dependents.AddRange(closure.Where(item => !dependents.Contains(item)));
            }
        }

        try
        {
            // a dependent that does not compile must not prevent instantiating the document itself
            return Compile(document, dependents, documents.Count == dependents.Count ? null : documents);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidOperationException($"The schema '{document.Name}' does not compile: {exception.Message}", exception);
        }
    }

    private static SchemaInstantiator Compile(EditDocument document, List<EditDocument> documents, List<EditDocument>? fallback)
    {
        try
        {
            var builder = new SchemaSetBuilder();
            foreach (var item in documents)
            {
                builder.AddDocument(EditNodeMapper.ToSchemaDocument(item));
            }
            return new SchemaInstantiator(builder.Compile(), documents, document.Name);
        }
        catch (Exception exception) when (fallback is not null
            && exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return Compile(document, fallback, null);
        }
    }

    /// <summary>
    /// The document and (transitively) its included documents that are in <paramref name="candidates"/>;
    /// <paramref name="missing"/> receives the names of the included schemas that are not.
    /// </summary>
    private static List<EditDocument> GetClosure(EditDocument document, List<EditDocument> candidates, out List<string> missing)
    {
        var documents = new List<EditDocument>();
        missing = [];
        var queue = new Queue<EditDocument>();
        queue.Enqueue(document);
        while (queue.TryDequeue(out var current))
        {
            if (documents.Contains(current))
            {
                continue;
            }
            documents.Add(current);
            foreach (var include in current.Includes)
            {
                var included = candidates.Find(other => !ReferenceEquals(other, current)
                    && String.Equals(other.Name, include.Schema, StringComparison.OrdinalIgnoreCase));
                if (included is not null)
                {
                    queue.Enqueue(included);
                }
                else if (!missing.Contains(include.Schema, StringComparer.OrdinalIgnoreCase))
                {
                    missing.Add(include.Schema);
                }
            }
        }
        return documents;
    }

    /// <summary>
    /// Creates the instance of a root of the instantiated document (by its name).
    /// </summary>
    /// <exception cref="InvalidOperationException">The document has no such root.</exception>
    public InstanceGroup CreateRoot(string rootName)
    {
        var root = _schemas.FindRoot(new SchemaName($"{DocumentName}{SchemaName.Separator}{rootName}"));
        return BuildGroup(new InstanceGroup(root, new SchemaPath(root.Name)), root, DocumentName);
    }

    /// <summary>
    /// Creates the instance of any group (a root, a nested group, repeat or choice, or a node def) of the instantiated document.
    /// </summary>
    /// <exception cref="InvalidOperationException">The compiled document has no such node.</exception>
    public InstanceNode Create(EditGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var (current, names, documentName) = FindCompiled(group);
        var path = String.Join(SchemaPath.Separator, names);
        return current is null
            ? throw new InvalidOperationException($"The compiled schema has no node '{path}'.")
            : Build(current, documentName, new SchemaPath(path));
    }

    /// <summary>
    /// The name of the root (<c>Document.Root</c>) that holds <paramref name="group"/>: the root of the plan its instance is written with.
    /// Null for (a group in) a node def: it has no plan.
    /// </summary>
    public SchemaName? FindRootName(EditGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        EditNode topNode = group;
        for (EditNode? node = group; node is not null and not EditDocument; node = node.Parent)
        {
            topNode = node;
        }
        if (topNode.Parent is not EditDocument owner)
        {
            return null;
        }
        return new SchemaName($"{owner.Name}{SchemaName.Separator}{topNode.Name}");
    }

    /// <summary>
    /// Writes the values of <paramref name="instance"/> with the plan of <paramref name="root"/> (see <see cref="FindRootName"/>).
    /// An instance of a nested node is written as a range of that plan: only its fields are written,
    /// but the logic of the groups that lead to it (e.g. SysEx framing, checksums) still applies.
    /// </summary>
    /// <param name="processors">The processors the schemas use (those of the opened assemblies).</param>
    /// <returns>The serialized bytes.</returns>
    /// <exception cref="InvalidOperationException">The plan cannot be built or a value cannot be written; the message describes the problem.</exception>
    public byte[] Serialize(SchemaName root, InstanceNode instance, IProcessorProvider processors)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(processors);
        var serializer = new SerializerBuilder()
            .AddSchemas(_schemas)
            .AddProcessors(processors)
            .Build();
        var plan = serializer.GetPlan(root);
        var output = new ArrayBufferWriter<byte>();
        if (String.Equals(plan.Root.Path.Value, instance.Path.Value, StringComparison.OrdinalIgnoreCase))
        {
            // the engine enters the root with the source itself; the items of a root repeat are found by its path
            serializer.Serialize(plan, new InstanceValueSource(instance is InstanceRepeat ? null : instance, instance), output);
        }
        else
        {
            serializer.Serialize(plan.CreateRange(instance.Path), new InstanceValueSource(null, instance), output);
        }
        return output.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Finds the compiled node of an edited group (in any of the documents) by its names from the root or node def down.
    /// </summary>
    private (SchemaNode? Node, List<string> Names, string Document) FindCompiled(EditGroup group)
    {
        var names = new List<string>();
        EditNode topNode = group;
        for (EditNode? node = group; node is not null and not EditDocument; node = node.Parent)
        {
            names.Insert(0, node.Name);
            topNode = node;
        }
        var owner = topNode.Parent as EditDocument ?? _edits.FirstOrDefault(edit => edit.NodeDefs.Contains(topNode)) ?? _edits[0];
        var document = _schemas.Documents.First(candidate => String.Equals(candidate.Name, owner.Name, StringComparison.OrdinalIgnoreCase));
        // the outermost node is a root of the document or a node def (which has no parent)
        var top = owner.Members.Contains(topNode) ? (IReadOnlyList<SchemaNode>)document.Roots : document.NodeDefs;
        SchemaNode? current = top.FirstOrDefault(candidate => String.Equals(candidate.Name, names[0], StringComparison.OrdinalIgnoreCase));
        foreach (var name in names.Skip(1))
        {
            current = (current as SchemaGroup)?.Members.FirstOrDefault(member => String.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        return (current, names, owner.Name);
    }

    /// <summary>
    /// Finds the plain groups (in any depth, not only roots) of the authored documents that declare <paramref name="contract"/> as their nodeDef.
    /// The authored documents are used because compiling replaces the nodeDef of a nested group by its expansion.
    /// </summary>
    private IEnumerable<(string Document, EditGroup Group)> FindConforming(SchemaName contract)
    {
        foreach (var edit in _edits)
        {
            var pending = new Stack<EditNode>(edit.Members.Reverse());
            while (pending.TryPop(out var node))
            {
                if (node.GetType() == typeof(EditGroup) && node.NodeDef is { } nodeDef && Qualify(nodeDef, edit.Name) == contract)
                {
                    yield return (edit.Name, (EditGroup)node);
                }
                if (node is EditGroup { Members: var members })
                {
                    foreach (var member in members.Reverse())
                    {
                        pending.Push(member);
                    }
                }
            }
        }
    }

    private InstanceNode Build(SchemaNode node, string ns, SchemaPath path)
    {
        switch (node)
        {
            case SchemaField field:
                var dataType = field.DataType?.ToString() ?? String.Empty;
                return new InstanceField(field, path, field.Value is string constant ? constant : null,
                    value => DataTypeValueValidator.Validate(_edits, ns, dataType, value));
            case SchemaRepeat repeat:
                return BuildRepeat(repeat, ns, path);
            case SchemaChoice choice:
                return BuildChoice(choice, ns, path);
            case SchemaGroup group:
                return BuildGroup(new InstanceGroup(group, path), group, ns);
            default:
                throw new NotSupportedException($"Unsupported schema node type: {node.GetType().Name}.");
        }
    }

    private T BuildGroup<T>(T instance, SchemaGroup group, string ns)
        where T : InstanceGroup
    {
        foreach (var member in group.Members)
        {
            instance.AddMember(Build(member, ns, instance.Path.Append(member.Name)));
        }
        return instance;
    }

    private InstanceRepeat BuildRepeat(SchemaRepeat schema, string ns, SchemaPath path)
    {
        int? constantCount = schema.Count is int count ? count : null;
        var repeat = new InstanceRepeat(schema, path, constantCount,
            (owner, index) => BuildGroup(new InstanceRepeatItem(schema, path, owner, index), schema, ns));
        // an open repeat starts with one copy, so the user sees which fields it holds
        for (var index = 0; index < (constantCount ?? 1); index++)
        {
            repeat.AddNewItem();
        }
        return repeat;
    }

    private InstanceChoice BuildChoice(SchemaChoice schema, string ns, SchemaPath path)
    {
        int? constantIndex = schema.SelectedIndex is int index ? index : null;
        if (schema.MemberNodeDef is not { } contract)
        {
            var alternatives = schema.Members
                .Select(member => new ChoiceOption(member.Name, _ => Build(member, ns, path.Append(member.Name))))
                .ToList();
            return new InstanceChoice(schema, path, alternatives, constantIndex);
        }

        // open choice: any group that declares the contract as its nodeDef can be the body
        var qualified = Qualify(contract, ns);
        var bodies = new List<ChoiceOption>();
        foreach (var (documentName, group) in FindConforming(qualified))
        {
            // the group must be found in the compiled schema (its names are relative to its own document)
            if (FindCompiled(group).Node is not SchemaGroup compiled)
            {
                continue;
            }
            bodies.Add(new ChoiceOption($"{documentName}{SchemaName.Separator}{group.Name}", _ => BuildGroup(
                new InstanceBody(compiled, new SchemaPath(compiled.Name)), compiled, documentName)));
        }
        bodies.Sort((left, right) => String.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        // the selected index is not used by an open choice
        return new InstanceChoice(schema, path, bodies, constantIndex: null);
    }

    private static SchemaName Qualify(SchemaName name, string ns)
        => String.IsNullOrEmpty(name.Namespace) ? new SchemaName($"{ns}{SchemaName.Separator}{name.Name}") : name;
}
