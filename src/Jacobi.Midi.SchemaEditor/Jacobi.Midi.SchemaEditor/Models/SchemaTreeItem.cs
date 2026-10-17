using CommunityToolkit.Mvvm.ComponentModel;
using Jacobi.BinarySerializer.Schema;
using Jacobi.BinarySerializer.Schema.Editing;
using System.Collections.ObjectModel;

namespace Jacobi.Midi.SchemaEditor.Models;

public sealed partial class SchemaTreeItem : ObservableObject
{
    public SchemaTreeItem(
        EditNode node,
        int[] memberPath,
        int depth)
    {
        Node = node;
        Name = node.Name;
        Kind = node switch
        {
            EditField => nameof(SchemaNodeKind.Field),
            EditPackedGroup => nameof(SchemaNodeKind.PackedGroup),
            EditRepeat => nameof(SchemaNodeKind.Repeat),
            EditChoice => nameof(SchemaNodeKind.Choice),
            EditDocument => nameof(SchemaNodeKind.Schema),
            EditGroup => nameof(SchemaNodeKind.Group),
            _ => throw new NotSupportedException($"Unsupported schema tree node: {node.GetType().Name}.")
        };
        DataType = node is EditField field ? field.DataType?.ToString() ?? String.Empty : String.Empty;
        Description = node.Properties.FirstOrDefault(p => p.Name == "description")?.Value ?? String.Empty;
        SampleValue = node.Properties.FirstOrDefault(p => p.Name == "sample")?.Value ?? String.Empty;
        MemberPath = memberPath;
        Depth = depth;

        if (node is EditGroup group)
        {
            for (var index = 0; index < group.Members.Count; index++)
            {
                Children.Add(new SchemaTreeItem(
                    group.Members[index],
                    [.. memberPath, index],
                    depth + 1));
            }
        }
    }

    public EditNode Node { get; }
    public string Kind { get; }
    public int[] MemberPath { get; }
    public int Depth { get; }
    public ObservableCollection<SchemaTreeItem> Children { get; } = [];

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string DataType { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

    [ObservableProperty]
    public partial string SampleValue { get; set; }

    public string DisplayName => $"{Name}  ·  {Kind}";
}
