using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.SchemaEditor.Models;

namespace Jacobi.Midi.SchemaEditor.Instantiate;

/// <summary>
/// One SysEx message written from an instance: its bytes and the time to wait after sending it (<c>midi.messageInterval</c>
/// of the data message of an address map; zero otherwise).
/// </summary>
internal sealed record SerializedMessage(byte[] Bytes, TimeSpan Interval);

/// <summary>
/// Creates the <see cref="InstanceNode"/> trees ("instances") of the roots of a compiled document
/// (a <see cref="SchemaSetCompilation"/>, see <see cref="SchemaSetWorkspace"/>) and serializes or deserializes their values.
/// It walks the compiled schema, so node defs are expanded and open choices can look up their bodies in the whole set.
/// A body that is an address map is detected and handled by the Device addressing code (<see cref="AddressMapAdapter"/>):
/// its repeats get the item count of the address space, and the groups the user includes are sent at their addresses.
/// </summary>
internal sealed class SchemaInstantiator
{
    private readonly SchemaSet _schemas;
    private readonly IReadOnlyList<EditDocument> _edits;
    private readonly List<string> _warnings;
    private readonly IProcessorProvider? _processors;
    private AddressMapAdapter? _adapter;

    /// <param name="compilation">The compiled document with the schemas it was compiled with.</param>
    /// <param name="processors">
    /// The processors the schemas use (those of the opened assemblies); needed to detect the address maps when a body is
    /// selected. Without them a body is always a plain group.
    /// </param>
    public SchemaInstantiator(SchemaSetCompilation compilation, IProcessorProvider? processors = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        _processors = processors;
        _schemas = compilation.Schemas;
        _edits = compilation.Documents;
        _warnings = [.. compilation.Warnings];
        DocumentName = compilation.Document.Name;
    }

    /// <summary>The name of the document that was instantiated.</summary>
    public string DocumentName { get; }

    /// <summary>The documents that were compiled: the instantiated document, its includes and the other documents that compile with them.</summary>
    public IReadOnlyList<EditDocument> Documents => _edits;

    /// <summary>
    /// The situations the user may not expect: those of the compilation (a schema that is used instead of another one or is left out)
    /// and an open choice without bodies. Empty when there are none.
    /// </summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// Compiles <paramref name="document"/> with the documents of <paramref name="workspace"/>
    /// (see <see cref="SchemaSetWorkspace.Compile(EditDocument, IEnumerable{EditDocument})"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">An include of the document is not loaded or the document does not compile.</exception>
    public static SchemaInstantiator Create(EditDocument document, IEnumerable<EditDocument> workspace, IProcessorProvider? processors = null)
        => new(SchemaSetWorkspace.Compile(document, workspace), processors);

    /// <summary>
    /// Creates the instance of a root of the instantiated document (by its name).
    /// The root is expanded; the large parts below it start collapsed (see <see cref="InstanceNode.ApplyDefaultExpansion"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">The document has no such root.</exception>
    public InstanceGroup CreateRoot(string rootName)
    {
        var root = _schemas.FindRoot(new SchemaName($"{DocumentName}{SchemaName.Separator}{rootName}"));
        return Expanded(BuildGroup(new InstanceGroup(root, new SchemaPath(root.Name)), root, DocumentName));
    }

    /// <summary>
    /// Creates the instance of any group (a root, a nested group, repeat or choice, or a node def) of the instantiated document.
    /// The instance is expanded; the large parts below it start collapsed (see <see cref="InstanceNode.ApplyDefaultExpansion"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">The compiled document has no such node.</exception>
    public InstanceNode Create(EditGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var (current, names, documentName) = FindCompiled(group);
        var path = String.Join(SchemaPath.Separator, names);
        return current is null
            ? throw new InvalidOperationException($"The compiled schema has no node '{path}'.")
            : Expanded(Build(current, documentName, new SchemaPath(path)));
    }

    private static T Expanded<T>(T instance)
        where T : InstanceNode
    {
        instance.ApplyDefaultExpansion();
        instance.IsExpanded = true;
        return instance;
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
    /// When the body of an open choice is an address map, the included groups of the map are written as the data messages
    /// that carry them (one or more), with the address and size of each message calculated from the map.
    /// </summary>
    /// <param name="processors">The processors the schemas use (those of the opened assemblies).</param>
    /// <param name="warnings">Receives the situations the user may not expect, e.g. a group that is sent as part of an atomic group.</param>
    /// <returns>The serialized messages: one, or one per block of an address map.</returns>
    /// <exception cref="InvalidOperationException">The plan cannot be built or a value cannot be written; the message describes the problem.</exception>
    public IReadOnlyList<SerializedMessage> Serialize(SchemaName root, InstanceNode instance, IProcessorProvider processors, ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(processors);
        ArgumentNullException.ThrowIfNull(warnings);
        var serializer = CreateSerializer(processors);
        var plan = serializer.GetPlan(root);
        var isRoot = String.Equals(plan.Root.Path.Value, instance.Path.Value, StringComparison.OrdinalIgnoreCase);
        // the engine enters the root with the source itself; the items of a root repeat are found by its path
        var source = new InstanceValueSource(isRoot && instance is not InstanceRepeat ? instance : null, instance);
        if (FindAddressMap(instance) is { } map)
        {
            var messages = new AddressMapAdapter(serializer)
                .WriteData(root, map.Choice.Path, map.Body.Root, Selection(map.Body), source, warnings);
            return [.. messages.Select(message => new SerializedMessage(message.SysEx, message.Message.Interval))];
        }

        var output = new ArrayBufferWriter<byte>();
        if (isRoot)
        {
            serializer.Serialize(plan, source, output);
        }
        else
        {
            serializer.Serialize(plan.CreateRange(instance.Path), source, output);
        }
        return [new SerializedMessage(output.WrittenSpan.ToArray(), TimeSpan.Zero)];
    }

    /// <summary>
    /// Describes how the included groups of an address map body are sent: the data messages with their address and size,
    /// or why they cannot be sent. Null when the instance has no address map body.
    /// </summary>
    /// <param name="processors">The processors the schemas use (those of the opened assemblies).</param>
    public string? DescribeTransfer(SchemaName root, InstanceNode instance, IProcessorProvider processors)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(processors);
        if (FindAddressMap(instance) is not { } map)
        {
            return null;
        }
        var selection = Selection(map.Body, throwWhenEmpty: false);
        if (selection.Count == 0)
        {
            return $"Nothing of the address map '{map.Body.Root}' is included: include the groups to send.";
        }

        try
        {
            var warnings = new List<string>();
            var plan = new AddressMapAdapter(CreateSerializer(processors)).PlanData(root, map.Choice.Path, map.Body.Root, selection, warnings);
            const int shown = 8;
            var blocks = plan.Blocks.Take(shown).Select(block => $"{plan.AddressSpace.Format(block.Addresses.Start)} ({block.Addresses.Size} bytes)");
            var text = $"Sends {plan.Blocks.Count} {plan.Message.Name} message(s) at {String.Join(", ", blocks)}{(plan.Blocks.Count > shown ? ", …" : String.Empty)}.";
            return String.Join(Environment.NewLine, [text, .. warnings]);
        }
        catch (Exception exception) when (exception is AddressMapException or ExecutionPlanException
            or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return $"Cannot send the included groups: {exception.Message}";
        }
    }

    /// <summary>
    /// Reads <paramref name="bytes"/> into <paramref name="instance"/> with the plan of <paramref name="root"/> (see <see cref="FindRootName"/>):
    /// the reverse of <see cref="Serialize"/>. An instance of a nested node is read as a range of that plan.
    /// The fields are filled out, the items of the repeats added and the alternatives of the choices selected as they are read;
    /// the body of an open choice must be selected beforehand. When the read fails, the values read up to the failure remain.
    /// When the schemas hold an address map, the reading is left to <see cref="AddressMapAdapter"/>: it ends the repeats of
    /// the map and reads a data message into the group of the map it carries; otherwise the serializer reads as is.
    /// In an address map body, the part that was read is included (and the rest left out), so it can be sent back.
    /// </summary>
    /// <param name="processors">The processors the schemas use (those of the opened assemblies).</param>
    /// <param name="warnings">Receives the situations the user may not expect (e.g. a message that is read only partly into a map), also when the read fails.</param>
    /// <returns>The number of bytes that were read (the bytes after them are not part of the instance).</returns>
    /// <exception cref="ExecutionPlanException">The plan cannot be built.</exception>
    /// <exception cref="InvalidOperationException">The bytes do not match the schema; the message describes the problem.</exception>
    public long Deserialize(SchemaName root, InstanceNode instance, byte[] bytes, IProcessorProvider processors, ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(processors);
        ArgumentNullException.ThrowIfNull(warnings);
        var serializer = CreateSerializer(processors);
        var reader = new AddressMapAdapter(serializer);
        var plan = serializer.GetPlan(root);
        var input = new ReadOnlySequence<byte>(bytes);
        var isRoot = String.Equals(plan.Root.Path.Value, instance.Path.Value, StringComparison.OrdinalIgnoreCase);
        // the engine enters the root with the sink itself; the items of a root repeat are found by its path
        var sink = new InstanceValueSink(isRoot && instance is not InstanceRepeat ? instance : null, instance);
        sink.Begin();
        var completed = false;
        try
        {
            var read = isRoot
                ? reader.Deserialize(plan, input, sink, warnings)
                : reader.Deserialize(plan.CreateRange(instance.Path), input, sink, warnings);
            completed = true;
            return read;
        }
        finally
        {
            sink.End(completed);
            IncludeRead(instance, sink);
        }
    }

    // in an address map body: include the part of the map that was read (the nearest node that can be included), leave out the rest
    private static void IncludeRead(InstanceNode instance, InstanceValueSink sink)
    {
        if (FindAddressMap(instance) is not { } map)
        {
            return;
        }
        for (var node = sink.ReadNodeIn(map.Body); node is not null; node = node.Parent)
        {
            if (node.CanInclude)
            {
                map.Body.IncludeOnly(node);
                return;
            }
        }
    }

    private Serializer CreateSerializer(IProcessorProvider processors)
        => new SerializerBuilder()
            .AddSchemas(_schemas)
            .AddProcessors(processors)
            .Build();

    /// <summary>The open choice (of the instance) whose selected body is an address map; null when there is none.</summary>
    private static (InstanceChoice Choice, InstanceBody Body)? FindAddressMap(InstanceNode node)
    {
        if (node is InstanceChoice { Selected: InstanceBody { IsAddressMap: true } body } choice)
        {
            return (choice, body);
        }
        foreach (var child in node.Children())
        {
            if (FindAddressMap(child) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    // the nodes of the map that are included as a whole
    private static List<MapOccurrence> Selection(InstanceBody body, bool throwWhenEmpty = true)
    {
        var selection = body.IncludedNodes().Select(node => new MapOccurrence(node.Path, node.Instance)).ToList();
        if (selection.Count == 0 && throwWhenEmpty)
        {
            throw new InvalidOperationException($"Nothing of the address map '{body.Root}' is included: include the groups to send.");
        }
        return selection;
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

    /// <param name="mapItems">In the body of an address map: the item count of a repeat (by its path) in the address space.</param>
    private InstanceNode Build(SchemaNode node, string ns, SchemaPath path, Func<SchemaPath, int?>? mapItems = null)
    {
        switch (node)
        {
            case SchemaField field:
                var dataType = field.DataType?.ToString() ?? String.Empty;
                return new InstanceField(field, path, field.Value is string constant ? constant : null,
                    value => DataTypeValueValidator.Validate(_edits, ns, dataType, value));
            case SchemaRepeat repeat:
                return BuildRepeat(repeat, ns, path, mapItems);
            case SchemaChoice choice:
                return BuildChoice(choice, ns, path);
            case SchemaGroup group:
                return BuildGroup(new InstanceGroup(group, path), group, ns, mapItems);
            default:
                throw new NotSupportedException($"Unsupported schema node type: {node.GetType().Name}.");
        }
    }

    private T BuildGroup<T>(T instance, SchemaGroup group, string ns, Func<SchemaPath, int?>? mapItems = null)
        where T : InstanceGroup
    {
        foreach (var member in group.Members)
        {
            instance.AddMember(Build(member, ns, instance.Path.Append(member.Name), mapItems));
        }
        return instance;
    }

    private InstanceRepeat BuildRepeat(SchemaRepeat schema, string ns, SchemaPath path, Func<SchemaPath, int?>? mapItems)
    {
        // a repeat of an address map has no count in the schema: the address space has the items
        int? constantCount = schema.Count is int count ? count : mapItems?.Invoke(path);
        var repeat = new InstanceRepeat(schema, path, constantCount,
            (owner, index) => BuildGroup(new InstanceRepeatItem(schema, path, owner, index), schema, ns, mapItems));
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
            var name = new SchemaName($"{documentName}{SchemaName.Separator}{group.Name}");
            bodies.Add(new ChoiceOption(name.FullName, _ => BuildBody(compiled, documentName, name)));
        }
        if (bodies.Count == 0)
        {
            var warning = $"The open choice '{path}' has no bodies: none of the compiled schemas has a group with the nodeDef '{qualified}'.";
            if (!_warnings.Contains(warning))
            {
                _warnings.Add(warning);
            }
        }
        bodies.Sort((left, right) => String.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        // the selected index is not used by an open choice
        return new InstanceChoice(schema, path, bodies, constantIndex: null);
    }

    /// <summary>
    /// The body of an open choice. When it is an address map, its repeats get the item count of the address space,
    /// its groups and repeats can be included (or left out); nothing is included yet (including one group is quicker than
    /// leaving out many).
    /// </summary>
    private InstanceBody BuildBody(SchemaGroup compiled, string ns, SchemaName root)
    {
        var (isMap, note) = DetectAddressMap(root);
        var body = new InstanceBody(compiled, new SchemaPath(compiled.Name)) { Root = root, IsAddressMap = isMap, Note = note };
        BuildGroup(body, compiled, ns, isMap ? path => _adapter!.ItemCount(root, path) : null);
        if (isMap)
        {
            AllowInclusion(body);
        }
        return body;
    }

    // whether the root is an address map, and why it cannot be used as one (it is then a plain group)
    private (bool IsMap, string? Note) DetectAddressMap(SchemaName root)
    {
        if (_processors is null)
        {
            return (false, null);
        }
        try
        {
            _adapter ??= new AddressMapAdapter(CreateSerializer(_processors));
            if (!_adapter.IsMap(root))
            {
                return (false, null);
            }
            // builds and validates the map
            _adapter.ItemCount(root, new SchemaPath(root.Name));
            return (true, null);
        }
        catch (Exception exception) when (exception is AddressMapException or ExecutionPlanException or InvalidOperationException or ArgumentException)
        {
            return (false, $"'{root}' is an address map, but it cannot be used as one (it is a plain group here): {exception.Message}");
        }
    }

    private static void AllowInclusion(InstanceNode node)
    {
        if (node is InstanceGroup or InstanceRepeat)
        {
            node.CanInclude = true;
            foreach (var child in node.Children())
            {
                AllowInclusion(child);
            }
        }
    }

    private static SchemaName Qualify(SchemaName name, string ns)
        => String.IsNullOrEmpty(name.Namespace) ? new SchemaName($"{ns}{SchemaName.Separator}{name.Name}") : name;
}
