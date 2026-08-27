namespace PaperTodo;

internal enum TodoCompletionRecordOrigin
{
    DirectCheckbox,
    Batch,
    External
}

/// <summary>
/// Keeps only the stable identity of the task currently offering the optional completion note.
/// The owning PaperItem remains authoritative; UI surfaces may disappear and be rebuilt freely.
/// </summary>
internal sealed class TodoCompletionRecordPromptSession
{
    public string? ItemId { get; private set; }

    public bool TryOffer(
        string? itemId,
        bool wasDone,
        bool isDone,
        bool itemRemainsAvailable,
        TodoCompletionRecordOrigin origin)
    {
        if (origin != TodoCompletionRecordOrigin.DirectCheckbox ||
            wasDone ||
            !isDone ||
            !itemRemainsAvailable ||
            string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        ItemId = itemId;
        return true;
    }

    public bool IsCurrent(string? itemId) =>
        !string.IsNullOrWhiteSpace(itemId) &&
        string.Equals(ItemId, itemId, StringComparison.Ordinal);

    public bool DismissIfInvalid(Func<string, bool> isStillValid)
    {
        ArgumentNullException.ThrowIfNull(isStillValid);
        if (ItemId == null || isStillValid(ItemId))
        {
            return false;
        }

        Dismiss();
        return true;
    }

    public void Dismiss() => ItemId = null;
}
