namespace NaraDreamPainter.App.Services;

public interface IUndoAction
{
    string Name { get; }

    /// <summary>
    /// Actions that share a key collapse into one entry while they sit next to each other, so a slider
    /// drag over a property leaves a single step in the history instead of one per pixel of travel.
    /// </summary>
    string? MergeKey { get; }

    void Undo();

    void Redo();

    IUndoAction MergeWith(IUndoAction newer);
}

public sealed class DelegateAction : IUndoAction
{
    private readonly Action _undo;
    private readonly Action _redo;

    public DelegateAction(string name, Action undo, Action redo)
    {
        Name = name;
        _undo = undo;
        _redo = redo;
    }

    public string Name { get; }

    public string? MergeKey => null;

    public void Undo() => _undo();

    public void Redo() => _redo();

    public IUndoAction MergeWith(IUndoAction newer) => newer;
}

/// <summary>
/// A value that was edited in place: the pair of setters restores either end of the change.
/// </summary>
public sealed class PropertyChange<T> : IUndoAction
{
    private readonly T _oldValue;
    private readonly T _newValue;
    private readonly Action<T> _apply;
    private readonly string? _mergeKey;

    public PropertyChange(string name, T oldValue, T newValue, Action<T> apply, string? mergeKey = null)
    {
        Name = name;
        _oldValue = oldValue;
        _newValue = newValue;
        _apply = apply;
        _mergeKey = mergeKey;
    }

    public string Name { get; }

    public string? MergeKey => _mergeKey;

    public void Undo() => _apply(_oldValue);

    public void Redo() => _apply(_newValue);

    public IUndoAction MergeWith(IUndoAction newer) => newer is PropertyChange<T> next
        ? new PropertyChange<T>(next.Name, _oldValue, next._newValue, next._apply, _mergeKey)
        : newer;
}

/// <summary>
/// Undo history. Pushed actions have already been applied by the caller, so pushing only records the
/// step; Undo and Redo replay it. Merging keeps continuous edits of one property to a single step.
/// </summary>
public sealed class UndoStack
{
    private const int Limit = 200;

    private readonly List<IUndoAction> _undo = [];
    private readonly List<IUndoAction> _redo = [];

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoName => _undo.Count > 0 ? _undo[^1].Name : null;

    public string? RedoName => _redo.Count > 0 ? _redo[^1].Name : null;

    public void Push(IUndoAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action.MergeKey is not null && _undo.Count > 0 && _undo[^1].MergeKey == action.MergeKey)
        {
            _undo[^1] = _undo[^1].MergeWith(action);
        }
        else
        {
            _undo.Add(action);
            if (_undo.Count > Limit) _undo.RemoveAt(0);
        }

        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;

        IUndoAction action = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        action.Undo();
        _redo.Add(action);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;

        IUndoAction action = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        action.Redo();
        _undo.Add(action);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
