using System.Buffers;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Device.Commands;

/// <summary>
/// One field of a command root: the plan field, the channel message it is transferred with (<c>midi.cmd</c>) and the
/// resolution of its physical value in bits (7, 14 or 32), which follows from the size of its data type.
/// </summary>
/// <param name="Index">The position of the field in the root, in plan order.</param>
internal sealed record CommandField(FieldInfo Field, Command Command, int Bits, int Index)
{
    /// <summary>The range of only this field, to convert its value with the serializer.</summary>
    public PlanRange Range { get; init; } = null!;
}

/// <summary>
/// The command map of a command root (<c>midi.role</c> <c>cmd1</c> or <c>cmd2</c>): its fields, each with the channel
/// message it is transferred with, validated against the rules of a command root. It knows the protocol of the root
/// (MIDI 1.0 or 2.0) and the device value the root fixes (<c>midi.device.channel</c>), and
/// converts the value of a field between its logical value (the value model) and its physical value (the value in the
/// channel message) with the serializer, so the field's data type and value processors apply. Not a message map: no
/// SysEx. Immutable and thread-safe.
/// </summary>
internal sealed class CommandMap
{
    /// <summary>The device value of the MIDI channel (0-15).</summary>
    public const string ChannelValue = "channel";

    private readonly Serializer _serializer;
    private readonly Dictionary<Command, CommandField> _byCommand;

    private CommandMap(Serializer serializer, ExecutionPlan plan, SchemaName root, int protocol, DeviceValues device,
        IReadOnlyList<CommandField> fields)
    {
        _serializer = serializer;
        Plan = plan;
        Root = root;
        Protocol = protocol;
        Device = device;
        Fields = fields;
        _byCommand = fields.ToDictionary(field => field.Command);
    }

    public ExecutionPlan Plan { get; }

    public SchemaName Root { get; }

    /// <summary>The MIDI protocol of the root: 1 (<c>cmd1</c>) or 2 (<c>cmd2</c>).</summary>
    public int Protocol { get; }

    /// <summary>The device values the root fixes (<c>midi.device.*</c>); the client's values override them.</summary>
    public DeviceValues Device { get; }

    /// <summary>The fields of the root, in plan order.</summary>
    public IReadOnlyList<CommandField> Fields { get; }

    /// <summary>The field transferred with the command; null when no field is.</summary>
    public CommandField? Find(Command command) => _byCommand.GetValueOrDefault(command);

    /// <summary>The command field of a field of the root's plan; null when it is not one.</summary>
    public CommandField? FieldOf(FieldInfo field) => Fields.FirstOrDefault(candidate => ReferenceEquals(candidate.Field, field));

    /// <summary>The range of the whole root.</summary>
    public PlanRange Whole => Plan.CreateRange(Plan.Root.Path);

    /// <summary>The fields of a range of the root's plan.</summary>
    /// <exception cref="ArgumentException">The range is of another plan.</exception>
    public IReadOnlyList<CommandField> FieldsOf(PlanRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (!ReferenceEquals(range.ExecutionPlan, Plan))
        {
            throw new ArgumentException($"The range is not of the command root '{Root}'.", nameof(range));
        }
        var first = Fields.First(field => ReferenceEquals(field.Field, range.First)).Index;
        var last = Fields.First(field => ReferenceEquals(field.Field, range.Last)).Index;
        return [.. Fields.Where(field => field.Index >= first && field.Index <= last)];
    }

    /// <summary>
    /// The physical value of a field for the logical value the source holds: the field is written with the
    /// serializer, so its data type and value processors apply. False when the source has no value for the field.
    /// </summary>
    public bool TryGetPhysical(CommandField field, IValueSource source, out uint value)
    {
        var watch = new Watch();
        var writer = new ArrayBufferWriter<byte>();
        try
        {
            _serializer.Serialize(field.Range, new WatchedSource(source, watch), writer);
        }
        catch (Exception) when (watch.HadNoValue)
        {
            value = 0;
            return false;
        }

        if (field.Field.DataType.Decode is not { } decode || !decode(writer.WrittenSpan.ToArray(), out var physical) || physical is null)
        {
            throw new InvalidOperationException($"The physical value of '{field.Field.Path}' cannot be decoded with its data type {field.Field.DataType}.");
        }
        value = Convert.ToUInt32(physical, System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>
    /// Gives the logical value of a field for its physical value to the sink: the field is read with the serializer,
    /// so its data type and value processors apply.
    /// </summary>
    public void SetLogical(CommandField field, uint value, IValueSink sink)
    {
        var dataType = field.Field.DataType;
        if (!dataType.TryConvert(value, out var physical) || dataType.Encode is not { } encode || !encode(physical, out var bytes))
        {
            throw new InvalidOperationException($"The value {value} does not fit the data type {dataType} of '{field.Field.Path}'.");
        }
        _serializer.Deserialize(field.Range, new ReadOnlySequence<byte>(bytes), sink);
    }

    /// <summary>
    /// The command map of a command root; throws a <see cref="MessageMapException"/> with all problems when the root
    /// is not a valid command root.
    /// </summary>
    public static CommandMap Create(Serializer serializer, ExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(plan);

        var problems = new MessageMapProblems();
        var map = TryCreate(serializer, plan, problems);
        if (map is null || problems.HasErrors)
        {
            throw new MessageMapException(plan.Name?.FullName ?? plan.Root.Path.Value, problems);
        }
        return map;
    }

    /// <summary>Validates a command root and creates its map; null when it has errors (in <paramref name="problems"/>).</summary>
    internal static CommandMap? TryCreate(Serializer serializer, ExecutionPlan plan, MessageMapProblems problems)
    {
        var rootPath = plan.Root.Path;
        if (plan.Name is not { } root)
        {
            problems.Error(String.Empty, rootPath, "the plan has no schema name; create it with the serializer (GetPlan).");
            return null;
        }

        var document = root.Namespace;
        if (MidiProperties.CommandProtocolOf(plan.Root.Group) is not { } protocol)
        {
            problems.Error(document, rootPath,
                $"'{root}' is not a command root: give it {MidiProperties.Role}=\"{MidiProperties.RoleCommand1}\" (MIDI 1.0) or \"{MidiProperties.RoleCommand2}\" (MIDI 2.0).");
            return null;
        }

        var device = DeviceValues.FromProperties(plan.Root.Group, document, rootPath, problems);
        ValidateDevice(device, document, rootPath, problems);

        var fields = new List<CommandField>();
        Collect(plan.Root, protocol, document, fields, problems);
        CheckDuplicates(fields, document, problems);
        if (fields.Count == 0)
        {
            problems.Error(document, rootPath, $"the command root '{root}' has no fields.");
        }
        if (problems.HasErrors)
        {
            return null;
        }

        var ranged = fields
            .Select(field => field with { Range = plan.CreateRange(field.Field.Path, field.Field.Path) })
            .ToList();
        return new CommandMap(serializer, plan, root, protocol, device, ranged);
    }

    private static void ValidateDevice(DeviceValues device, string document, SchemaPath path, MessageMapProblems problems)
    {
        foreach (var (name, value) in device)
        {
            if (String.Equals(name, ChannelValue, StringComparison.OrdinalIgnoreCase))
            {
                if (value is < 0 or > 15)
                {
                    problems.Error(document, path, $"'{MidiProperties.DevicePrefix}{name}' must be from 0 to 15, not {value}.");
                }
            }
            else
            {
                problems.Warning(document, path,
                    $"'{MidiProperties.DevicePrefix}{name}' is not used: a command root only uses '{MidiProperties.DevicePrefix}{ChannelValue}' (the UMP group is the session's).");
            }
        }
    }

    // the fields of the group and its groups, in plan order; repeats and choices are not allowed
    private static void Collect(GroupInfo group, int protocol, string document, List<CommandField> fields, MessageMapProblems problems)
    {
        foreach (var member in group.Members)
        {
            if (MidiProperties.RoleOf(MidiProperties.SchemaNodeOf(member)) is { } role)
            {
                problems.Error(document, member.Path, $"the role '{role}' does not apply in a command root.");
            }

            switch (member)
            {
                case RepeatInfo:
                    problems.Error(document, member.Path, "a command root cannot have a repeat: its items would have the same command.");
                    break;
                case ChoiceInfo:
                    problems.Error(document, member.Path, "a command root cannot have a choice: it has no layout to choose from.");
                    break;
                case GroupInfo nested:
                    Collect(nested, protocol, document, fields, problems);
                    break;
                case FieldInfo field when CreateField(field, protocol, fields.Count, document, problems) is { } created:
                    fields.Add(created);
                    break;
            }
        }
    }

    private static CommandField? CreateField(FieldInfo field, int protocol, int index, string document, MessageMapProblems problems)
    {
        var path = field.Path;
        if (MidiProperties.Find(field, MidiProperties.Cmd) is not { } text)
        {
            problems.Error(document, path, $"a field of a command root needs {MidiProperties.Cmd} (e.g. cc#74, nrpn#3/12 or pc).");
            return null;
        }
        if (!Command.TryParse(text, out var command, out var error))
        {
            problems.Error(document, path, $"{MidiProperties.Cmd}: {error}");
            return null;
        }
        if (field.HasExpectedValue)
        {
            problems.Error(document, path, "a field of a command root cannot have a fixed value.");
            return null;
        }
        if (field.Pipeline.FieldProcessors.Count > 0 || field.Pipeline.LayoutProcessors.Count > 0)
        {
            problems.Error(document, path, "a field of a command root has no representation or layout processors: its value is a number in a channel message.");
            return null;
        }
        if (ReservedReason(command, protocol) is { } reserved)
        {
            problems.Error(document, path, $"{command} {reserved}");
            return null;
        }
        if (BitsOf(field.DataType, command, protocol, out var allowed) is not { } bits)
        {
            problems.Error(document, path, $"the data type {field.DataType} of {command} in a {(protocol == 1 ? MidiProperties.RoleCommand1 : MidiProperties.RoleCommand2)} root must be {allowed}.");
            return null;
        }
        if (command.Kind == CommandKind.ControlChange && bits == 14 && command.Number > 31)
        {
            problems.Error(document, path, $"a 14-bit {command} needs a controller number from 0 to 31 (its LSB is the number + 32).");
            return null;
        }
        return new CommandField(field, command, bits, index);
    }

    // the resolution of the physical value: the size of the data type, by protocol and kind
    private static int? BitsOf(Jacobi.BinarySerializer.Descriptors.DataTypeDescriptor dataType, Command command, int protocol, out string allowed)
    {
        var size = IsUnsignedInteger(dataType.ClrType) ? dataType.FixedSize : null;
        if (command.Kind == CommandKind.ProgramChange)
        {
            allowed = "a 1-byte unsigned integer (7 bits: the program)";
            return size == 1 ? 7 : null;
        }
        if (protocol == 1)
        {
            allowed = "a 1-byte (7 bits) or 2-byte (14 bits) unsigned integer";
            return size switch { 1 => 7, 2 => 14, _ => null };
        }
        allowed = "a 4-byte unsigned integer (32 bits)";
        return size == 4 ? 32 : null;
    }

    private static bool IsUnsignedInteger(Type type)
        => type == typeof(byte) || type == typeof(ushort) || type == typeof(uint);

    // the controller numbers a field cannot use: they belong to other functions
    private static string? ReservedReason(Command command, int protocol)
    {
        if (command.Kind != CommandKind.ControlChange)
        {
            return null;
        }
        return command.Number switch
        {
            >= 120 => "is a channel mode message, not a setting.",
            6 or 38 or 98 or 99 or 100 or 101 => "is part of an RPN/NRPN; use rpn#<number> or nrpn#<number>.",
            0 or 32 when protocol == 2 => "is a bank select; MIDI 2.0 receivers ignore it (M2-104 7.4.6).",
            _ => null,
        };
    }

    private static void CheckDuplicates(List<CommandField> fields, string document, MessageMapProblems problems)
    {
        var used = new Dictionary<Command, CommandField>();
        foreach (var field in fields)
        {
            IEnumerable<Command> commands = field is { Command.Kind: CommandKind.ControlChange, Bits: 14 }
                ? [field.Command, field.Command with { Number = field.Command.Number + 32 }]
                : [field.Command];
            foreach (var command in commands)
            {
                if (used.TryGetValue(command, out var other))
                {
                    problems.Error(document, field.Field.Path, $"{command} is also used by '{other.Field.Path}'.");
                }
                else
                {
                    used[command] = field;
                }
            }
        }
    }

    // records that the source had no value for a field, to tell a missing value from another failure
    private sealed class Watch
    {
        public bool HadNoValue { get; set; }
    }

    // a scope of the client's source that reports a missing value to the watch
    private sealed class WatchedSource(IValueSource scope, Watch watch) : IValueSource
    {
        public SourceResult GetField(FieldContext context)
        {
            var result = scope.GetField(context);
            if (result.Status == SourceStatus.NoValue)
            {
                watch.HadNoValue = true;
            }
            return result;
        }

        public IValueSource EnterGroup(GroupContext context) => new WatchedSource(scope.EnterGroup(context), watch);
        public int GetCount(RepeatContext context) => scope.GetCount(context);
        public IValueSource EnterItem(RepeatContext context, int index) => new WatchedSource(scope.EnterItem(context, index), watch);
        public int GetSelectedIndex(ChoiceContext context) => scope.GetSelectedIndex(context);
        public IValueSource EnterChoice(ChoiceContext context) => new WatchedSource(scope.EnterChoice(context), watch);
    }
}
