using System.Globalization;

namespace PaperTodo;

public sealed record TodoBoardQueryContext(
    string SearchText,
    string Sort,
    DateOnly Today,
    CultureInfo Culture,
    TimeZoneInfo TimeZone,
    string PendingStatusText,
    string CompletedStatusText);

public sealed record TodoBoardEntry(
    string PaperId,
    string ItemId,
    string PaperTitle,
    string Text,
    string Note,
    bool Done,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int ItemOrder,
    int PaperOrder);

public sealed class TodoBoardSnapshot
{
    internal TodoBoardSnapshot(
        IReadOnlyList<TodoBoardEntry> allEntries,
        IReadOnlyList<TodoBoardEntry> tableEntries,
        TodoBoardQueryContext query)
    {
        AllEntries = allEntries;
        TableEntries = tableEntries;
        _query = query;
    }

    private readonly TodoBoardQueryContext _query;

    public IReadOnlyList<TodoBoardEntry> AllEntries { get; }
    public IReadOnlyList<TodoBoardEntry> TableEntries { get; }
    public DateOnly Today => _query.Today;

    public IReadOnlyList<TodoBoardEntry> ActivityEntriesOn(DateOnly date) =>
        AllEntries
            .Where(entry => SpansDate(entry, date))
            .OrderBy(entry => entry.Done)
            .ThenByDescending(entry => entry.CreatedAt)
            .ToList();

    private bool SpansDate(TodoBoardEntry entry, DateOnly date)
    {
        var start = LocalDate(entry.CreatedAt);
        var end = entry.CompletedAt.HasValue
            ? LocalDate(entry.CompletedAt.Value)
            : _query.Today;
        if (end < start)
        {
            end = start;
        }
        return date >= start && date <= end;
    }

    private DateOnly LocalDate(DateTimeOffset value) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, _query.TimeZone).DateTime);
}

public static class TodoBoardProjection
{
    public static TodoBoardSnapshot Build(
        IReadOnlyList<PaperData> papers,
        Func<PaperData, string> resolvePaperTitle,
        TodoBoardQueryContext query)
    {
        ArgumentNullException.ThrowIfNull(papers);
        ArgumentNullException.ThrowIfNull(resolvePaperTitle);
        ArgumentNullException.ThrowIfNull(query);

        var entries = papers
            .SelectMany((paper, paperOrder) =>
                paper.Type == PaperTypes.Todo
                    ? paper.Items
                        .Where(item => !TodoRules.IsPlaceholder(item))
                        .Select(item => new TodoBoardEntry(
                            paper.Id,
                            item.Id,
                            resolvePaperTitle(paper),
                            item.Text,
                            item.Note,
                            item.Done,
                            item.CreatedAt,
                            item.CompletedAt,
                            item.Order,
                            paperOrder))
                    : [])
            .ToList();

        var searchText = query.SearchText.Trim();
        var filteredEntries = string.IsNullOrEmpty(searchText)
            ? entries
            : entries
                .Where(entry => MatchesSearch(entry, searchText, query));
        var tableEntries = Sort(filteredEntries, query).ToList();

        return new TodoBoardSnapshot(entries, tableEntries, query);
    }

    private static bool MatchesSearch(
        TodoBoardEntry entry,
        string searchText,
        TodoBoardQueryContext query)
    {
        var compareInfo = query.Culture.CompareInfo;
        return Contains(entry.Text, searchText, compareInfo) ||
            Contains(entry.Note, searchText, compareInfo) ||
            Contains(entry.PaperTitle, searchText, compareInfo) ||
            Contains(
                entry.Done
                    ? query.CompletedStatusText
                    : query.PendingStatusText,
                searchText,
                compareInfo) ||
            Contains(FormatTimestamp(entry.CreatedAt, query), searchText, compareInfo) ||
            entry.CompletedAt.HasValue &&
            Contains(
                FormatTimestamp(entry.CompletedAt.Value, query),
                searchText,
                compareInfo);
    }

    private static IOrderedEnumerable<TodoBoardEntry> Sort(
        IEnumerable<TodoBoardEntry> entries,
        TodoBoardQueryContext query)
    {
        var textComparer = StringComparer.Create(query.Culture, ignoreCase: true);
        return TodoBoardSorts.Normalize(query.Sort) switch
        {
            TodoBoardSorts.TaskAscending => entries
                .OrderBy(entry => entry.Text, textComparer)
                .ThenBy(entry => entry.PaperOrder)
                .ThenBy(entry => entry.ItemOrder),
            TodoBoardSorts.TaskDescending => entries
                .OrderByDescending(entry => entry.Text, textComparer)
                .ThenBy(entry => entry.PaperOrder)
                .ThenBy(entry => entry.ItemOrder),
            TodoBoardSorts.StatusAscending => entries
                .OrderBy(entry => entry.Done)
                .ThenByDescending(entry => entry.CreatedAt)
                .ThenBy(entry => entry.PaperOrder)
                .ThenBy(entry => entry.ItemOrder),
            TodoBoardSorts.StatusDescending => entries
                .OrderByDescending(entry => entry.Done)
                .ThenByDescending(entry => entry.CreatedAt)
                .ThenBy(entry => entry.PaperOrder)
                .ThenBy(entry => entry.ItemOrder),
            TodoBoardSorts.PaperAscending => entries
                .OrderBy(entry => entry.PaperTitle, textComparer)
                .ThenBy(entry => entry.ItemOrder),
            TodoBoardSorts.PaperDescending => entries
                .OrderByDescending(entry => entry.PaperTitle, textComparer)
                .ThenBy(entry => entry.ItemOrder),
            TodoBoardSorts.CreatedAscending => entries
                .OrderBy(entry => entry.CreatedAt)
                .ThenBy(entry => entry.PaperOrder)
                .ThenBy(entry => entry.ItemOrder),
            TodoBoardSorts.CreatedDescending => entries
                .OrderByDescending(entry => entry.CreatedAt)
                .ThenBy(entry => entry.PaperOrder)
                .ThenBy(entry => entry.ItemOrder),
            TodoBoardSorts.CompletedAscending => entries
                .OrderBy(entry => !entry.CompletedAt.HasValue)
                .ThenBy(entry => entry.CompletedAt)
                .ThenBy(entry => entry.PaperOrder)
                .ThenBy(entry => entry.ItemOrder),
            TodoBoardSorts.CompletedDescending => entries
                .OrderBy(entry => !entry.CompletedAt.HasValue)
                .ThenByDescending(entry => entry.CompletedAt)
                .ThenBy(entry => entry.PaperOrder)
                .ThenBy(entry => entry.ItemOrder),
            TodoBoardSorts.NoteAscending => entries
                .OrderBy(entry => string.IsNullOrWhiteSpace(entry.Note))
                .ThenBy(entry => entry.Note, textComparer)
                .ThenBy(entry => entry.PaperOrder)
                .ThenBy(entry => entry.ItemOrder),
            TodoBoardSorts.NoteDescending => entries
                .OrderBy(entry => string.IsNullOrWhiteSpace(entry.Note))
                .ThenByDescending(entry => entry.Note, textComparer)
                .ThenBy(entry => entry.PaperOrder)
                .ThenBy(entry => entry.ItemOrder),
            _ => entries
                .OrderBy(entry => entry.Done)
                .ThenByDescending(entry => entry.CreatedAt)
                .ThenBy(entry => entry.PaperTitle, textComparer)
                .ThenBy(entry => entry.ItemOrder)
        };
    }

    private static bool Contains(
        string value,
        string searchText,
        CompareInfo compareInfo) =>
        compareInfo.IndexOf(value, searchText, CompareOptions.IgnoreCase) >= 0;

    private static string FormatTimestamp(
        DateTimeOffset value,
        TodoBoardQueryContext query) =>
        TimeZoneInfo.ConvertTime(value, query.TimeZone)
            .ToString("yyyy-MM-dd HH:mm", query.Culture);
}
