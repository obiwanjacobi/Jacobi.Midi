using Jacobi.BinarySerializer.Execution;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// Operations on a <see cref="PlanRange"/> that follow the <c>midi.*</c> tags of its plan, whatever moves the data (an
/// address map, Property Exchange).
/// </summary>
internal static class PlanRangeExtensions
{
    /// <summary>
    /// Widens a range to the atomic groups and repeats it overlaps (<c>midi.transfer="atomic"</c>), so it never begins or
    /// ends inside one. Only the bounds can be inside one: an atomic node between them is covered already.
    /// </summary>
    public static PlanRange WidenToAtomic(this PlanRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        var begin = OutermostAtomic(range.First, range.BeginInstance);
        var end = OutermostAtomic(range.Last, range.EndInstance);
        if (begin is null && end is null)
        {
            return range;
        }
        return range.ExecutionPlan.CreateRange(
            ((NodeInfo?)begin?.Node ?? range.First).Path, begin?.Instance ?? range.BeginInstance,
            ((NodeInfo?)end?.Node ?? range.Last).Path, end?.Instance ?? range.EndInstance);
    }

    // the outermost atomic node around a field, with the item indices of the repeats above it; null when there is none
    private static (GroupInfo Node, InstancePath Instance)? OutermostAtomic(FieldInfo field, InstancePath instance)
    {
        GroupInfo? atomic = null;
        for (var node = field.Parent; node is not null; node = node.Parent)
        {
            if (MidiProperties.TransferOf(node, out _) == NodeTransfer.Atomic)
            {
                atomic = node;
            }
        }
        if (atomic is null)
        {
            return null;
        }

        var repeats = 0;
        for (var node = atomic.Parent; node is not null; node = node.Parent)
        {
            if (node is RepeatInfo)
            {
                repeats++;
            }
        }
        return (atomic, instance.Length <= repeats ? instance : new InstancePath(instance.AsSpan()[..repeats].ToArray()));
    }
}
