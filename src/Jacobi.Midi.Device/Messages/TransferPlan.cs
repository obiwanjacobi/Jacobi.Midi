using Jacobi.BinarySerializer.Execution;

namespace Jacobi.Midi.Device.Messages;

/// <summary>
/// How ranges of a root are transferred (see <see cref="MessageMaps.Plan"/>): the data message and the blocks it
/// carries, one message per block, in the order the map transfers them.
/// </summary>
public sealed record TransferPlan(DeviceMessage Message, IReadOnlyList<PlannedBlock> Blocks);

/// <summary>
/// One block of a <see cref="TransferPlan"/>: the nodes one message carries and a short description of them for a
/// person, given by the map (e.g. the address and size of an address map block, or the whole root of a dump).
/// </summary>
public sealed record PlannedBlock(PlanRange Range, string Description);
