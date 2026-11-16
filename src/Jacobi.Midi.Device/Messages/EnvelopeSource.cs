using System.Globalization;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// The body of a data message: the selected group (the device map), the nodes inside it that are written or read
/// (a range of the map plan; null: the whole map), and the value model scope for their values.
/// </summary>
internal readonly record struct EnvelopeBody<TScope>(SchemaName Group, PlanRange? Target, TScope Scope);

/// <summary>
/// Makes the selection of the data open choice from an <see cref="EnvelopeBody{TScope}"/>: the whole body, or the range
/// of its target.
/// </summary>
internal static class EnvelopeBodySelection
{
    public static OpenChoiceSourceResult ToSelection(this EnvelopeBody<IValueSource> body)
        => body.Target is { } target
            ? OpenChoiceSourceResult.SelectRange(body.Scope, body.Group, target.First.Path, target.BeginInstance, target.Last.Path, target.EndInstance)
            : OpenChoiceSourceResult.Select(body.Scope, body.Group);

    public static OpenChoiceSinkResult ToSelection(this EnvelopeBody<IValueSink> body)
        => body.Target is { } target
            ? OpenChoiceSinkResult.SelectRange(body.Scope, body.Group, target.First.Path, target.BeginInstance, target.Last.Path, target.EndInstance)
            : OpenChoiceSinkResult.Select(body.Scope, body.Group);
}

/// <summary>
/// The value source for writing an envelope message: answers the fields by their <c>midi.role</c> (the values of the
/// transferred block, e.g. the address and size of an address map, and the device values), leaves the rest to the
/// schema (constants, derived sizes, checksums) and selects the body for the data open choice.
/// With a client source (the client's value model of the whole message, scope by scope), the device values the
/// <paramref name="device"/> values do not hold and the fields without a role are taken from it, and the body scope
/// is the client's scope for the open choice.
/// </summary>
/// <param name="blockRoles">The values of the roles that depend on the transferred block (e.g. <c>address</c>, <c>size</c>), by role.</param>
/// <param name="body">Selects the body for the data open choice; gets the client's scope for it (null without a client source).</param>
/// <param name="client">The client's value source at the same scope; null when the client only supplies the body values.</param>
internal sealed class EnvelopeSource(IReadOnlyDictionary<string, long> blockRoles, DeviceValues device,
    Func<IValueSource?, EnvelopeBody<IValueSource>>? body = null, IValueSource? client = null)
    : IOpenChoiceSource, IOpenRepeatSource
{
    /// <summary>No roles that depend on a block: a message that is the same for every block.</summary>
    public static readonly IReadOnlyDictionary<string, long> NoBlockRoles = new Dictionary<string, long>();

    public SourceResult GetField(FieldContext context)
    {
        if (MidiProperties.Find(context.Field, MidiProperties.Role) is not { } role)
        {
            return client?.GetField(context) ?? SourceResult.NoValue();
        }

        long? value = role.StartsWith(MidiProperties.RoleDevicePrefix, StringComparison.OrdinalIgnoreCase)
            ? device.TryGet(role[MidiProperties.RoleDevicePrefix.Length..], out var deviceValue) ? deviceValue : null
            : blockRoles.TryGetValue(role, out var blockValue) ? blockValue : null;
        if (value is null)
        {
            if (role.StartsWith(MidiProperties.RoleDevicePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return client?.GetField(context) is { Status: SourceStatus.Value } provided
                    ? provided
                    : throw new InvalidOperationException($"'{context.Path}': there is no value for the device value '{role}'.");
            }
            return client?.GetField(context) ?? SourceResult.NoValue();
        }

        var type = context.Field.DataType.ClrType;
        return SourceResult.Provided(new LogicalField(context.Name, type, Convert.ChangeType(value.Value, type, CultureInfo.InvariantCulture)));
    }

    public OpenChoiceSourceResult EnterOpenChoice(OpenChoiceContext context)
    {
        if (!MidiProperties.IsData(context.Choice.Group))
        {
            // another open choice of the message: the client selects its member
            return client is null
                ? throw new InvalidOperationException($"'{context.Path}': the open choice is not the data of the message (midi.role=\"data\"), and there is no client to select its member.")
                : ValueModels.EnterOpenChoice(client, context, Scope);
        }
        if (body is null)
        {
            throw new InvalidOperationException($"'{context.Path}': the message has no body to write.");
        }
        var clientBody = client is IOpenChoiceSource openChoice ? openChoice.EnterOpenChoice(context).Scope : null;
        return body(clientBody).ToSelection();
    }

    public IValueSource EnterGroup(GroupContext context) => Scope(client?.EnterGroup(context));
    public void ExitGroup(GroupContext context) => client?.ExitGroup(context);

    public IValueSource EnterRepeat(RepeatContext context, int count) => Scope(client?.EnterRepeat(context, count));

    public OpenRepeatSourceResult EnterOpenRepeat(RepeatContext context)
        => client is null
            ? throw new NotSupportedException($"'{context.Path}': repeats without a count are not supported in an envelope message without a client.")
            : ValueModels.EnterOpenRepeat(client, context, Scope);

    public OpenRepeatAction NextItem(RepeatContext context, int index)
        => client is null ? OpenRepeatAction.Default : ValueModels.NextItem(client, context, index);

    public void ExitRepeat(RepeatContext context, int count) => client?.ExitRepeat(context, count);
    public IValueSource EnterItem(RepeatContext context, int index, int count) => Scope(client?.EnterItem(context, index, count));
    public void ExitItem(RepeatContext context, int index, int count) => client?.ExitItem(context, index, count);
    public IValueSource EnterChoice(ChoiceContext context, int selectedIndex) => Scope(client?.EnterChoice(context, selectedIndex));
    public void ExitChoice(ChoiceContext context, int selectedIndex) => client?.ExitChoice(context, selectedIndex);

    private EnvelopeSource Scope(IValueSource? clientScope)
        => client is null ? this : new EnvelopeSource(blockRoles, device, body, clientScope);
}

/// <summary>
/// The value sink for reading an envelope message: keeps the values of the fields with a <c>midi.role</c> (the
/// values of the block, e.g. the address of an address map, and the device values) and lets a selector choose the
/// body for the data open choice, based on them.
/// </summary>
internal sealed class EnvelopeSink(Func<EnvelopeSink, EnvelopeBody<IValueSink>> selectBody) : IOpenChoiceSink
{
    private readonly Dictionary<string, long> _blockRoles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The values read from the fields with a role other than <c>device.&lt;name&gt;</c> (e.g. <c>address</c>), by role.</summary>
    public IReadOnlyDictionary<string, long> BlockRoles => _blockRoles;

    /// <summary>The device values read from the <c>device.&lt;name&gt;</c> fields.</summary>
    public DeviceValues Device { get; } = new();

    public void SetField(FieldContext context, LogicalField value)
    {
        if (MidiProperties.Find(context.Field, MidiProperties.Role) is not { } role || value.Value is null)
        {
            return;
        }

        var number = Convert.ToInt64(value.Value, CultureInfo.InvariantCulture);
        if (role.StartsWith(MidiProperties.RoleDevicePrefix, StringComparison.OrdinalIgnoreCase))
        {
            Device[role[MidiProperties.RoleDevicePrefix.Length..]] = number;
        }
        else
        {
            _blockRoles[role] = number;
        }
    }

    public OpenChoiceSinkResult EnterOpenChoice(OpenChoiceContext context)
        => MidiProperties.IsData(context.Choice.Group)
            ? selectBody(this).ToSelection()
            : throw new InvalidOperationException($"'{context.Path}': only the data open choice (midi.role=\"data\") is supported in an envelope message.");

    public IValueSink EnterGroup(GroupContext context) => this;
    public IValueSink EnterRepeat(RepeatContext context, int count) => this;
    public void ExitRepeat(RepeatContext context, int count) { }
    public IValueSink EnterItem(RepeatContext context, int index, int count) => this;
    public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => this;
    public void ExitGroup(GroupContext context) { }
    public void ExitItem(RepeatContext context, int index, int count) { }
    public void ExitChoice(ChoiceContext context, int selectedIndex) { }
}

/// <summary>
/// A value sink that discards all values: used for trial reads.
/// </summary>
internal sealed class NullSink : IValueSink
{
    public static readonly NullSink Instance = new();

    public void SetField(FieldContext context, LogicalField value) { }
    public IValueSink EnterGroup(GroupContext context) => this;
    public IValueSink EnterRepeat(RepeatContext context, int count) => this;
    public void ExitRepeat(RepeatContext context, int count) { }
    public IValueSink EnterItem(RepeatContext context, int index, int count) => this;
    public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => this;
    public void ExitGroup(GroupContext context) { }
    public void ExitItem(RepeatContext context, int index, int count) { }
    public void ExitChoice(ChoiceContext context, int selectedIndex) { }
}
