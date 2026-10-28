using System.Globalization;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Devices;
using Jacobi.Midi.Console.Core.Editing;
using Jacobi.Midi.Console.Core.Palettes;
using Jacobi.Midi.Console.Core.Parameters;
using Jacobi.Midi.Console.Core.Rigs;
using Jacobi.Midi.Console.Looks;

namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// An item of the rig tree in Edit mode: a device of the rig (<see cref="Instance"/>) with its parameter tree, or a
/// group, repeat or field of it (<see cref="Node"/>). Every item can be dragged onto a dashboard
/// (<see cref="Source"/>): a device or group becomes a section, a field a control.
/// </summary>
internal sealed class RigTreeItem
{
    private RigTreeItem(string name, string detail)
    {
        Name = name;
        Detail = detail;
    }

    public string Name { get; }

    /// <summary>What it is: the device's name and map root, a repeat's number of items, a field's range or options.</summary>
    public string Detail { get; }

    /// <summary>The device of the rig; null for a node of its parameter tree.</summary>
    public DeviceInstance? Instance { get; private init; }

    /// <summary>The node of the parameter tree (a device's map root); null for a device without state.</summary>
    public ParameterNode? Node { get; private init; }

    /// <summary>What a drag of the item drops; null when the device has no parameter tree (see the problems).</summary>
    public DropSource? Source { get; private init; }

    /// <summary>The device's accent colour; null for a node, or a device with the look's.</summary>
    public IBrush? ColorBrush { get; private init; }

    public IReadOnlyList<RigTreeItem> Children { get; private init; } = [];

    public bool IsDevice => Instance is not null;

    /// <summary>The item of a device, with its parameters when it has a state (its schemas compile).</summary>
    public static RigTreeItem Of(DeviceInstance device, DeviceState? state)
    {
        ArgumentNullException.ThrowIfNull(device);
        var detail = state is null ? $"{device.MapRoot}: not available, see the problems" : device.Name is { } name ? $"{name} · {device.MapRoot}" : device.MapRoot;
        var source = state is null ? null : new DropSource(device.Alias, device.MapRoot, state.Parameters);
        return new RigTreeItem(device.Alias, detail)
        {
            Instance = device,
            Node = state?.Parameters,
            Source = source,
            ColorBrush = RgbColor.TryParse(device.Colors[ColorArea.Accent], out var color) ? new SolidColorBrush(PaletteBrushes.ToColor(color.Value)) : null,
            Children = source is null ? [] : [.. source.Node.Children.Select(child => Of(source, child))],
        };
    }

    private static RigTreeItem Of(DropSource device, ParameterNode node)
        => new(node.Name, DetailOf(node))
        {
            Node = node,
            Source = device with { Node = node },
            Children = [.. node.Children.Select(child => Of(device, child))],
        };

    private static string DetailOf(ParameterNode node)
        => node.Kind switch
        {
            ParameterNodeKind.Repeat => node.ItemCount is { } count ? $"[{count}]" : "[…]",
            ParameterNodeKind.Field => node.Info switch
            {
                { Options: { } options } => $"{options.Count} options",
                { Kind: ParameterKind.Switch } => "on/off",
                { Kind: ParameterKind.Text } => "text",
                { Range: { } range } => $"{range.Minimum.ToString(CultureInfo.InvariantCulture)}…{range.Maximum.ToString(CultureInfo.InvariantCulture)}",
                _ => "number",
            },
            _ => String.Empty,
        };
}

/// <summary>
/// A kind of control in a palette of Edit mode, to drag onto a dashboard as a new, unbound control. A kind without its
/// face yet (<see cref="IsAvailable"/>) still makes a control: it is shown as a placeholder until its face is made.
/// </summary>
internal sealed record PaletteItem(Enum Kind, bool IsAvailable)
{
    public string Name => Kind.ToString();

    public string Tip => IsAvailable ? $"Drag onto the dashboard to add a {Name}" : $"Drag onto the dashboard to add a {Name}: it is shown as a placeholder until its face is made";

    public double Opacity => IsAvailable ? 1 : 0.6;
}
