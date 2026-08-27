namespace PaperTodo;

public static class TodoBoardPlanningItemKinds
{
    public const string Span = "span";
    public const string Marker = "marker";
}

public sealed record TodoBoardPlanningItem(
    TodoBoardEntry Entry,
    string Kind,
    DateOnly PlannedStart,
    DateOnly PlannedEnd,
    int StartIndex,
    int EndIndex,
    bool ContinuesBefore,
    bool ContinuesAfter);

public sealed class TodoBoardPlanningTimelineLayout
{
    private TodoBoardPlanningTimelineLayout(
        string scale,
        DateOnly windowStart,
        DateOnly windowEnd,
        IReadOnlyList<TodoBoardPlanningItem> scheduledItems,
        IReadOnlyList<TodoBoardEntry> unscheduledEntries)
    {
        Scale = scale;
        WindowStart = windowStart;
        WindowEnd = windowEnd;
        ScheduledItems = scheduledItems;
        UnscheduledEntries = unscheduledEntries;
    }

    public string Scale { get; }
    public DateOnly WindowStart { get; }
    public DateOnly WindowEnd { get; }
    public int DayCount => WindowEnd.DayNumber - WindowStart.DayNumber + 1;
    public IReadOnlyList<TodoBoardPlanningItem> ScheduledItems { get; }
    public IReadOnlyList<TodoBoardEntry> UnscheduledEntries { get; }

    public static TodoBoardPlanningTimelineLayout Build(
        TodoBoardSnapshot snapshot,
        DateOnly anchor,
        string? scale,
        DayOfWeek firstDayOfWeek)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var normalizedScale = TodoBoardTimelineScales.Normalize(scale);
        var (windowStart, windowEnd) = WindowFor(
            anchor,
            normalizedScale,
            firstDayOfWeek);
        var scheduledItems = new List<TodoBoardPlanningItem>();
        var unscheduledEntries = new List<TodoBoardEntry>();
        foreach (var entry in snapshot.QueryEntries)
        {
            if (!entry.PlannedStartDate.HasValue && !entry.DueDate.HasValue)
            {
                unscheduledEntries.Add(entry);
                continue;
            }

            var plannedStart = entry.PlannedStartDate ?? entry.DueDate!.Value;
            var plannedEnd = entry.DueDate ?? entry.PlannedStartDate!.Value;
            if (plannedStart > windowEnd || plannedEnd < windowStart)
            {
                continue;
            }

            scheduledItems.Add(new TodoBoardPlanningItem(
                entry,
                entry.PlannedStartDate.HasValue && entry.DueDate.HasValue
                    ? TodoBoardPlanningItemKinds.Span
                    : TodoBoardPlanningItemKinds.Marker,
                plannedStart,
                plannedEnd,
                Math.Max(0, plannedStart.DayNumber - windowStart.DayNumber),
                Math.Min(
                    windowEnd.DayNumber - windowStart.DayNumber,
                    plannedEnd.DayNumber - windowStart.DayNumber),
                plannedStart < windowStart,
                plannedEnd > windowEnd));
        }

        scheduledItems = scheduledItems
            .OrderBy(item => item.PlannedStart)
            .ThenByDescending(item => item.PlannedEnd)
            .ThenBy(item => item.Entry.PaperOrder)
            .ThenBy(item => item.Entry.ItemOrder)
            .ThenBy(item => item.Entry.PaperId, StringComparer.Ordinal)
            .ThenBy(item => item.Entry.ItemId, StringComparer.Ordinal)
            .ToList();

        return new TodoBoardPlanningTimelineLayout(
            normalizedScale,
            windowStart,
            windowEnd,
            scheduledItems,
            unscheduledEntries);
    }

    private static (DateOnly Start, DateOnly End) WindowFor(
        DateOnly anchor,
        string scale,
        DayOfWeek firstDayOfWeek)
    {
        if (scale == TodoBoardTimelineScales.Month)
        {
            var start = new DateOnly(anchor.Year, anchor.Month, 1);
            return (start, start.AddMonths(1).AddDays(-1));
        }

        var offset = ((int)anchor.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        var weekStart = anchor.AddDays(-offset);
        return (weekStart, weekStart.AddDays(6));
    }
}
