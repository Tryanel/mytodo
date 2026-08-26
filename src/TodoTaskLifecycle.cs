namespace PaperTodo;

/// <summary>
/// Establishes a task identity exactly once when a placeholder first receives
/// durable task content. Completion and planning state cannot create a task.
/// </summary>
public static class TodoTaskLifecycle
{
    public const int CurrentStateVersion = 1;

    public static bool MaterializeIfNeeded(
        PaperItem item,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.CreatedAt != default || !HasMaterializingContent(item))
        {
            return false;
        }
        if (createdAt == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(createdAt),
                "A materialized task requires a creation time.");
        }

        item.CreatedAt = createdAt;
        return true;
    }

    private static bool HasMaterializingContent(PaperItem item) =>
        !string.IsNullOrWhiteSpace(item.Text) ||
        !string.IsNullOrWhiteSpace(item.Note) ||
        item.ReminderAt.HasValue ||
        !string.IsNullOrWhiteSpace(item.LinkedPaperId) ||
        !string.IsNullOrWhiteSpace(item.LinkedPath);
}
