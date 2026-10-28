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

namespace Jacobi.Midi.Console.Core.Devices;

/// <summary>
/// The console's value model of one device instance: the last known value of each parameter, by the path and
/// instance of the device's map plan (the identity of a <see cref="ParameterReference"/>). The single place of truth
/// for the controls: they read and write it, and it is synchronised with the device (the exchange receives into
/// <see cref="CreateSink"/> and sends from <see cref="Source"/>) or loaded from and saved to a dump offline.
/// A parameter without a value is unknown. Thread-safe; <see cref="ValueChanged"/> is raised on the thread that
/// changed the value, after the change.
/// </summary>
public sealed class DeviceState
{
    private readonly Lock _lock = new();
    private readonly FieldValueStore _values = new();
    private readonly AddressMapAdapter _maps;

    /// <param name="device">The device instance of the rig: its alias, map root and device values.</param>
    /// <param name="library">The compiled schemas that hold the device's map.</param>
    /// <exception cref="ArgumentException">The library does not compile, or the map root is not an address map in it.</exception>
    public DeviceState(DeviceInstance device, SchemaLibrary library)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(library);
        if (library.Serializer is null || library.Maps is null)
            throw new ArgumentException("The schemas do not compile; see the problems of the library.", nameof(library));

        MapRoot = new SchemaName(device.MapRoot);
        if (!library.Maps.IsMap(MapRoot))
            throw new ArgumentException($"'{device.MapRoot}' of device '{device.Alias}' is not an address map in the schemas.", nameof(device));

        Alias = device.Alias;
        DeviceValues = new DeviceValues();
        foreach (var (name, value) in device.DeviceValues)
            DeviceValues[name] = value;
        Plan = library.Serializer.GetPlan(MapRoot);
        Source = new StateSource(this);
        _maps = library.Maps;
    }

    public string Alias { get; }

    public SchemaName MapRoot { get; }

    /// <summary>The device values the map root does not fix (e.g. <c>deviceId</c>).</summary>
    public DeviceValues DeviceValues { get; }

    /// <summary>The plan of the map root: its paths are the paths of the values; ranges of it are synchronised.</summary>
    public ExecutionPlan Plan { get; }

    /// <summary>The number of parameters with a known value.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _values.Count;
        }
    }

    /// <summary>The parameter tree of the map root: the groups, repeats and fields to bind controls to.</summary>
    public ParameterNode Parameters => _parameters ??= ParameterNode.Of(Plan, ItemCount);
    private ParameterNode? _parameters;

    // the item count of a repeat of the map; an invalid map has none (it is reported when the bindings are resolved)
    private int? ItemCount(string repeat)
    {
        try
        {
            return _maps.ItemCount(MapRoot, new SchemaPath(repeat));
        }
        catch (AddressMapException)
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
    /// Loads the values of a dump (e.g. the contents of a .syx file): the data messages of the device's map, for this
    /// device, are decoded; other messages are skipped and reported.
    /// </summary>
    /// <exception cref="InvalidOperationException">A device value the messages need is not known.</exception>
    /// <exception cref="AddressMapException">The map is not valid.</exception>
    public DumpLoadResult LoadDump(byte[] dump)
    {
        ArgumentNullException.ThrowIfNull(dump);
        var messages = SysExSplitter.Split(dump);
        var warnings = new List<string>();
        var decoded = _maps.ReadData(MapRoot, messages.Select(message => (ReadOnlyMemory<byte>)message), DeviceValues,
            CreateSink(ValueOrigin.Dump), warnings);
        return new DumpLoadResult(messages.Count, decoded, warnings);
    }

    /// <summary>
    /// Saves a range of the map as a dump: the data messages that transfer it (as the device would get them), one
    /// after the other. The range is widened to the atomic groups it overlaps; all their values must be known.
    /// </summary>
    /// <exception cref="InvalidOperationException">A value of the range, or a device value the messages need, is not known.</exception>
    /// <exception cref="AddressMapException">The map is not valid.</exception>
    public byte[] SaveDump(PlanRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (!ReferenceEquals(range.ExecutionPlan, Plan))
            throw new ArgumentException($"The range is not of the plan of '{MapRoot}'.", nameof(range));
        return [.. _maps.EncodeData(range, Source, DeviceValues).SelectMany(message => message.SysEx)];
    }

    // the exchange and the dump write through this sink: every value with its origin
    private sealed class StateSink(DeviceState state, ValueOrigin origin) : IValueSink
    {
        public void SetField(FieldContext context, LogicalField value)
            => state.SetValue(context.Path, context.Instance, value, origin);

        // the state is flat: every scope is the sink itself
        public IValueSink EnterGroup(GroupContext context) => this;
        public IValueSink EnterItem(RepeatContext context, int index, int count) => this;
        public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => this;
        public void ExitGroup(GroupContext context) { }
        public void ExitItem(RepeatContext context, int index, int count) { }
        public void ExitChoice(ChoiceContext context, int selectedIndex) { }
    }

    // reads the store under the lock
    private sealed class StateSource(DeviceState state) : IValueSource
    {
        public SourceResult GetField(FieldContext context)
        {
            lock (state._lock)
                return state._values.GetField(context);
        }

        public IValueSource EnterGroup(GroupContext context) => this;
        public int GetCount(RepeatContext context) => -1;
        public IValueSource EnterItem(RepeatContext context, int index) => this;
        public int GetSelectedIndex(ChoiceContext context)
            => throw new NotSupportedException($"'{context.Path}': choices are not supported in an address map.");
        public IValueSource EnterChoice(ChoiceContext context) => this;
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
