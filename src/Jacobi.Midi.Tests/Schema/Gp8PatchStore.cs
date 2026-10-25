using Jacobi.BinarySerializer;
using Jacobi.BinarySerializer.Execution;
using Jacobi.BinarySerializer.Processor;
using Jacobi.Midi.Device.Addressing;

namespace Jacobi.Midi.Tests.Schema;

/// <summary>
/// A flat value model for one GP-8 DT1 message: keeps the field values read by path and instance and provides
/// them again for writing. The data open choice selects the memory patch of the GP-8 address map at the address read;
/// the map ends its repeats after that patch.
/// </summary>
internal sealed class Gp8PatchStore(AddressMap map) : IOpenChoiceSink, IOpenChoiceSource
{
    private const int MemoryStart = 0x2000;   // wire 40 00
    private const int Stride = 0x40;          // wire 00 40

    private readonly Dictionary<(string Path, InstancePath Instance), LogicalField> _values = [];

    /// <summary>The index of the memory patch, from the address of the message; -1 before it was read.</summary>
    public int PatchIndex { get; private set; } = -1;

    /// <summary>The value of a field of the memory patch that was read, by its path in the patch (e.g. 'DynamicFilter.Sens').</summary>
    public LogicalField GetPatchField(string path)
        => _values[($"{PatchPath}.{path}", PatchInstance)];

    /// <summary>Replaces the value of a field of the memory patch that is provided for writing.</summary>
    public void SetPatchField(string path, LogicalField value)
        => _values[($"{PatchPath}.{path}", PatchInstance)] = value;

    public void SetField(FieldContext context, LogicalField value)
    {
        _values[(context.Path, context.Instance)] = value;
        if (String.Equals(context.Path, "Message16.DT1.ChecksumScope.Address", StringComparison.OrdinalIgnoreCase))
        {
            PatchIndex = (Convert.ToInt32(value.Value) - MemoryStart) / Stride;
        }
    }

    public SourceResult GetField(FieldContext context)
        => _values.TryGetValue((context.Path, context.Instance), out var value)
            ? SourceResult.Provided(value)
            : SourceResult.NoValue();

    public OpenChoiceSinkResult EnterOpenChoice(OpenChoiceContext context)
        => new(map.WrapBody((IValueSink)this, PatchPath, PatchInstance), map.Root, PatchPath, PatchInstance);

    OpenChoiceSourceResult IOpenChoiceSource.EnterOpenChoice(OpenChoiceContext context)
        => new(map.WrapBody((IValueSource)this, PatchPath, PatchInstance), map.Root, PatchPath, PatchInstance);

    private const string PatchPath = "AddressMap.Memory.Patch";
    private InstancePath PatchInstance => new([PatchIndex]);

    // the store is flat: every scope is the store itself
    public IValueSink EnterGroup(GroupContext context) => this;
    public IValueSink EnterItem(RepeatContext context, int index, int count) => this;
    public IValueSink EnterChoice(ChoiceContext context, int selectedIndex) => this;
    public void ExitGroup(GroupContext context) { }
    public void ExitItem(RepeatContext context, int index, int count) { }
    public void ExitChoice(ChoiceContext context, int selectedIndex) { }

    IValueSource IValueSource.EnterGroup(GroupContext context) => this;
    public int GetCount(RepeatContext context) => -1;
    public IValueSource EnterItem(RepeatContext context, int index) => this;
    public int GetSelectedIndex(ChoiceContext context)
        => throw new NotSupportedException($"'{context.Path}': choices are not supported.");
    IValueSource IValueSource.EnterChoice(ChoiceContext context) => this;
}
