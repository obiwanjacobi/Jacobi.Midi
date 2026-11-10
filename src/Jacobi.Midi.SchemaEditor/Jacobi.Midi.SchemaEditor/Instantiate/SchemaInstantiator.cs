using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Device.Exchange;
using Jacobi.Midi.SchemaEditor.Models;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.SchemaEditor.Instantiate;

/// <summary>
/// One SysEx message written from an instance: its bytes and the time to wait after sending it (the <c>messageInterval</c>
/// of the <see cref="ExchangeNames.SendData"/> exchange of an address map's exchange file; zero otherwise).
/// </summary>
internal sealed record SerializedMessage(byte[] Bytes, TimeSpan Interval);

/// <summary>
/// Creates the <see cref="InstanceNode"/> trees ("instances") of the roots of a compiled document
/// (a <see cref="SchemaSetCompilation"/>, see <see cref="SchemaSetWorkspace"/>) and serializes or deserializes their values.
/// It walks the compiled schema, so node defs are expanded and open choices can look up their bodies in the whole set.
/// The kind of message map of a body is detected by Midi.Device (<see cref="MessageMaps"/>, <see cref="InstanceBody.MapKind"/>).
/// Its messages are written, read and described by Midi.Device (<see cref="MessageMaps"/>) for every kind of map: e.g. the
/// repeats of an address map body get the item count of the address space, and the groups the user includes are sent at their addresses.
/// </summary>
internal sealed class SchemaInstantiator
{
    private readonly SchemaSet _schemas;
    private readonly IReadOnlyList<EditDocument> _edits;
    private readonly List<string> _warnings;
    private readonly Serializer _serializer;
    private readonly ExchangeDefinitions? _exchanges;
    private MessageMaps? _maps;

    /// <param name="compilation">The compiled document with the schemas it was compiled with.</param>
    /// <param name="processors">
    /// The processors the schemas use (those of the opened assemblies): the instances are created, checked, written and read
    /// with the plans of the serializer, as the device does.
    /// </param>
    /// <param name="exchanges">The exchange files of the opened assemblies: the time between the messages sent to a device; null: none.</param>
    public SchemaInstantiator(SchemaSetCompilation compilation, IProcessorProvider processors, ExchangeDefinitions? exchanges = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(processors);
        _schemas = compilation.Schemas;
        _serializer = new SerializerBuilder()
            .AddSchemas(_schemas)
            .AddProcessors(processors)
            .Build();
        _edits = compilation.Documents;
        _exchanges = exchanges;
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
    public static SchemaInstantiator Create(EditDocument document, IEnumerable<EditDocument> workspace, IProcessorProvider processors)
        => new(SchemaSetWorkspace.Compile(document, workspace), processors);

    /// <summary>
    /// Creates the instance of a root of the instantiated document (by its name).
    /// The root is expanded; the large parts below it start collapsed (see <see cref="InstanceNode.ApplyDefaultExpansion"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">The document has no such root.</exception>
    public InstanceGroup CreateRoot(string rootName)
    {
        var name = new SchemaName($"{DocumentName}{SchemaName.Separator}{rootName}");
        var root = _schemas.FindRoot(name);
        return MarkMessageDump(Top(BuildGroup(new InstanceGroup(root, new SchemaPath(root.Name)), root, DocumentName, CreatePlan(root)), DocumentName), name);
    }

    /// <summary>
    /// Creates the instance of any group (a root, a nested group, repeat or choice, or a node def) of the instantiated document.
    /// The instance is expanded; the large parts below it start collapsed (see <see cref="InstanceNode.ApplyDefaultExpansion"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">The compiled document has no such node.</exception>
    public InstanceNode Create(EditGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var (current, top, names, documentName) = FindCompiled(group);
        var path = String.Join(SchemaPath.Separator, names);
        if (current is null)
        {
            throw new InvalidOperationException($"The compiled schema has no node '{path}'.");
        }
        var instance = Top(Build(current, documentName, new SchemaPath(path), top is SchemaGroup outermost ? CreatePlan(outermost) : null), documentName);
        // the instance of a root: it may be a dump message itself
        return instance is InstanceGroup root && names.Count == 1 && FindRootName(group) is { } rootName
            ? MarkMessageDump(root, rootName)
            : instance;
    }

    /// <summary>
    /// Marks how the role fields of a root that is a dump message itself (<see cref="MessageMapKind.MessageDump"/>) get
    /// their value: the device values the root fixes are supplied by its map (see <see cref="MessageMapInfo.DescribeRole"/>),
    /// the others are given in the instance. Any other root is left as it is.
    /// </summary>
    private InstanceGroup MarkMessageDump(InstanceGroup instance, SchemaName root)
    {
        if (KindOf(root) == MessageMapKind.MessageDump)
        {
            foreach (var field in RoleFields(instance))
            {
                field.RoleSupply = DescribeRole(root, field.Role!);
            }
        }
        return instance;
    }

    // the kind of the message map of a root; null when it has no valid map (e.g. a plain group)
    private MessageMapKind? KindOf(SchemaName root)
    {
        try
        {
            return Maps.Info(root).Kind;
        }
        catch (Exception exception) when (exception is MessageMapException or ExecutionPlanException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    // the top node of an instance tree: it knows its document and is expanded
    private static T Top<T>(T instance, string documentName)
        where T : InstanceNode
    {
        instance.Document = documentName;
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
    /// <param name="warnings">Receives the situations the user may not expect, e.g. a group that is sent as part of an atomic group.</param>
    /// <returns>The serialized messages: one, or one per block of an address map.</returns>
    /// <exception cref="InvalidOperationException">The plan cannot be built or a value cannot be written; the message describes the problem.</exception>
    public IReadOnlyList<SerializedMessage> Serialize(SchemaName root, InstanceNode instance, ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(warnings);
        var plan = _serializer.GetPlan(root);
        var isRoot = IsRoot(root, instance);
        // the engine enters the root with the source itself; the items of a root repeat are found by its path
        var source = new InstanceValueSource(isRoot && instance is not InstanceRepeat ? instance : null, instance);
        if (TransferOf(root, instance, throwWhenEmpty: true) is { } transfer)
        {
            // written by the map from the instance (the whole message): it supplies the roles of each block and the
            // device values the root fixes
            var interval = _exchanges?.Find(transfer.Root, ExchangeNames.SendData)?.MessageInterval ?? TimeSpan.Zero;
            return [.. Maps.SaveMessages(transfer.Ranges, source, new DeviceValues(), transfer.Message, warnings)
                .Select(bytes => new SerializedMessage(bytes, interval))];
        }

        var output = new ArrayBufferWriter<byte>();
        if (isRoot)
        {
            _serializer.Serialize(plan, source, output);
        }
        else
        {
            _serializer.Serialize(plan.CreateRange(instance.Path), source, output);
        }
        return [new SerializedMessage(output.WrittenSpan.ToArray(), TimeSpan.Zero)];
    }

    /// <summary>
    /// Describes how a body with a message map is sent: for an address map the data messages of the included groups
    /// with their address and size, or why they cannot be sent; for a carried dump the one message with the whole body.
    /// Null when the instance has no such body.
    /// </summary>
    public string? DescribeTransfer(SchemaName root, InstanceNode instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        try
        {
            if (TransferOf(root, instance, throwWhenEmpty: false) is not { } transfer)
            {
                return null;
            }
            if (transfer.Ranges.Count == 0)
            {
                return $"Nothing of the address map '{transfer.Root}' is included: include the groups to send.";
            }

            var warnings = new List<string>();
            var plan = Maps.Plan(transfer.Ranges, new DeviceValues(), transfer.Message, warnings);
            const int shown = 8;
            var blocks = plan.Blocks.Take(shown).Select(block => block.Description);
            var text = $"Sends {plan.Blocks.Count} {plan.Message.Name} message(s): {String.Join(", ", blocks)}{(plan.Blocks.Count > shown ? ", …" : String.Empty)}.";
            return String.Join(Environment.NewLine, [text, .. warnings]);
        }
        catch (Exception exception) when (exception is MessageMapException or ExecutionPlanException
            or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return $"Cannot send the included groups: {exception.Message}";
        }
    }

    /// <summary>
    /// Reads <paramref name="bytes"/> into <paramref name="instance"/> with the plan of <paramref name="root"/> (see <see cref="FindRootName"/>):
    /// the reverse of <see cref="Serialize"/>. An instance of a nested node is read as a range of that plan.
    /// The fields are filled out, the items of the repeats added and the alternatives of the choices selected as they are read;
    /// the body of a data open choice is selected from the data (see <see cref="SelectBodies"/>), any other open choice
    /// must be selected beforehand. When the read fails, the values read up to the failure remain.
    /// A message whose data body has a message map is read by Midi.Device (<see cref="MessageMaps.ReadMessage"/>): the map
    /// recognises it (or the read fails with the reason) and places the data at the block it carries, e.g. one patch of
    /// an address map; otherwise the serializer reads as is.
    /// In an address map body, the part that was read is included (and the rest left out), so it can be sent back.
    /// </summary>
    /// <param name="warnings">Receives the situations the user may not expect (e.g. another body selected from the data), also when the read fails.</param>
    /// <param name="accumulate">
    /// True to add the values to those of the earlier reads instead of clearing them first, e.g. the data messages that
    /// each carry a part of one patch; the part that was read is then included as well. A repeat of an address map that holds
    /// values keeps its item: a message for another item of it is not read into it (with a warning).
    /// </param>
    /// <returns>The number of bytes that were read (the bytes after them are not part of the instance).</returns>
    /// <exception cref="ExecutionPlanException">The plan cannot be built.</exception>
    /// <exception cref="InvalidOperationException">The bytes do not match the schema; the message describes the problem.</exception>
    public long Deserialize(SchemaName root, InstanceNode instance, byte[] bytes, ICollection<string> warnings, bool accumulate = false)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(warnings);
        SelectBodies(root, instance, bytes, warnings, accumulate);
        var plan = _serializer.GetPlan(root);
        var input = new ReadOnlySequence<byte>(bytes);
        var isRoot = String.Equals(plan.Root.Path.Value, instance.Path.Value, StringComparison.OrdinalIgnoreCase);
        // the engine enters the root with the sink itself; the items of a root repeat are found by its path
        var sink = new InstanceValueSink(isRoot && instance is not InstanceRepeat ? instance : null, instance);
        sink.Begin(accumulate);
        var completed = false;
        try
        {
            var read = (FindAddressMap(instance) ?? FindMapBody(instance, MessageMapKind.CarriedDump)) is { } body
                // any device id: the editor matches what it is given
                ? Maps.ReadMessage(body.Body.Root, MessageName(root, body.Choice), bytes, sink, new DeviceValues())
                : isRoot
                    ? _serializer.Deserialize(plan, input, sink)
                    : _serializer.Deserialize(plan.CreateRange(instance.Path), input, sink);
            completed = true;
            return read;
        }
        finally
        {
            sink.End(completed);
            IncludeRead(instance, sink, accumulate);
            foreach (var skipped in sink.Skipped)
            {
                warnings.Add($"Not read: the message addresses another item than the accumulated values, {skipped}.");
            }
        }
    }

    /// <summary>
    /// Selects the body of each data open choice (<c>midi.role="data"</c>) of the instance from the message, as
    /// Midi.Device recognises it (<see cref="MessageMaps.FindCarriedRoot"/>, with any device id): e.g. the body the
    /// model in a dump selects, or the address map of the model. A choice whose message is not recognised keeps its
    /// selection. Another body than the selected one is selected with a warning; when accumulating, the selected body
    /// (with its values) is kept, with a warning.
    /// </summary>
    private void SelectBodies(SchemaName root, InstanceNode instance, byte[] bytes, ICollection<string> warnings, bool accumulate)
    {
        foreach (var choice in DataChoices(instance).ToList())
        {
            if (MessageOf(choice) is not { } message)
            {
                continue;
            }
            SchemaName? carried;
            try
            {
                carried = Maps.FindCarriedRoot(new SchemaName($"{root.Namespace}{SchemaName.Separator}{message.Path.Value}"), bytes, new DeviceValues());
            }
            catch (Exception exception) when (exception is ExecutionPlanException or InvalidOperationException or ArgumentException)
            {
                warnings.Add($"'{choice.Path}': the body cannot be found from the data: {exception.Message}");
                continue;
            }
            if (carried is not { } found
                || choice.Options.FirstOrDefault(option => String.Equals(option.Name, found.FullName, StringComparison.OrdinalIgnoreCase)) is not { } carriedOption
                || ReferenceEquals(carriedOption, choice.SelectedOption))
            {
                continue;
            }

            if (choice.SelectedOption is { } selected)
            {
                if (accumulate)
                {
                    warnings.Add($"'{choice.Path}': the message carries '{found}', not '{selected.Name}'; the accumulated body is kept.");
                    continue;
                }
                warnings.Add($"'{choice.Path}': the message carries '{found}'; it is selected instead of '{selected.Name}'.");
            }
            choice.SelectedOption = carriedOption;
        }
    }

    // the data open choices of an instance, outside the bodies (a body holds no message)
    private static IEnumerable<InstanceChoice> DataChoices(InstanceNode node)
    {
        if (node is InstanceChoice { IsOpen: true } choice && AddressMapTags.IsData(choice.Schema))
        {
            yield return choice;
            yield break;
        }
        foreach (var child in node.Children())
        {
            foreach (var found in DataChoices(child))
            {
                yield return found;
            }
        }
    }

    // in an address map body: include the part of the map that was read (the nearest node that can be included) and leave
    // out the rest; when accumulating, the part that was read is included as well (see InstanceBody.KeepContiguous)
    private static void IncludeRead(InstanceNode instance, InstanceValueSink sink, bool accumulate)
    {
        if (FindAddressMap(instance) is not { } map)
        {
            return;
        }
        for (var node = sink.ReadNodeIn(map.Body); node is not null; node = node.Parent)
        {
            if (node.CanInclude)
            {
                if (accumulate)
                {
                    node.IsIncluded = true;
                }
                else
                {
                    map.Body.IncludeOnly(node);
                }
                return;
            }
        }
    }

    // An included node as the nodes it transfers: the node itself, or, when a repeat of the map (which shows one item) is
    // in it, its parts: the repeat stands for its one item, not for all the items of the address space.
    private static IEnumerable<InstanceNode> Occurrences(InstanceNode node)
    {
        if (!HasAddressedRepeat(node))
        {
            return [node];
        }
        return node.Children().Where(child => child.CanInclude).SelectMany(Occurrences);
    }

    // the range of the root's plan one node occurrence covers: a group or field, all items of a repeat, or one item
    // (from its first to its last member)
    private static PlanRange RangeOf(ExecutionPlan plan, InstanceNode node)
    {
        if (node is InstanceRepeatItem item)
        {
            var members = item.Children().ToList();
            return members.Count > 0
                ? plan.CreateRange(members[0].Path, item.Instance, members[^1].Path, item.Instance)
                : throw new InvalidOperationException($"The item '{item.DisplayPath}' has no members.");
        }
        return plan.CreateRange(node.Path, node.Instance, node.Path, node.Instance);
    }

    // true when the instance is the root of the plan of the root (not a node in it)
    private static bool IsRoot(SchemaName root, InstanceNode instance)
        => String.Equals(root.Name, instance.Path.Value, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What a body with a message map, or a root that is a dump message itself, transfers: the ranges of the root (the
    /// included nodes of an address map; the whole root of a dump), the data message (the instantiated one) and the
    /// root (whose exchange file gives the time between the messages). Null when the instance has neither.
    /// </summary>
    private (IReadOnlyList<PlanRange> Ranges, SchemaName Message, SchemaName Root)? TransferOf(SchemaName root, InstanceNode instance, bool throwWhenEmpty)
    {
        if (FindAddressMap(instance) is { } map)
        {
            return (Selection(map.Body, throwWhenEmpty), MessageName(root, map.Choice), map.Body.Root);
        }
        if (FindMapBody(instance, MessageMapKind.CarriedDump) is { } dump)
        {
            return ([WholeRange(dump.Body.Root)], MessageName(root, dump.Choice), dump.Body.Root);
        }
        if (IsRoot(root, instance) && instance is InstanceGroup && KindOf(root) == MessageMapKind.MessageDump)
        {
            return ([WholeRange(root)], root, root);
        }
        return null;
    }

    // the schema name of the message that holds a data open choice, e.g. 'Roland.Message16.DT1'
    private static SchemaName MessageName(SchemaName root, InstanceChoice data)
        => MessageOf(data) is { } message
            ? new SchemaName($"{root.Namespace}{SchemaName.Separator}{message.Path.Value}")
            : throw new InvalidOperationException($"'{data.Path}' is not in a message.");

    private PlanRange WholeRange(SchemaName root)
    {
        var plan = _serializer.GetPlan(root);
        return plan.CreateRange(plan.Root.Path);
    }

    private static bool HasAddressedRepeat(InstanceNode node)
        => node is InstanceRepeat { IsAddressed: true } || node.Children().Any(HasAddressedRepeat);

    // created when it is used: the message maps of the roots, any kind (address map or dump)
    private MessageMaps Maps => _maps ??= new MessageMaps(_serializer);

    /// <summary>The open choice (of the instance) whose selected body is an address map; null when there is none.</summary>
    private static (InstanceChoice Choice, InstanceBody Body)? FindAddressMap(InstanceNode node)
        => FindMapBody(node, MessageMapKind.Address);

    /// <summary>The open choice (of the instance) whose selected body has a message map of the kind; null when there is none.</summary>
    private static (InstanceChoice Choice, InstanceBody Body)? FindMapBody(InstanceNode node, MessageMapKind kind)
    {
        if (node is InstanceChoice { Selected: InstanceBody body } choice && body.MapKind == kind)
        {
            return (choice, body);
        }
        foreach (var child in node.Children())
        {
            if (FindMapBody(child, kind) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    // the nodes of the map that are included as a whole
    private List<PlanRange> Selection(InstanceBody body, bool throwWhenEmpty = true)
    {
        var plan = _serializer.GetPlan(body.Root);
        var selection = body.IncludedNodes().SelectMany(Occurrences).Select(node => RangeOf(plan, node)).ToList();
        if (selection.Count == 0 && throwWhenEmpty)
        {
            throw new InvalidOperationException($"Nothing of the address map '{body.Root}' is included: include the groups to send.");
        }
        return selection;
    }

    /// <summary>
    /// Finds the compiled node of an edited group (in any of the documents) by its names from the root or node def down.
    /// </summary>
    /// <returns>The compiled node, the outermost compiled node above it (a root or node def), its names and the name of its document.</returns>
    private (SchemaNode? Node, SchemaNode? Top, IReadOnlyList<string> Names, string Document) FindCompiled(EditGroup group)
        => CompiledNodeLocator.Find(_schemas, _edits, group);

    /// <summary>
    /// Finds the plain groups (in any depth, not only roots) of the authored documents that declare <paramref name="contract"/> as their nodeDef.
    /// The authored documents are searched; each group found is then looked up in the compiled schema.
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

    /// <param name="plan">The plan of the outermost node (see <see cref="CreatePlan"/>): its fields check the values of the instance fields.</param>
    /// <param name="mapItems">In the body of an address map: the item count of a repeat (by its path) in the address space; the repeat gets one item.</param>
    private InstanceNode Build(SchemaNode node, string ns, SchemaPath path, ExecutionPlan? plan, Func<SchemaPath, int?>? mapItems = null)
    {
        switch (node)
        {
            case SchemaField field:
                var constant = field.Value is string text ? text : null;
                return plan?.Find(path) is FieldInfo compiled
                    ? new InstanceField(field, path, constant, value => InstanceValueSource.ToLogical(compiled, value, out _),
                        InstanceValueSource.OptionsOf(compiled))
                    : new InstanceField(field, path, constant);
            case SchemaRepeat repeat:
                return BuildRepeat(repeat, ns, path, plan, mapItems);
            case SchemaChoice choice:
                return BuildChoice(choice, ns, path, plan);
            case SchemaGroup group:
                return BuildGroup(new InstanceGroup(group, path), group, ns, plan, mapItems);
            default:
                throw new NotSupportedException($"Unsupported schema node type: {node.GetType().Name}.");
        }
    }

    private T BuildGroup<T>(T instance, SchemaGroup group, string ns, ExecutionPlan? plan, Func<SchemaPath, int?>? mapItems = null)
        where T : InstanceGroup
    {
        foreach (var member in group.Members)
        {
            instance.AddMember(Build(member, ns, instance.Path.Append(member.Name), plan, mapItems));
        }
        return instance;
    }

    private InstanceRepeat BuildRepeat(SchemaRepeat schema, string ns, SchemaPath path, ExecutionPlan? plan, Func<SchemaPath, int?>? mapItems)
    {
        int? constantCount = schema.Count is int count ? count : null;
        // a repeat of an address map has no count in the schema: it shows one item of the address space
        var addressItemCount = constantCount is null ? mapItems?.Invoke(path) : null;
        var repeat = new InstanceRepeat(schema, path, constantCount,
            (owner, index) => BuildGroup(new InstanceRepeatItem(schema, path, owner, index), schema, ns, plan, mapItems), addressItemCount);
        // an open repeat starts with one copy, so the user sees which fields it holds
        for (var index = 0; index < (constantCount ?? 1); index++)
        {
            repeat.AddNewItem();
        }
        return repeat;
    }

    private InstanceChoice BuildChoice(SchemaChoice schema, string ns, SchemaPath path, ExecutionPlan? plan)
    {
        int? constantIndex = schema.SelectedIndex is int index ? index : null;
        if (schema.MemberNodeDef is not { } contract)
        {
            var alternatives = schema.Members
                .Select(member => new ChoiceOption(member.Name, _ => Build(member, ns, path.Append(member.Name), plan)))
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
            AddWarning($"The open choice '{path}' has no bodies: none of the compiled schemas has a group with the nodeDef '{qualified}'.");
        }
        bodies.Sort((left, right) => String.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        // the selected index is not used by an open choice
        var open = new InstanceChoice(schema, path, bodies, constantIndex: null);
        if (AddressMapTags.IsData(schema))
        {
            open.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(InstanceChoice.Selected))
                {
                    MarkRoles(open);
                }
            };
        }
        return open;
    }

    /// <summary>
    /// Marks how the role fields of the message that <paramref name="data"/> is the data of (<c>midi.role="data"</c>) get
    /// their value: with a body that has a message map (any kind) the map supplies some of them (see
    /// <see cref="MessageMapInfo.DescribeRole"/>), the others are given in the instance.
    /// </summary>
    private void MarkRoles(InstanceChoice data)
    {
        if (MessageOf(data) is not { } group)
        {
            return;
        }

        var map = data.Selected is InstanceBody { MapKind: not null } body ? body.Root : (SchemaName?)null;
        foreach (var field in RoleFields(group))
        {
            field.RoleSupply = map is { } root ? DescribeRole(root, field.Role!) : null;
        }
    }

    // The message that holds a data open choice, found as Midi.Device finds a data message: the member group of the
    // schema root that holds the data (path 'root.message'), or the root itself when the data is one of its members.
    private static InstanceGroup? MessageOf(InstanceChoice data)
    {
        var message = data.Parent;
        while (message?.Parent is not null && message.Path.Segments.Count() > 2)
        {
            message = message.Parent;
        }
        return message as InstanceGroup;
    }

    // the role fields of a message; the data (an open choice) has its own fields
    private static IEnumerable<InstanceField> RoleFields(InstanceGroup group)
        => group.Members.SelectMany(member => member switch
        {
            InstanceField { Role: not null } field => [field],
            InstanceGroup child => RoleFields(child),
            _ => Enumerable.Empty<InstanceField>(),
        });

    private string? DescribeRole(SchemaName map, string role)
    {
        try
        {
            return Maps.Info(map).DescribeRole(role);
        }
        catch (Exception exception) when (exception is MessageMapException or ExecutionPlanException or InvalidOperationException or ArgumentException)
        {
            // the body only has a map kind when its map is valid (see DetectMessageMap)
            AddWarning($"How the role '{role}' is supplied by '{map}' is not known: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// The body of an open choice. When it is an address map, its repeats show one item of the address space (selected
    /// by its index), its groups and repeats can be included (or left out); nothing is included yet (including one group is
    /// quicker than leaving out many).
    /// </summary>
    private InstanceBody BuildBody(SchemaGroup compiled, string ns, SchemaName root)
    {
        var (map, note) = DetectMessageMap(root);
        var body = new InstanceBody(compiled, new SchemaPath(compiled.Name)) { Root = root, MapKind = map?.Kind, Note = note };
        // only an address map has repeats whose items are not in the schema (one item of the address space is shown)
        BuildGroup(body, compiled, ns, CreatePlan(compiled), body.IsAddressMap ? map!.ItemCount : null);
        if (body.IsAddressMap)
        {
            AllowInclusion(body);
        }
        return body;
    }

    /// <summary>
    /// The message map of a body's root (created and validated by Midi.Device), and why it cannot be used when it is not
    /// valid (the body is then a plain group). A root that is neither carried by a data message nor a SysEx message
    /// itself has no map: it is a plain group without a note (e.g. a body of an open choice that is not MIDI data).
    /// </summary>
    private (MessageMapInfo? Map, string? Note) DetectMessageMap(SchemaName root)
    {
        try
        {
            return (Maps.Info(root), null);
        }
        catch (MessageMapException exception) when (exception.Kind is null or MessageMapKind.MessageDump)
        {
            return (null, null);
        }
        catch (MessageMapException exception)
        {
            var kind = exception.Kind == MessageMapKind.Address ? "an address map" : "a dump carried by a data message";
            return (null, $"'{root}' is {kind}, but it cannot be used as one (it is a plain group here): {exception.Message}");
        }
        catch (Exception exception) when (exception is ExecutionPlanException or InvalidOperationException or ArgumentException)
        {
            return (null, $"'{root}' has no message map (it is a plain group here): {exception.Message}");
        }
    }

    /// <summary>
    /// The plan of an outermost node (a root, node def or body of an open choice): its fields check the values the user types
    /// (see <see cref="InstanceValueSource.ToLogical"/>). Null when the plan cannot be built (a warning says why).
    /// </summary>
    private ExecutionPlan? CreatePlan(SchemaGroup top)
    {
        try
        {
            return _serializer.GetPlan(top);
        }
        catch (Exception exception) when (exception is ExecutionPlanException or InvalidOperationException or ArgumentException)
        {
            AddWarning($"The values of '{top.Name}' are not checked: its plan cannot be built: {exception.Message}");
            return null;
        }
    }

    private void AddWarning(string warning)
    {
        if (!_warnings.Contains(warning))
        {
            _warnings.Add(warning);
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
