namespace PaperTodo;

public sealed record TodoHistoryTransition(
    List<PaperItem> Items,
    IReadOnlyList<PaperItem> ReplacedItems);

public sealed class TodoUndoHistory
{
    private readonly int _maxDepth;
    private readonly List<List<PaperItem>> _undoStack = [];
    private readonly List<List<PaperItem>> _redoStack = [];

    public TodoUndoHistory(int maxDepth)
    {
        if (maxDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth));
        }

        _maxDepth = maxDepth;
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Record(IEnumerable<PaperItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        AddUndoSnapshot(TodoRules.CloneAll(items));
        _redoStack.Clear();
    }

    public TodoHistoryTransition? Undo(IEnumerable<PaperItem> currentItems)
    {
        ArgumentNullException.ThrowIfNull(currentItems);
        if (!CanUndo)
        {
            return null;
        }

        var currentSnapshot = TodoRules.CloneAll(currentItems);
        _redoStack.Add(currentSnapshot);
        var previousItems = Pop(_undoStack);
        return new TodoHistoryTransition(
            previousItems,
            TodoRules.CloneAll(currentSnapshot));
    }

    public TodoHistoryTransition? Redo(IEnumerable<PaperItem> currentItems)
    {
        ArgumentNullException.ThrowIfNull(currentItems);
        if (!CanRedo)
        {
            return null;
        }

        var currentSnapshot = TodoRules.CloneAll(currentItems);
        AddUndoSnapshot(currentSnapshot);
        var nextItems = Pop(_redoStack);
        return new TodoHistoryTransition(
            nextItems,
            TodoRules.CloneAll(currentSnapshot));
    }

    private void AddUndoSnapshot(List<PaperItem> snapshot)
    {
        _undoStack.Add(snapshot);
        if (_undoStack.Count > _maxDepth)
        {
            _undoStack.RemoveAt(0);
        }
    }

    private static List<PaperItem> Pop(List<List<PaperItem>> stack)
    {
        var snapshot = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return snapshot;
    }
}
