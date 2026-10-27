using Jacobi.Midi.Console.Core.Editing;
using Xunit;

namespace Jacobi.Midi.Tests.Console;

/// <summary>
/// Undo and redo of edits.
/// </summary>
public class EditHistoryTests
{
    private sealed class Holder
    {
        public string? Label { get; set; }
    }

    private static ValueEdit<string?> SetLabel(Holder holder, string? label)
        => new("Label", () => holder.Label, value => holder.Label = value, label);

    [Fact]
    public void Undo_RevertsInReverseOrder_AndRedoAppliesAgain()
    {
        var holder = new Holder { Label = "a" };
        var history = new EditHistory();

        history.Do(SetLabel(holder, "b"));
        history.Do(SetLabel(holder, "c"));
        Assert.Equal("c", holder.Label);

        history.Undo();
        Assert.Equal("b", holder.Label);
        history.Undo();
        Assert.Equal("a", holder.Label);
        Assert.False(history.CanUndo);

        history.Redo();
        Assert.Equal("b", holder.Label);
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void Do_AfterUndo_ClearsWhatWasUndone()
    {
        var holder = new Holder { Label = "a" };
        var history = new EditHistory();
        history.Do(SetLabel(holder, "b"));
        history.Undo();

        history.Do(SetLabel(holder, "c"));

        Assert.False(history.CanRedo);
        Assert.Null(history.Redo());
        Assert.Equal("c", holder.Label);
    }

    [Fact]
    public void ValueEdit_ThatSetsTheSameValue_ChangesNothing()
    {
        var holder = new Holder { Label = "a" };

        Assert.False(SetLabel(holder, "a").Changes);
        Assert.True(SetLabel(holder, null).Changes);
    }

    [Fact]
    public void Changed_IsRaised_ForEveryStep()
    {
        var holder = new Holder();
        var history = new EditHistory();
        var changes = 0;
        history.Changed += (_, _) => changes++;

        history.Do(SetLabel(holder, "b"));
        history.Undo();
        history.Redo();
        history.Clear();

        Assert.Equal(4, changes);
        Assert.False(history.CanUndo);
    }
}
