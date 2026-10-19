using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Jacobi.Midi.SchemaEditor.Models;

internal sealed class SysExMessage
{
    public SysExMessage(byte[] bytes)
    {
        Bytes = bytes;
        HasStartMarker = bytes.Length > 0 && bytes[0] == 0xF0;
        HasEndMarker = bytes.Length > 0 && bytes[^1] == 0xF7;
        Hex = String.Join(' ', bytes.Select(b => b.ToString("X2")));

        for (var index = 0; index < bytes.Length; index++)
        {
            ByteCells.Add(new SysExByteCell(index, bytes[index], index == 0 && HasStartMarker, index == bytes.Length - 1 && HasEndMarker));
        }
    }

    public byte[] Bytes { get; }
    public string Hex { get; }
    public bool HasStartMarker { get; }
    public bool HasEndMarker { get; }
    public ObservableCollection<SysExByteCell> ByteCells { get; } = [];
    public string BoundarySummary => $"{(HasStartMarker ? "F0 start detected" : "No F0 start")}  ·  {(HasEndMarker ? "F7 end detected" : "No F7 end")}";
}

public sealed partial class SysExByteCell : ObservableObject
{
    public SysExByteCell(int index, byte value, bool isStart, bool isEnd)
    {
        Index = index;
        Value = value;
        IsStart = isStart;
        IsEnd = isEnd;
    }

    public int Index { get; }
    public byte Value { get; }
    public bool IsStart { get; }
    public bool IsEnd { get; }
    public string Hex => Value.ToString("X2");
    public string Marker => IsStart ? "F0" : IsEnd ? "F7" : String.Empty;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string Background => IsSelected ? "#674D1E" : IsStart || IsEnd ? "#243B55" : "#172338";

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(Background));
}

public sealed class SysExComparisonRow
{
    public required int Index { get; init; }
    public required string Left { get; init; }
    public required string Right { get; init; }
    public required bool IsDifferent { get; init; }
    public string Background => IsDifferent ? "#4A3023" : "#172338";
}
