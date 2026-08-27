namespace PaperTodo;

public sealed record TodoBoardActivitySegment(
    TodoBoardEntry Entry,
    int WeekIndex,
    int StartColumn,
    int EndColumn,
    int Lane,
    bool ContinuesBefore,
    bool ContinuesAfter,
    bool IsVisible);

public sealed record TodoBoardActivityOverflow(
    DateOnly Date,
    IReadOnlyList<TodoBoardEntry> HiddenEntries);

public sealed class TodoBoardActivityCalendarLayout
{
    private readonly IReadOnlyDictionary<DateOnly, IReadOnlyList<TodoBoardEntry>>
        _entriesByDate;

    private TodoBoardActivityCalendarLayout(
        IReadOnlyList<TodoBoardActivitySegment> segments,
        IReadOnlyList<TodoBoardActivityOverflow> overflowDays,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<TodoBoardEntry>> entriesByDate)
    {
        Segments = segments;
        OverflowDays = overflowDays;
        _entriesByDate = entriesByDate;
    }

    public IReadOnlyList<TodoBoardActivitySegment> Segments { get; }
    public IReadOnlyList<TodoBoardActivityOverflow> OverflowDays { get; }

    public IReadOnlyList<TodoBoardEntry> EntriesOn(DateOnly date) =>
        _entriesByDate.TryGetValue(date, out var entries)
            ? entries
            : [];

    public static TodoBoardActivityCalendarLayout Build(
        TodoBoardSnapshot snapshot,
        DateOnly firstVisibleDate,
        int weekCount,
        int visibleLaneCount)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weekCount);
        ArgumentOutOfRangeException.ThrowIfNegative(visibleLaneCount);

        var lastVisibleDate = firstVisibleDate.AddDays(weekCount * 7 - 1);
        var spans = snapshot.QueryEntries
            .Select(entry =>
            {
                var (start, end) = snapshot.ActivitySpanFor(entry);
                return new ActivitySpan(entry, start, end);
            })
            .Where(span =>
                span.Start <= lastVisibleDate &&
                span.End >= firstVisibleDate)
            .OrderBy(span => span.Start)
            .ThenByDescending(span => span.End)
            .ThenBy(span => span.Entry.PaperOrder)
            .ThenBy(span => span.Entry.ItemOrder)
            .ThenBy(span => span.Entry.PaperId, StringComparer.Ordinal)
            .ThenBy(span => span.Entry.ItemId, StringComparer.Ordinal)
            .ToList();

        var segments = new List<TodoBoardActivitySegment>();
        for (var weekIndex = 0; weekIndex < weekCount; weekIndex++)
        {
            var weekStart = firstVisibleDate.AddDays(weekIndex * 7);
            var weekEnd = weekStart.AddDays(6);
            var weekSpans = spans
                .Where(span => span.Start <= weekEnd && span.End >= weekStart)
                .ToList();
            var occupiedLanes = new List<List<(int Start, int End)>>();

            foreach (var span in weekSpans)
            {
                var startColumn = Math.Max(0, span.Start.DayNumber - weekStart.DayNumber);
                var endColumn = Math.Min(6, span.End.DayNumber - weekStart.DayNumber);
                var lane = FirstAvailableLane(
                    occupiedLanes,
                    startColumn,
                    endColumn);
                OccupyLane(occupiedLanes, lane, startColumn, endColumn);
                segments.Add(new TodoBoardActivitySegment(
                    span.Entry,
                    weekIndex,
                    startColumn,
                    endColumn,
                    lane,
                    span.Start < weekStart,
                    span.End > weekEnd,
                    lane < visibleLaneCount));
            }
        }

        var orderedSegments = segments
            .OrderBy(segment => segment.WeekIndex)
            .ThenBy(segment => segment.Lane)
            .ThenBy(segment => segment.StartColumn)
            .ThenBy(segment => segment.Entry.PaperId, StringComparer.Ordinal)
            .ThenBy(segment => segment.Entry.ItemId, StringComparer.Ordinal)
            .ToList();
        var entriesByDate = new Dictionary<DateOnly, IReadOnlyList<TodoBoardEntry>>();
        var overflowDays = new List<TodoBoardActivityOverflow>();
        for (var dayOffset = 0; dayOffset < weekCount * 7; dayOffset++)
        {
            var date = firstVisibleDate.AddDays(dayOffset);
            var weekIndex = dayOffset / 7;
            var column = dayOffset % 7;
            var daySegments = orderedSegments
                .Where(segment =>
                    segment.WeekIndex == weekIndex &&
                    segment.StartColumn <= column &&
                    segment.EndColumn >= column)
                .OrderBy(segment => segment.Lane)
                .ToList();
            var entries = daySegments
                .Select(segment => segment.Entry)
                .ToList();
            if (entries.Count > 0)
            {
                entriesByDate[date] = entries;
            }

            var hiddenEntries = daySegments
                .Where(segment => !segment.IsVisible)
                .Select(segment => segment.Entry)
                .ToList();
            if (hiddenEntries.Count > 0)
            {
                overflowDays.Add(new TodoBoardActivityOverflow(date, hiddenEntries));
            }
        }

        return new TodoBoardActivityCalendarLayout(
            orderedSegments,
            overflowDays,
            entriesByDate);
    }

    private static int FirstAvailableLane(
        IReadOnlyList<List<(int Start, int End)>> lanes,
        int start,
        int end)
    {
        for (var lane = 0; lane < lanes.Count; lane++)
        {
            if (LaneIsAvailable(lanes, lane, start, end))
            {
                return lane;
            }
        }
        return lanes.Count;
    }

    private static bool LaneIsAvailable(
        IReadOnlyList<List<(int Start, int End)>> lanes,
        int lane,
        int start,
        int end) =>
        lane >= lanes.Count ||
        lanes[lane].All(interval => end < interval.Start || start > interval.End);

    private static void OccupyLane(
        List<List<(int Start, int End)>> lanes,
        int lane,
        int start,
        int end)
    {
        while (lanes.Count <= lane)
        {
            lanes.Add([]);
        }
        lanes[lane].Add((start, end));
    }

    private sealed record ActivitySpan(
        TodoBoardEntry Entry,
        DateOnly Start,
        DateOnly End);
}
