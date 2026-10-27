namespace Jacobi.Midi.Console.Core.Editing;

/// <summary>
/// A change of a dashboard or rig that can be undone: everything the user changes in Edit mode is one.
/// </summary>
public interface IEdit
{
    /// <summary>What the edit does, for the user (e.g. <c>Label of 'Cutoff'</c>).</summary>
    string Description { get; }

    void Apply();

    void Revert();
}

/// <summary>
/// An edit that sets a value: it takes the value it replaces when it is created, and sets that value back when it is
/// undone.
/// </summary>
public sealed class ValueEdit<T> : IEdit
{
    private readonly Action<T> _set;
    private readonly T _value;
    private readonly T _previous;

    public ValueEdit(string description, Func<T> get, Action<T> set, T value)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        Description = description;
        _set = set;
        _value = value;
        _previous = get();
    }

    public string Description { get; }

    /// <summary>False when the value is the value it replaces: the edit changes nothing.</summary>
    public bool Changes => !EqualityComparer<T>.Default.Equals(_value, _previous);

    public void Apply() => _set(_value);

    public void Revert() => _set(_previous);
}

/// <summary>
/// Edits that belong together (a move and the controls it pushes aside): done in order, undone in reverse, as one.
/// </summary>
public sealed class EditGroup(string description, IReadOnlyList<IEdit> edits) : IEdit
{
    public string Description => description;

    public IReadOnlyList<IEdit> Edits => edits;

    public void Apply()
    {
        foreach (var edit in edits)
            edit.Apply();
    }

    public void Revert()
    {
        for (var index = edits.Count - 1; index >= 0; index--)
            edits[index].Revert();
    }
}

/// <summary>
/// The edits done, to undo them in reverse order, and the edits undone, to redo them; a new edit clears what was
/// undone.
/// </summary>
public sealed class EditHistory
{
    private readonly Stack<IEdit> _done = new();
    private readonly Stack<IEdit> _undone = new();

    public bool CanUndo => _done.Count > 0;

    public bool CanRedo => _undone.Count > 0;

    /// <summary>The edit <see cref="Undo"/> reverts; null when there is none.</summary>
    public IEdit? NextUndo => _done.TryPeek(out var edit) ? edit : null;

    /// <summary>The edit <see cref="Redo"/> applies again; null when there is none.</summary>
    public IEdit? NextRedo => _undone.TryPeek(out var edit) ? edit : null;

    /// <summary>An edit was done, undone or redone, or the history was cleared.</summary>
    public event EventHandler? Changed;

    /// <summary>Applies the edit and remembers it.</summary>
    public void Do(IEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        edit.Apply();
        _done.Push(edit);
        _undone.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reverts the last edit; null when there is none.</summary>
    public IEdit? Undo()
    {
        if (!_done.TryPop(out var edit))
            return null;
        edit.Revert();
        _undone.Push(edit);
        Changed?.Invoke(this, EventArgs.Empty);
        return edit;
    }

    /// <summary>Applies the last undone edit again; null when there is none.</summary>
    public IEdit? Redo()
    {
        if (!_undone.TryPop(out var edit))
            return null;
        edit.Apply();
        _done.Push(edit);
        Changed?.Invoke(this, EventArgs.Empty);
        return edit;
    }

    /// <summary>Forgets all edits: the objects they change are no longer open.</summary>
    public void Clear()
    {
        _done.Clear();
        _undone.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
