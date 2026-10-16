using CommunityToolkit.Mvvm.ComponentModel;
using Jacobi.BinarySerializer.Schema;
using System.Collections.ObjectModel;

namespace Jacobi.Midi.SchemaEditor.Models;

public sealed partial class SchemaTreeItem : ObservableObject
{
    public SchemaTreeItem(
        SchemaNode node,
        int[] memberPath,
        int depth)
    {
        Name = node.Name;
        Kind = node.Kind.ToString();
        DataType = node is SchemaField field ? field.DataType?.ToString() ?? String.Empty : String.Empty;
        Description = node.Properties.FirstOrDefault(p => p.Name == "description")?.Value ?? String.Empty;
        SampleValue = node.Properties.FirstOrDefault(p => p.Name == "sample")?.Value ?? String.Empty;
        MemberPath = memberPath;
        Depth = depth;

        if (node is SchemaGroup group)
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
