using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Device.Messages;
using Jacobi.Midi.Tests.Addressing;
using Xunit;

namespace Jacobi.Midi.Tests.Messages;

/// <summary>
/// Tests <see cref="MessageMaps.Plan"/> with the test device's address map: the blocks of several ranges, widened to
/// the atomic patch, planned once and in address order, each described by the map.
/// </summary>
public class TransferPlanTests
{
    [Fact]
    public void Plan_RangesInOneAtomicGroup_AreOneBlock_InAddressOrder()
    {
        var serializer = TestDevice.CreateSerializer();
        var plan = serializer.GetPlan(TestDevice.MapName);
        var item = InstancePath.Empty.Append(3);
        var warnings = new List<string>();

        // two fields of patch 3 (atomic), then the global volume (lower address)
        var transfer = new MessageMaps(serializer).Plan(
        [
            plan.CreateRange("AddressMap.Memory.Patch.Filter.Cutoff", item, "AddressMap.Memory.Patch.Filter.Cutoff", item),
            plan.CreateRange("AddressMap.Memory.Patch.Level", item, "AddressMap.Memory.Patch.Level", item),
            plan.CreateRange("AddressMap.Global.Volume", "AddressMap.Global.Volume"),
        ], new DeviceValues { ["deviceId"] = 0 }, warnings: warnings);

        Assert.Equal(2, transfer.Blocks.Count);
        Assert.Equal("AddressMap.Global.Volume", transfer.Blocks[0].Range.First.Path.Value);
        Assert.Contains("0x01-60", transfer.Blocks[1].Description, StringComparison.Ordinal);
        // each field is widened to the whole patch
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, warning => Assert.Contains("is part of a larger block", warning, StringComparison.Ordinal));
    }
}
