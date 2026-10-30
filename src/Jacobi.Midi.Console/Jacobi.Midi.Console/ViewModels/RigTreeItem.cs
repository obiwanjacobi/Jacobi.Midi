using System.Globalization;
using Avalonia.Media;
using Jacobi.Midi.Console.Core.Connections;
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
/// (<see cref="Source"/>): a device or group becomes a section, a field a control. A device shows its MIDI
/// connection in MIDI mode (<see cref="Connection"/>).
/// </summary>
internal sealed class RigTreeItem : ViewModelBase
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

    /// <summary>The MIDI connection of the device in MIDI mode; null offline, and for a node.</summary>
    public DeviceConnection? Connection
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasConnection));
                OnPropertyChanged(nameof(ConnectionBrush));
                OnPropertyChanged(nameof(ConnectionTip));
            }
        }
    }

    /// <summary>Whether a control of the selected dashboard is bound to the field, or to a field inside the item.</summary>
    public ParameterUsage Usage
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsUsed));
                OnPropertyChanged(nameof(UsageOpacity));
                OnPropertyChanged(nameof(UsageTip));
            }
        }
    }

    public bool IsUsed => Usage != ParameterUsage.None;

    // a field that is used has the full mark, an item with used fields inside it a fainter one
    public double UsageOpacity => Usage == ParameterUsage.Used ? 1 : 0.4;

    public string? UsageTip => Usage switch
    {
        ParameterUsage.Used => "A control of the dashboard is bound to it",
        ParameterUsage.Inside => "Controls of the dashboard are bound to parameters in it",
        _ => null,
    };

    /// <summary>
    /// Marks the item and those inside it with the paths of the device's parameters the dashboard's controls are bound to
    /// (<see cref="DashboardSession.UsedPaths"/>); none without a dashboard. True when a field in or of it is used.
    /// </summary>
    public bool MarkUsage(IReadOnlySet<string> usedPaths)
    {
        ArgumentNullException.ThrowIfNull(usedPaths);
        // every child is marked, also after the first used one
        var inside = Children.Aggregate(false, (any, child) => child.MarkUsage(usedPaths) | any);
        Usage = Node is { Kind: ParameterNodeKind.Field } node && usedPaths.Contains(node.Path) ? ParameterUsage.Used
            : inside ? ParameterUsage.Inside
            : ParameterUsage.None;
        return Usage != ParameterUsage.None;
    }

    public bool HasConnection => Connection is not null;

    public IBrush ConnectionBrush => MidiViewModel.BrushOf(Connection);

    public string? ConnectionTip => Connection?.Description;

    /// <summary>The item of a device, with its parameters when it has a state (its schemas compile).</summary>
    public static RigTreeItem Of(DeviceInstance device, DeviceState? state, DeviceConnection? connection)
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
            Connection = connection,
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
/// Whether the controls of the selected dashboard use an item of the rig tree: a field they are bound to, or a device,
/// group or repeat with such fields inside it.
/// </summary>
internal enum ParameterUsage
{
    None,
    Inside,
    Used,
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
