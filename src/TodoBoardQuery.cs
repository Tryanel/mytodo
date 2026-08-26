namespace PaperTodo;

public static class TodoBoardFilterStatuses
{
    public const string Pending = "pending";
    public const string Done = "done";

    public static bool IsValid(string? status) => status is Pending or Done;
}

public static class TodoBoardNoteFilters
{
    public const string Any = "any";
    public const string WithNote = "with-note";
    public const string WithoutNote = "without-note";

    public static string Normalize(string? value) => value switch
    {
        WithNote => WithNote,
        WithoutNote => WithoutNote,
        _ => Any
    };
}

public sealed class TodoBoardFilterState
{
    public List<string> Statuses { get; set; } = [];
    public List<string> PaperIds { get; set; } = [];
    public DateOnly? CreatedFrom { get; set; }
    public DateOnly? CreatedTo { get; set; }
    public DateOnly? CompletedFrom { get; set; }
    public DateOnly? CompletedTo { get; set; }
    public DateOnly? PlannedFrom { get; set; }
    public DateOnly? PlannedTo { get; set; }
    public string Note { get; set; } = TodoBoardNoteFilters.Any;
}

public static class TodoBoardFilters
{
    public static TodoBoardFilterState Clone(TodoBoardFilterState? filters)
    {
        filters ??= new TodoBoardFilterState();
        return new TodoBoardFilterState
        {
            Statuses = [.. filters.Statuses ?? []],
            PaperIds = [.. filters.PaperIds ?? []],
            CreatedFrom = filters.CreatedFrom,
            CreatedTo = filters.CreatedTo,
            CompletedFrom = filters.CompletedFrom,
            CompletedTo = filters.CompletedTo,
            PlannedFrom = filters.PlannedFrom,
            PlannedTo = filters.PlannedTo,
            Note = filters.Note
        };
    }

    public static bool IsActive(TodoBoardFilterState? filters) =>
        filters is not null &&
        ((filters.Statuses?.Count ?? 0) > 0 ||
            (filters.PaperIds?.Count ?? 0) > 0 ||
            filters.CreatedFrom.HasValue ||
            filters.CreatedTo.HasValue ||
            filters.CompletedFrom.HasValue ||
            filters.CompletedTo.HasValue ||
            filters.PlannedFrom.HasValue ||
            filters.PlannedTo.HasValue ||
            TodoBoardNoteFilters.Normalize(filters.Note) != TodoBoardNoteFilters.Any);

    public static TodoBoardFilterState Normalize(TodoBoardFilterState? filters)
    {
        filters ??= new TodoBoardFilterState();
        filters.Statuses = (filters.Statuses ?? [])
            .Where(TodoBoardFilterStatuses.IsValid)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        filters.PaperIds = (filters.PaperIds ?? [])
            .Select(id => id?.Trim() ?? "")
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        filters.Note = TodoBoardNoteFilters.Normalize(filters.Note);
        (filters.CreatedFrom, filters.CreatedTo) = NormalizeRange(
            filters.CreatedFrom,
            filters.CreatedTo);
        (filters.CompletedFrom, filters.CompletedTo) = NormalizeRange(
            filters.CompletedFrom,
            filters.CompletedTo);
        (filters.PlannedFrom, filters.PlannedTo) = NormalizeRange(
            filters.PlannedFrom,
            filters.PlannedTo);
        return filters;
    }

    private static (DateOnly? From, DateOnly? To) NormalizeRange(
        DateOnly? from,
        DateOnly? to) =>
        from.HasValue && to.HasValue && from.Value > to.Value
            ? (to, from)
            : (from, to);
}

public static class TodoBoardSortFields
{
    public const string Task = "task";
    public const string Status = "status";
    public const string Paper = "paper";
    public const string Created = "created";
    public const string Completed = "completed";
    public const string PlannedStart = "planned-start";
    public const string Due = "due";
    public const string Note = "note";

    public static bool IsValid(string? field) => field is
        Task or
        Status or
        Paper or
        Created or
        Completed or
        PlannedStart or
        Due or
        Note;
}

public sealed record TodoBoardSortRule(string Field, bool Descending);

public static class TodoBoardSortRules
{
    public static List<TodoBoardSortRule> FromLegacy(string? sort) =>
        TodoBoardSorts.Normalize(sort) switch
        {
            TodoBoardSorts.TaskAscending => [new(TodoBoardSortFields.Task, false)],
            TodoBoardSorts.TaskDescending => [new(TodoBoardSortFields.Task, true)],
            TodoBoardSorts.StatusAscending =>
            [
                new(TodoBoardSortFields.Status, false),
                new(TodoBoardSortFields.Created, true)
            ],
            TodoBoardSorts.StatusDescending =>
            [
                new(TodoBoardSortFields.Status, true),
                new(TodoBoardSortFields.Created, true)
            ],
            TodoBoardSorts.PaperAscending => [new(TodoBoardSortFields.Paper, false)],
            TodoBoardSorts.PaperDescending => [new(TodoBoardSortFields.Paper, true)],
            TodoBoardSorts.CreatedAscending => [new(TodoBoardSortFields.Created, false)],
            TodoBoardSorts.CreatedDescending => [new(TodoBoardSortFields.Created, true)],
            TodoBoardSorts.CompletedAscending => [new(TodoBoardSortFields.Completed, false)],
            TodoBoardSorts.CompletedDescending => [new(TodoBoardSortFields.Completed, true)],
            TodoBoardSorts.NoteAscending => [new(TodoBoardSortFields.Note, false)],
            TodoBoardSorts.NoteDescending => [new(TodoBoardSortFields.Note, true)],
            _ => []
        };

    public static List<TodoBoardSortRule> Normalize(
        IEnumerable<TodoBoardSortRule>? rules)
    {
        var normalized = new List<TodoBoardSortRule>();
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules ?? [])
        {
            if (rule is null ||
                !TodoBoardSortFields.IsValid(rule.Field) ||
                !fields.Add(rule.Field))
            {
                continue;
            }
            normalized.Add(new TodoBoardSortRule(rule.Field, rule.Descending));
        }
        return normalized;
    }

    public static List<TodoBoardSortRule> SetPrimary(
        IEnumerable<TodoBoardSortRule>? current,
        string field,
        bool descendingFirst)
    {
        if (!TodoBoardSortFields.IsValid(field))
        {
            throw new ArgumentOutOfRangeException(nameof(field));
        }

        var normalized = Normalize(current);
        var first = normalized.FirstOrDefault();
        var existing = normalized.FirstOrDefault(rule => rule.Field == field);
        var descending = first?.Field == field
            ? !first.Descending
            : existing?.Descending ?? descendingFirst;
        return
        [
            new TodoBoardSortRule(field, descending),
            .. normalized.Where(rule => rule.Field != field)
        ];
    }
}
