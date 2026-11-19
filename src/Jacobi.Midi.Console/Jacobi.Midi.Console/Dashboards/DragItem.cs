using Avalonia.Input;
using Jacobi.Midi.Console.Core.Editing;
using Jacobi.Midi.Console.Core.Libraries;

namespace Jacobi.Midi.Console.Dashboards;

/// <summary>
/// What is dragged from the left panel of Edit mode onto a dashboard: a node of a device's parameter tree
/// (<see cref="ParameterDragItem"/>), a new control from a palette (<see cref="ControlDragItem"/>) or a user control
/// of a library (<see cref="UserControlDragItem"/>). It stays in the
/// application (<see cref="Format"/>).
/// </summary>
internal abstract record DragItem
{
    public static DataFormat<DragItem> Format { get; } = DataFormat.CreateInProcessFormat<DragItem>("jacobi-midi-console-item");
}

/// <summary>A group, repeat or field of a device: becomes a bound control or section, or binds a slot.</summary>
internal sealed record ParameterDragItem(DropSource Source) : DragItem;

/// <summary>A new, unbound control of a kind: a <c>ParameterControlKind</c> or a <c>LayoutControlKind</c>.</summary>
internal sealed record ControlDragItem(Enum Kind) : DragItem;

/// <summary>A user control of a library: placed as a copy of its section (see <c>UserControlPlace</c>).</summary>
internal sealed record UserControlDragItem(UserControlDefinition Control) : DragItem;
