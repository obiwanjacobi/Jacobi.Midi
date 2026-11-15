using Jacobi.Midi.Device.Messages;

namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// The device values a device of a schema needs, for the device panel: the names its roots with a message map need
/// from the device instance (e.g. <c>deviceId</c>), and the values its roots fix (which the device can override).
/// </summary>
/// <param name="Required">The names of the device values the device supplies.</param>
/// <param name="Fixed">The device values the roots fix (<c>midi.device.*</c>).</param>
internal sealed record DeviceValueNeeds(IReadOnlyList<string> Required, DeviceValues Fixed);
