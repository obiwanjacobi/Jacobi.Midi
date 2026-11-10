using Jacobi.BinarySerializer;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// The outcome of matching a message of the device with a message of a map (<see cref="IMessageMap.Match"/>): it is
/// recognised (with the data it carries, for a data message), it is not this message (other traffic: silent), or it is
/// a near miss: the envelope and the device match, but the message cannot be read (<see cref="Problem"/> says why).
/// </summary>
internal readonly record struct MessageMatch
{
    private MessageMatch(bool isRecognised, ReceivedData? data, string? problem)
    {
        IsRecognised = isRecognised;
        Data = data;
        Problem = problem;
    }

    /// <summary>Not this message, or not for this device: other traffic.</summary>
    public static MessageMatch NotThis => default;

    /// <summary>Recognised; <paramref name="data"/> is the data of a data message, null for any other message.</summary>
    public static MessageMatch Recognised(ReceivedData? data) => new(true, data, null);

    /// <summary>A near miss: the envelope and the device match, but the message cannot be read.</summary>
    public static MessageMatch Rejected(string problem) => new(false, null, problem);

    public bool IsRecognised { get; }

    /// <summary>The data of a recognised data message; null otherwise.</summary>
    public ReceivedData? Data { get; }

    /// <summary>Why a near miss cannot be read; null when it is recognised or not this message.</summary>
    public string? Problem { get; }

    public bool IsRejected => Problem is not null;

    /// <summary>
    /// The outcome of a failed read of a message (one place for every kind of map): a near miss once the message was
    /// accepted as far as its envelope and device go (<paramref name="committed"/>: e.g. its body was selected);
    /// before that, a constant that differs (the engine's <see cref="SerializationCategories.ExpectedValue"/>), a
    /// device value that differs or a rejection by the value model (any other exception) is not this message; any
    /// other failure of the serializer is a near miss.
    /// </summary>
    /// <param name="read">The device values read before the failure.</param>
    public static MessageMatch Failed(Exception exception, DeviceValues read, DeviceValues device, bool committed)
    {
        if (committed)
        {
            return Rejected(exception.Message);
        }
        if (exception is not SerializationException failure
            || failure is { Origin: SerializationException.EngineOrigin, Category: SerializationCategories.ExpectedValue }
            || read.Any(value => device.TryGet(value.Key, out var expected) && expected != value.Value))
        {
            return NotThis;
        }
        return Rejected(failure.Message);
    }
}
