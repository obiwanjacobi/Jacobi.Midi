using System.Globalization;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;

namespace Jacobi.Midi.Device.Addressing;

/// <summary>
/// The body of a data message: the selected group (the device map), the group occurrence inside it that is written
/// or read, and the value model scope for its values.
/// </summary>
internal readonly record struct EnvelopeBody<TScope>(SchemaName Group, SchemaPath Target, InstancePath TargetInstance, TScope Scope);

/// <summary>
/// The value source for writing an envelope message: answers the fields by their <c>midi.role</c>
/// (address start and size of the transfer, device values), leaves the rest to the schema
/// (constants, derived sizes, checksums) and selects the body for the data open choice.
/// </summary>
internal sealed class EnvelopeSource(AddressRange addresses, DeviceValues device, EnvelopeBody<IValueSource>? body = null)
    : IOpenChoiceSource
{
    public SourceResult GetField(FieldContext context)
    {
        if (MidiProperties.Find(context.Field, MidiProperties.Role) is not { } role)
        {
            return SourceResult.NoValue();
        }

        long? value = role switch
        {
            MidiProperties.RoleAddress => addresses.Start,
            MidiProperties.RoleSize => addresses.Size,
            _ when role.StartsWith(MidiProperties.RoleDevicePrefix, StringComparison.OrdinalIgnoreCase) =>
                device.TryGet(role[MidiProperties.RoleDevicePrefix.Length..], out var deviceValue)
                    ? deviceValue
                    : throw new InvalidOperationException($"'{context.Path}': there is no value for the device value '{role}'."),
            _ => null,
        };
        if (value is null)
        {
            return SourceResult.NoValue();
        }

        var type = context.Field.DataType.ClrType;
        return SourceResult.Provided(new LogicalField(context.Name, type, Convert.ChangeType(value.Value, type, CultureInfo.InvariantCulture)));
    }

    public OpenChoiceSourceResult EnterOpenChoice(OpenChoiceContext context)
    {
        if (body is not { } selected)
        {
            throw new InvalidOperationException($"'{context.Path}': the message has no body to write.");
        }
        return new OpenChoiceSourceResult(selected.Scope, selected.Group, selected.Target, selected.TargetInstance);
    }

    public IValueSource EnterGroup(GroupContext context) => this;

    public int GetCount(RepeatContext context)
        => throw new NotSupportedException($"'{context.Path}': repeats are not supported in an envelope message.");

    public IValueSource EnterItem(RepeatContext context, int index) => this;

    public int GetSelectedIndex(ChoiceContext context)
        => throw new NotSupportedException($"'{context.Path}': choices are not supported in an envelope message.");

    public IValueSource EnterChoice(ChoiceContext context) => this;
}

/// <summary>
/// The value sink for reading an envelope message: keeps the values of the fields with a <c>midi.role</c>
/// (address start, device values) and lets a selector choose the body for the data open choice, based on them.
/// </summary>
internal sealed class EnvelopeSink(Func<EnvelopeSink, EnvelopeBody<IValueSink>> selectBody) : IOpenChoiceSink
{
    /// <summary>The address read from the <c>address</c> field; null before it was read.</summary>
    public long? Address { get; private set; }

    /// <summary>The device values read from the <c>device.&lt;name&gt;</c> fields.</summary>
    public DeviceValues Device { get; } = new();

    public void SetField(FieldContext context, LogicalField value)
    {
        if (MidiProperties.Find(context.Field, MidiProperties.Role) is not { } role || value.Value is null)
        {
            return;
        }

        if (String.Equals(role, MidiProperties.RoleAddress, StringComparison.OrdinalIgnoreCase))
        {
            Address = Convert.ToInt64(value.Value, CultureInfo.InvariantCulture);
        }
        else if (role.StartsWith(MidiProperties.RoleDevicePrefix, StringComparison.OrdinalIgnoreCase))
        {
            Device[role[MidiProperties.RoleDevicePrefix.Length..]] = Convert.ToInt64(value.Value, CultureInfo.InvariantCulture);
        }
    }

    public OpenChoiceSinkResult EnterOpenChoice(OpenChoiceContext context)
    {
        var body = selectBody(this);
        return new OpenChoiceSinkResult(body.Scope, body.Group, body.Target, body.TargetInstance);
    }

    public IValueSink EnterGroup(GroupContext context) => this;
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
    public IValueSink EnterItem(RepeatContext context, int index, int count) => this;
    public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => this;
    public void ExitGroup(GroupContext context) { }
    public void ExitItem(RepeatContext context, int index, int count) { }
    public void ExitChoice(ChoiceContext context, int selectedIndex) { }
}
