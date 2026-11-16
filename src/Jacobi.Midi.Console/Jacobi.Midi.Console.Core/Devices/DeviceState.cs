using System.Diagnostics.CodeAnalysis;
using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.BinarySerializer.Schema;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Console.Core.Schemas;
using Jacobi.Midi.Device.Addressing;
using Jacobi.Midi.Messages;
using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Console.Core.Devices;

/// <summary>
/// The console's value model of one device instance: the last known value of each parameter, by the path and
/// instance in the plan of its root (the identity of a <see cref="ParameterReference"/>). The single place of truth
/// for the controls: they read and write it, and it is synchronised with the device (the session receives into
/// <see cref="CreateSink"/> and sends from <see cref="Source"/>) or loaded from and saved to a dump offline.
/// A parameter without a value is unknown. Thread-safe; <see cref="ValueChanged"/> is raised on the thread that
/// changed the value, after the change. The device is a schema document (<see cref="Schema"/>): all its roots
/// (<see cref="Roots"/>) belong to it, whatever they are (a root with a message map: SysEx data; a command root:
/// channel messages; a Property Exchange root: MIDI-CI; the Device project decides). A path starts with the name of
/// its root, so the paths of all roots are apart; <see cref="PlanOf"/> gives the plan of a path.
/// </summary>
public sealed class DeviceState
{
    private readonly Lock _lock = new();
    private readonly FieldValueStore _values = new();
    private readonly MessageMaps _maps;
    private readonly SchemaLibrary _library;
    // the plans of the roots, by root name
    private readonly Dictionary<string, ExecutionPlan> _plans = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="device">The device instance of the rig: its alias, schema and device values.</param>
    /// <param name="library">The compiled schemas that hold the device's schema.</param>
    /// <exception cref="ArgumentException">The library does not compile, or does not have the device's schema.</exception>
    public DeviceState(DeviceInstance device, SchemaLibrary library)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(library);
        if (library.Serializer is null || library.MessageMaps is null)
            throw new ArgumentException("The schemas do not compile; see the problems of the library.", nameof(library));
        if (!library.HasSchema(device.Schema))
            throw new ArgumentException($"'{device.Schema}' of device '{device.Alias}' is not a schema with root groups.", nameof(device));

        Device = device;
        Schema = device.Schema;
        DeviceValues = new DeviceValues();
        foreach (var (name, value) in device.DeviceValues)
            DeviceValues[name] = value;
        Roots = library.RootsOf(device.Schema);
        foreach (var root in Roots)
        {
            try
            {
                _plans[root.Name] = library.Serializer.GetPlan(root);
            }
            catch (ExecutionPlanException exception)
            {
                throw new ArgumentException($"The root '{root}' of device '{device.Alias}' has no plan: {String.Join(" ", exception.Errors)}", nameof(device), exception);
            }
        }
        Source = new StateSource(this);
        _maps = library.MessageMaps;
        _library = library;
    }

    /// <summary>The device instance of the rig the state is of; it keeps its state when it gets another alias.</summary>
    public DeviceInstance Device { get; }

    /// <summary>The alias of the device: follows the device when it gets another one.</summary>
    public string Alias => Device.Alias;

    /// <summary>The schema document of the device: its roots are the device's.</summary>
    public string Schema { get; }

    /// <summary>The device values the roots do not fix (e.g. <c>deviceId</c>).</summary>
    public DeviceValues DeviceValues { get; }

    /// <summary>The roots of the device's schema, in their order (see <see cref="SchemaLibrary.RootsOf"/>).</summary>
    public IReadOnlyList<SchemaName> Roots { get; }

    /// <summary>The plan of the root a path starts with; null for a path of no root of the device.</summary>
    public ExecutionPlan? PlanOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var separator = path.IndexOf(SchemaPath.Separator);
        return _plans.GetValueOrDefault(separator < 0 ? path : path[..separator]);
    }

    /// <summary>
    /// True when the parameter's root has a message map (SysEx data), so the exchanges a dashboard names apply to it;
    /// false for a command or Property Exchange root, which has only the standard exchanges, and for a path of no root.
    /// </summary>
    public bool UsesExchanges(string path)
        => PlanOf(path) is { } plan && MidiProperties.CanHaveMessageMap(plan.Root.Group);

    /// <summary>The node of a path (without repeat indices) of the device's roots; null when there is none.</summary>
    public NodeInfo? Find(string path) => PlanOf(path)?.Find(path);

    /// <summary>The range of one parameter (a field, or a group) at an instance, in the plan of its root.</summary>
    /// <exception cref="ArgumentException">The path is not of the device's roots.</exception>
    public PlanRange CreateRange(string path, InstancePath instance)
        => (PlanOf(path) ?? throw new ArgumentException($"'{path}' is not a path of the roots of device '{Alias}'.", nameof(path)))
            .CreateRange(path, instance, path, instance);

    /// <summary>The number of parameters with a known value.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _values.Count;
        }
    }

    /// <summary>
    /// The parameter trees of the roots, in the order of <see cref="Roots"/>: the groups, repeats and fields to bind
    /// controls to. A root without parameters (e.g. a message whose values the schema fixes) has none.
    /// </summary>
    public IReadOnlyList<ParameterNode> Parameters
        => _parameters ??= [.. Roots.Select(root => ParameterNode.Of(_plans[root.Name], repeat => ItemCount(root, repeat))).Where(node => node.Children.Count > 0)];
    private IReadOnlyList<ParameterNode>? _parameters;

    // the item count of a repeat of a root; an invalid map has none (it is reported when the bindings are resolved)
    private int? ItemCount(SchemaName root, string repeat)
    {
        try
        {
            return _library.ItemCount(root, repeat);
        }
        catch (MessageMapException)
        {
            return null;
        }
    }

    /// <summary>The values as a value source, e.g. to send a range or to save a dump: a parameter without a value has none.</summary>
    public IValueSource Source { get; }

    /// <summary>A parameter got another value.</summary>
    public event EventHandler<ValueChangedEventArgs>? ValueChanged;

    public bool TryGetValue(string path, InstancePath instance, [NotNullWhen(true)] out LogicalField? value)
    {
        lock (_lock)
        {
            if (_values.TryGet(path, instance, out var known))
            {
                value = known;
                return true;
            }
        }
        value = null;
        return false;
    }

    /// <summary>The value of a parameter of this device; false when it is unknown.</summary>
    /// <exception cref="ArgumentException">The parameter is of another device.</exception>
    public bool TryGetValue(ParameterReference parameter, [NotNullWhen(true)] out LogicalField? value)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        if (!String.Equals(parameter.Device, Alias, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{parameter.Device}' is not the device '{Alias}'.", nameof(parameter));
        return TryGetValue(parameter.Path, parameter.Instance, out value);
    }

    /// <summary>Sets a value; <see cref="ValueChanged"/> is raised when it differs from the known value.</summary>
    public void SetValue(string path, InstancePath instance, LogicalField value, ValueOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(value);
        lock (_lock)
        {
            if (_values.TryGet(path, instance, out var known) && known == value)
                return;
            _values.Set(path, instance, value);
        }
        ValueChanged?.Invoke(this, new ValueChangedEventArgs(path, instance, value, origin));
    }

    /// <summary>A value sink that sets the values it gets, with their origin (e.g. for the message exchange).</summary>
    public IValueSink CreateSink(ValueOrigin origin) => new StateSink(this, origin);

    /// <summary>
    /// Loads the values of a dump (e.g. the contents of a .syx file): the data messages of the device's roots with a
    /// message map, for this device, are decoded; other messages are skipped and reported.
    /// </summary>
    /// <exception cref="InvalidOperationException">A device value the messages need is not known.</exception>
    /// <exception cref="MessageMapException">A map is not valid.</exception>
    public DumpLoadResult LoadDump(byte[] dump)
    {
        ArgumentNullException.ThrowIfNull(dump);
        var messages = SysExSplitter.Split(dump);
        // only a root with a message map has data messages (the Device project decides); another root takes none of them
        var mapped = Roots.Where(root => _library.KindOf(root) is not null).ToList();
        if (mapped.Count == 0)
            return new DumpLoadResult(messages.Count, 0, [$"'{Schema}' has no root with a message map: no data message carries its values."]);
        var decoded = 0;
        List<string>? warnings = null;
        foreach (var root in mapped)
        {
            var rootWarnings = new List<string>();
            decoded += _maps.Load(root, messages.Select(message => (ReadOnlyMemory<byte>)message), DeviceValues,
                CreateSink(ValueOrigin.Dump), rootWarnings);
            // a message one root skips can be one another root decodes: only what every root skips is reported
            warnings = warnings is null ? rootWarnings : [.. warnings.Intersect(rootWarnings)];
        }
        return new DumpLoadResult(messages.Count, decoded, warnings ?? []);
    }

    /// <summary>
    /// Saves a range of the map as a dump: the data messages that transfer it (as the device would get them), one
    /// after the other. The range may be widened (the atomic groups it overlaps, the whole root of a dump); all their values must be known.
    /// </summary>
    /// <exception cref="InvalidOperationException">A value of the range, or a device value the messages need, is not known.</exception>
    /// <exception cref="MessageMapException">The map is not valid.</exception>
    public byte[] SaveDump(PlanRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (range.ExecutionPlan.Name is not { } root || !_plans.TryGetValue(root.Name, out var plan) || !ReferenceEquals(range.ExecutionPlan, plan))
            throw new ArgumentException($"The range is not of a root of '{Schema}'.", nameof(range));
        if (_library.KindOf(root) is null)
            throw new InvalidOperationException($"'{root}' has no message map: no data message carries its values.");
        return [.. _maps.Save([range], Source, DeviceValues).SelectMany(message => message)];
    }

    // the exchange and the dump write through this sink: every value with its origin
    private sealed class StateSink(DeviceState state, ValueOrigin origin) : IValueSink
    {
        public void SetField(FieldContext context, LogicalField value)
            => state.SetValue(context.Path, context.Instance, value, origin);

        // the state is flat: every scope is the sink itself
        public IValueSink EnterGroup(GroupContext context) => this;
        public IValueSink EnterRepeat(RepeatContext context, int count) => this;
        public IValueSink EnterItem(RepeatContext context, int index, int count) => this;
        public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => this;
        public void ExitGroup(GroupContext context) { }
        public void ExitRepeat(RepeatContext context, int count) { }
        public void ExitItem(RepeatContext context, int index, int count) { }
        public void ExitChoice(ChoiceContext context, int selectedIndex) { }
    }

    // reads the store under the lock
    private sealed class StateSource(DeviceState state) : IOpenRepeatSource
    {
        public SourceResult GetField(FieldContext context)
        {
            lock (state._lock)
                return state._values.GetField(context);
        }

        // the store is flat: every scope is the source itself; a repeat without a count is written item by item with
        // the engine's own rule (the store holds no number of items)
        public IValueSource EnterGroup(GroupContext context) => this;
        public void ExitGroup(GroupContext context) { }
        public IValueSource EnterRepeat(RepeatContext context, int count) => this;
        public OpenRepeatSourceResult EnterOpenRepeat(RepeatContext context) => OpenRepeatSourceResult.ItemByItem(this);
        public OpenRepeatAction NextItem(RepeatContext context, int index) => OpenRepeatAction.Default;
        public void ExitRepeat(RepeatContext context, int count) { }
        public IValueSource EnterItem(RepeatContext context, int index, int count) => this;
        public void ExitItem(RepeatContext context, int index, int count) { }
        public IValueSource EnterChoice(ChoiceContext context, int selectedIndex) => this;
        public void ExitChoice(ChoiceContext context, int selectedIndex) { }
    }
}

/// <summary>Where a value of a <see cref="DeviceState"/> came from.</summary>
public enum ValueOrigin
{
    /// <summary>Set by the user with a control.</summary>
    User,
    /// <summary>Received from the device.</summary>
    Device,
    /// <summary>Loaded from a dump.</summary>
    Dump,
}

/// <summary>A parameter of a <see cref="DeviceState"/> got another value.</summary>
public sealed class ValueChangedEventArgs(string path, InstancePath instance, LogicalField value, ValueOrigin origin) : EventArgs
{
    /// <summary>The path in the map plan.</summary>
    public string Path => path;
    public InstancePath Instance => instance;
    public LogicalField Value => value;
    public ValueOrigin Origin => origin;
}

/// <summary>The outcome of <see cref="DeviceState.LoadDump"/>: the messages in the dump, those decoded and why the others were skipped.</summary>
public sealed record DumpLoadResult(int Messages, int Decoded, IReadOnlyList<string> Warnings);
