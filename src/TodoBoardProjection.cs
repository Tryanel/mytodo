using System.Globalization;

namespace PaperTodo;

public sealed record TodoBoardQueryContext(
    string SearchText,
    string Sort,
    DateOnly Today,
    CultureInfo ComparisonCulture,
    CultureInfo DisplayCulture,
    TimeZoneInfo TimeZone,
    string PendingStatusText,
    string CompletedStatusText,
    TodoBoardFilterState? Filters = null,
    IReadOnlyList<TodoBoardSortRule>? SortRules = null);

public sealed record TodoBoardEntry(
    string PaperId,
    string ItemId,
    string PaperTitle,
    string Text,
    string Note,
    bool Done,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateOnly? PlannedStartDate,
    DateOnly? DueDate,
    int ItemOrder,
    int PaperOrder,
    string StatusText,
    string CreatedText,
    string? CompletedText,
    string? PlannedStartText,
    string? DueText);

public sealed class TodoBoardSnapshot
{
    internal TodoBoardSnapshot(
        IReadOnlyList<TodoBoardEntry> allEntries,
        IReadOnlyList<TodoBoardEntry> queryEntries,
        IReadOnlyList<TodoBoardEntry> tableEntries,
        TodoBoardQueryContext query)
    {
        AllEntries = allEntries;
        QueryEntries = queryEntries;
        TableEntries = tableEntries;
        _query = query;
    }

    private readonly TodoBoardQueryContext _query;

    public IReadOnlyList<TodoBoardEntry> AllEntries { get; }
    public IReadOnlyList<TodoBoardEntry> QueryEntries { get; }
    public IReadOnlyList<TodoBoardEntry> TableEntries { get; }
    public DateOnly Today => _query.Today;

    public IReadOnlyList<TodoBoardEntry> ActivityEntriesOn(DateOnly date) =>
        QueryEntries
            .Where(entry => SpansDate(entry, date))
            .OrderBy(entry => entry.Done)
            .ThenByDescending(entry => entry.CreatedAt)
            .ToList();

    internal (DateOnly Start, DateOnly End) ActivitySpanFor(TodoBoardEntry entry)
    {
        var start = LocalDate(entry.CreatedAt);
        var end = entry.CompletedAt.HasValue
            ? LocalDate(entry.CompletedAt.Value)
            : _query.Today;
        return (start, end < start ? start : end);
    }

    private bool SpansDate(TodoBoardEntry entry, DateOnly date)
    {
        var (start, end) = ActivitySpanFor(entry);
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
                        .Select(item => CreateEntry(
                            paper,
                            item,
                            resolvePaperTitle(paper),
                            paperOrder,
                            query))
                    : [])
            .ToList();

        var searchTokens = ParseSearchTokens(query.SearchText);
        var filters = TodoBoardFilters.Normalize(
            TodoBoardFilters.Clone(query.Filters));
        var existingPaperIds = papers
            .Where(paper => paper.Type == PaperTypes.Todo)
            .Select(paper => paper.Id)
            .ToHashSet(StringComparer.Ordinal);
        var filteredPaperIds = filters.PaperIds
            .Where(existingPaperIds.Contains)
            .ToHashSet(StringComparer.Ordinal);
        var queryEntries = entries
            .Where(entry => MatchesFilters(
                entry,
                filters,
                filteredPaperIds,
                query))
            .Where(entry => searchTokens.All(
                token => MatchesSearch(entry, token, query)))
            .ToList();
        var tableEntries = Sort(queryEntries, query).ToList();

        return new TodoBoardSnapshot(entries, queryEntries, tableEntries, query);
    }

    private static TodoBoardEntry CreateEntry(
        PaperData paper,
        PaperItem item,
        string paperTitle,
        int paperOrder,
        TodoBoardQueryContext query) => new(
            paper.Id,
            item.Id,
            paperTitle,
            item.Text,
            item.Note,
            item.Done,
            item.CreatedAt,
            item.CompletedAt,
            item.PlannedStartDate,
            item.DueDate,
            item.Order,
            paperOrder,
            item.Done ? query.CompletedStatusText : query.PendingStatusText,
            FormatTimestamp(item.CreatedAt, query),
            item.CompletedAt.HasValue
                ? FormatTimestamp(item.CompletedAt.Value, query)
                : null,
            item.PlannedStartDate.HasValue
                ? FormatDate(item.PlannedStartDate.Value, query)
                : null,
            item.DueDate.HasValue
                ? FormatDate(item.DueDate.Value, query)
                : null);

    private static bool MatchesSearch(
        TodoBoardEntry entry,
        string searchText,
        TodoBoardQueryContext query)
    {
        var compareInfo = query.ComparisonCulture.CompareInfo;
        return Contains(entry.Text, searchText, compareInfo) ||
            Contains(entry.Note, searchText, compareInfo) ||
            Contains(entry.PaperTitle, searchText, compareInfo) ||
            Contains(entry.StatusText, searchText, compareInfo) ||
            Contains(entry.CreatedText, searchText, compareInfo) ||
            entry.CompletedText is not null &&
            Contains(entry.CompletedText, searchText, compareInfo) ||
            entry.PlannedStartText is not null &&
            Contains(entry.PlannedStartText, searchText, compareInfo) ||
            entry.DueText is not null &&
            Contains(entry.DueText, searchText, compareInfo);
    }

    private static bool MatchesFilters(
        TodoBoardEntry entry,
        TodoBoardFilterState filters,
        IReadOnlySet<string> filteredPaperIds,
        TodoBoardQueryContext query)
    {
        var statusMatches = filters.Statuses.Count == 0 ||
            filters.Statuses.Contains(
                entry.Done
                    ? TodoBoardFilterStatuses.Done
                    : TodoBoardFilterStatuses.Pending,
                StringComparer.Ordinal);
        var paperMatches = filteredPaperIds.Count == 0 ||
            filteredPaperIds.Contains(entry.PaperId);
        var createdMatches = IsWithinRange(
            LocalDate(entry.CreatedAt, query.TimeZone),
            filters.CreatedFrom,
            filters.CreatedTo);
        var hasCompletedRange = filters.CompletedFrom.HasValue ||
            filters.CompletedTo.HasValue;
        var completedMatches = !hasCompletedRange ||
            entry.CompletedAt.HasValue &&
            IsWithinRange(
                LocalDate(entry.CompletedAt.Value, query.TimeZone),
                filters.CompletedFrom,
                filters.CompletedTo);
        var hasNote = !string.IsNullOrWhiteSpace(entry.Note);
        var noteMatches = TodoBoardNoteFilters.Normalize(filters.Note) switch
        {
            TodoBoardNoteFilters.WithNote => hasNote,
            TodoBoardNoteFilters.WithoutNote => !hasNote,
            _ => true
        };
        var hasPlannedRange = filters.PlannedFrom.HasValue ||
            filters.PlannedTo.HasValue;
        var plannedStart = entry.PlannedStartDate ?? entry.DueDate;
        var plannedEnd = entry.DueDate ?? entry.PlannedStartDate;
        var plannedMatches = !hasPlannedRange ||
            plannedStart.HasValue &&
            plannedEnd.HasValue &&
            (!filters.PlannedTo.HasValue ||
                plannedStart.Value <= filters.PlannedTo.Value) &&
            (!filters.PlannedFrom.HasValue ||
                plannedEnd.Value >= filters.PlannedFrom.Value);
        return statusMatches &&
            paperMatches &&
            createdMatches &&
            completedMatches &&
            noteMatches &&
            plannedMatches;
    }

    private static bool IsWithinRange(
        DateOnly value,
        DateOnly? from,
        DateOnly? to) =>
        (!from.HasValue || value >= from.Value) &&
        (!to.HasValue || value <= to.Value);

    private static DateOnly LocalDate(
        DateTimeOffset value,
        TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, timeZone).DateTime);

    private static IReadOnlyList<string> ParseSearchTokens(string searchText)
    {
        var tokens = new List<string>();
        for (var index = 0; index < searchText.Length;)
        {
            while (index < searchText.Length && char.IsWhiteSpace(searchText[index]))
            {
                index++;
            }
            if (index >= searchText.Length)
            {
                break;
            }

            var quoted = searchText[index] == '"';
            if (quoted)
            {
                index++;
            }
            var start = index;
            while (index < searchText.Length &&
                (quoted
                    ? searchText[index] != '"'
                    : !char.IsWhiteSpace(searchText[index])))
            {
                index++;
            }

            var token = searchText[start..index].Trim();
            if (token.Length > 0)
            {
                tokens.Add(token);
            }
            if (quoted && index < searchText.Length && searchText[index] == '"')
            {
                index++;
            }
        }
        return tokens;
    }

    private static IOrderedEnumerable<TodoBoardEntry> Sort(
        IEnumerable<TodoBoardEntry> entries,
        TodoBoardQueryContext query)
    {
        var sortRules = TodoBoardSortRules.Normalize(query.SortRules);
        if (sortRules.Count == 0)
        {
            sortRules = TodoBoardSortRules.FromLegacy(query.Sort);
        }
        if (sortRules.Count == 0)
        {
            sortRules =
            [
                new TodoBoardSortRule(TodoBoardSortFields.Status, false),
                new TodoBoardSortRule(TodoBoardSortFields.Created, true),
                new TodoBoardSortRule(TodoBoardSortFields.Paper, false)
            ];
        }
        var comparer = Comparer<TodoBoardEntry>.Create(
            (left, right) => CompareEntries(left, right, sortRules, query));
        return entries.OrderBy(entry => entry, comparer);
    }

    private static int CompareEntries(
        TodoBoardEntry left,
        TodoBoardEntry right,
        IReadOnlyList<TodoBoardSortRule> rules,
        TodoBoardQueryContext query)
    {
        var textComparer = StringComparer.Create(
            query.ComparisonCulture,
            ignoreCase: true);
        foreach (var rule in rules)
        {
            var comparison = rule.Field switch
            {
                TodoBoardSortFields.Task => textComparer.Compare(left.Text, right.Text),
                TodoBoardSortFields.Status => left.Done.CompareTo(right.Done),
                TodoBoardSortFields.Paper => textComparer.Compare(
                    left.PaperTitle,
                    right.PaperTitle),
                TodoBoardSortFields.Created => left.CreatedAt.CompareTo(right.CreatedAt),
                TodoBoardSortFields.Completed => CompareOptional(
                    left.CompletedAt,
                    right.CompletedAt,
                    rule.Descending),
                TodoBoardSortFields.PlannedStart => CompareOptional(
                    left.PlannedStartDate,
                    right.PlannedStartDate,
                    rule.Descending),
                TodoBoardSortFields.Due => CompareOptional(
                    left.DueDate,
                    right.DueDate,
                    rule.Descending),
                TodoBoardSortFields.Note => CompareOptionalText(
                    left.Note,
                    right.Note,
                    textComparer,
                    rule.Descending),
                _ => 0
            };
            if (comparison == 0)
            {
                continue;
            }
            if (rule.Field is TodoBoardSortFields.Completed or
                TodoBoardSortFields.PlannedStart or
                TodoBoardSortFields.Due or
                TodoBoardSortFields.Note)
            {
                return comparison;
            }
            return rule.Descending ? -comparison : comparison;
        }

        var paperOrder = left.PaperOrder.CompareTo(right.PaperOrder);
        if (paperOrder != 0)
        {
            return paperOrder;
        }
        var itemOrder = left.ItemOrder.CompareTo(right.ItemOrder);
        return itemOrder != 0
            ? itemOrder
            : StringComparer.Ordinal.Compare(left.ItemId, right.ItemId);
    }

    private static int CompareOptional<T>(
        T? left,
        T? right,
        bool descending)
        where T : struct, IComparable<T>
    {
        if (!left.HasValue)
        {
            return right.HasValue ? 1 : 0;
        }
        if (!right.HasValue)
        {
            return -1;
        }
        var comparison = left.Value.CompareTo(right.Value);
        return descending ? -comparison : comparison;
    }

    private static int CompareOptionalText(
        string left,
        string right,
        StringComparer comparer,
        bool descending)
    {
        var leftEmpty = string.IsNullOrWhiteSpace(left);
        var rightEmpty = string.IsNullOrWhiteSpace(right);
        if (leftEmpty || rightEmpty)
        {
            return leftEmpty == rightEmpty ? 0 : leftEmpty ? 1 : -1;
        }
        var comparison = comparer.Compare(left, right);
        return descending ? -comparison : comparison;
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
            .ToString("yyyy-MM-dd HH:mm", query.DisplayCulture);

    private static string FormatDate(
        DateOnly value,
        TodoBoardQueryContext query) =>
        value.ToString("yyyy-MM-dd", query.DisplayCulture);
}
