using Jacobi.BinarySerializer.Execution;
using Jacobi.Midi.Console.Core.Parameters;

namespace Jacobi.Midi.Console.Core.Devices;

/// <summary>
/// A node of the parameter tree of a device: the groups, repeats and fields of its map root, with the names of the
/// schema, as the user sees them in Edit mode to drag onto a dashboard. A field is a parameter (with its
/// <see cref="ParameterInfo"/>), a repeat has the number of its items in the address map. Fields whose value the schema
/// fixes (a constant, a count, a length or a size of another node) are not parameters and are left out, as are groups
/// with nothing left in them.
/// </summary>
public sealed class ParameterNode
{
    private ParameterNode(string name, string path, ParameterNodeKind kind)
    {
        Name = name;
        Path = path;
        Kind = kind;
    }

    public string Name { get; }

    /// <summary>The schema path from the map root, without repeat indices (e.g. <c>AddressMap.Memory.Patch.Level</c>).</summary>
    public string Path { get; }

    public ParameterNodeKind Kind { get; }

    /// <summary>The number of items of a repeat in the address map; null for other nodes, or when it is not known.</summary>
    public int? ItemCount { get; private init; }

    /// <summary>What the schema says about a field; null for a group or repeat.</summary>
    public ParameterInfo? Info { get; private init; }

    /// <summary>The group or repeat this node is in; null for the map root.</summary>
    public ParameterNode? Parent { get; private set; }

    /// <summary>The nodes in a group, or in each item of a repeat.</summary>
    public IReadOnlyList<ParameterNode> Children { get; private init; } = [];

    /// <summary>This node and the nodes around it, from the map root down.</summary>
    public IReadOnlyList<ParameterNode> PathFromRoot
    {
        get
        {
            var nodes = new List<ParameterNode>();
            for (var node = this; node is not null; node = node.Parent)
                nodes.Insert(0, node);
            return nodes;
        }
    }

    /// <summary>The parameter tree of a plan: its root is the map root.</summary>
    /// <param name="itemCount">The number of items of a repeat, by its path; null when it is not known.</param>
    public static ParameterNode Of(ExecutionPlan plan, Func<string, int?> itemCount)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(itemCount);
        return Of(plan.Root, itemCount) ?? new ParameterNode(plan.Root.Name, plan.Root.Path.Value, ParameterNodeKind.Group);
    }

    private static ParameterNode? Of(NodeInfo info, Func<string, int?> itemCount)
    {
        var path = info.Path.Value;
        switch (info)
        {
            case FieldInfo field:
                return IsParameter(field)
                    ? new ParameterNode(field.Name, path, ParameterNodeKind.Field) { Info = ParameterInfo.From(field) }
                    : null;

            case GroupInfo group:
                var children = group.Members.Select(member => Of(member, itemCount)).OfType<ParameterNode>().ToList();
                if (children.Count == 0)
                    return null;
                var node = group is RepeatInfo
                    ? new ParameterNode(group.Name, path, ParameterNodeKind.Repeat) { ItemCount = itemCount(path), Children = children }
                    : new ParameterNode(group.Name, path, ParameterNodeKind.Group) { Children = children };
                foreach (var child in children)
                    child.Parent = node;
                return node;

            default:
                return null;
        }
    }

    // a value the schema fixes or the engine derives is not a parameter
    private static bool IsParameter(FieldInfo field)
        => !field.HasExpectedValue && field.CountOf is null && field.ByteSizeOf is null && field.ByteLengthOf is null;
}

/// <summary>The kinds of nodes of a parameter tree.</summary>
public enum ParameterNodeKind
{
    Group,
    Repeat,
    Field,
}
