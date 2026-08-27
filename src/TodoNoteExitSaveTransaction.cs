namespace PaperTodo;

/// <summary>
/// Temporarily projects approved todo-note drafts into authoritative items so one synchronous
/// state save can persist the whole exit batch. A failed save restores the exact in-memory
/// values; editor sessions are committed only after persistence succeeds.
/// </summary>
internal sealed class TodoNoteExitSaveMutation
{
    private readonly PaperItem _item;
    private readonly string _draft;
    private readonly string _originalNote;
    private readonly DateTimeOffset _originalCreatedAt;

    public TodoNoteExitSaveMutation(PaperItem item, string draft)
    {
        ArgumentNullException.ThrowIfNull(item);
        _item = item;
        _draft = draft ?? "";
        _originalNote = item.Note;
        _originalCreatedAt = item.CreatedAt;
    }

    public void Apply()
    {
        _item.Note = _draft;
        TodoTaskLifecycle.MaterializeIfNeeded(_item, DateTimeOffset.Now);
    }

    public void Rollback()
    {
        _item.Note = _originalNote;
        _item.CreatedAt = _originalCreatedAt;
    }
}

internal sealed class TodoNoteExitSaveTransaction
{
    private readonly IReadOnlyList<TodoNoteExitSaveMutation> _mutations;

    public TodoNoteExitSaveTransaction(
        IEnumerable<TodoNoteExitSaveMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);
        _mutations = mutations.ToList();
    }

    public bool TryPersist(Func<bool> persist)
    {
        ArgumentNullException.ThrowIfNull(persist);
        var succeeded = false;
        try
        {
            foreach (var mutation in _mutations)
            {
                mutation.Apply();
            }
            succeeded = persist();
            return succeeded;
        }
        finally
        {
            if (!succeeded)
            {
                foreach (var mutation in _mutations.Reverse())
                {
                    mutation.Rollback();
                }
            }
        }
    }
}
