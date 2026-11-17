using System.Diagnostics;
using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Device.Exchange;
using Jacobi.Midi.Device.Messages;
using Xunit;
using static Jacobi.Midi.Tests.Addressing.MessageExchangeTests;
using static Jacobi.Midi.Tests.Addressing.TestDevice;

namespace Jacobi.Midi.Tests.Addressing;

/// <summary>
/// The timing of the message exchange on the stand-alone <see cref="TestDevice"/> schemas: these tests measure time and
/// run alone (<see cref="TimingCollection"/>).
/// </summary>
[Collection(TimingCollection.Name)]
public class MessageExchangeTimingTests
{
    [Fact]
    public async Task Send_KeepsTheMessageIntervalBetweenMessages()
    {
        var serializer = CreateSerializer();
        var device = new FakeDevice();
        var exchanges = new ExchangeDefinitions();
        exchanges.Add(ExchangeDefinitionReader.ReadXml(ExchangeXml.Replace("messageInterval=\"20\"", "messageInterval=\"60\""), "test"));
        await using var exchange = CreateExchange(serializer, device, exchanges: exchanges);
        var values = new FieldValueStore();
        var range = serializer.GetPlan(MapName)
            .CreateRange("AddressMap.Memory.Patch", new InstancePath([0]), "AddressMap.Memory.Patch", new InstancePath([2]));
        Assert.True((await RequestData(exchange, range, values)).IsComplete);
        var requests = device.Sent.Count;

        var result = await SendData(exchange, range, values);

        Assert.True(result.IsComplete);
        Assert.Equal(3, device.DataSets.Count);
        // 60 ms after the last request, and between the three data messages: measured where the device receives them
        var times = device.Sent.Skip(requests - 1).Select(sent => sent.Timestamp).ToList();
        Assert.Equal(4, times.Count);
        for (var i = 1; i < times.Count; i++)
        {
            var gap = Stopwatch.GetElapsedTime(times[i - 1], times[i]).TotalMilliseconds;
            Assert.True(gap >= 60 - 5, $"message {i}: {gap:F1} ms after the one before");
        }
    }
}
