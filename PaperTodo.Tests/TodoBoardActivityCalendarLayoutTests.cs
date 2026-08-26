using System.Globalization;

namespace PaperTodo.Tests;

public sealed class TodoBoardActivityCalendarLayoutTests
{
    [Fact]
    public void Build_uses_inclusive_activity_spans_and_explicit_today()
    {
        var snapshot = Snapshot(
            Entry("active", createdDay: 24),
            Entry("done", createdDay: 23, completedDay: 25));

        var layout = TodoBoardActivityCalendarLayout.Build(
            snapshot,
            new DateOnly(2026, 8, 24),
            weekCount: 1,
            visibleLaneCount: 3);

        Assert.Equal(
            ["active", "done"],
            layout.EntriesOn(new DateOnly(2026, 8, 24))
                .Select(entry => entry.ItemId)
                .Order());
        Assert.Equal(
            ["active", "done"],
            layout.EntriesOn(new DateOnly(2026, 8, 25))
                .Select(entry => entry.ItemId)
                .Order());
        Assert.Empty(layout.EntriesOn(new DateOnly(2026, 8, 26)));
    }

    [Fact]
    public void Build_splits_cross_week_and_month_spans_without_losing_identity()
    {
        var snapshot = Snapshot(Entry("long", createdDay: 28, completedDay: 3, completedMonth: 9));

        var layout = TodoBoardActivityCalendarLayout.Build(
            snapshot,
            new DateOnly(2026, 8, 24),
            weekCount: 2,
            visibleLaneCount: 3);

        Assert.Collection(
            layout.Segments,
            first =>
            {
                Assert.Equal("long", first.Entry.ItemId);
                Assert.Equal(0, first.WeekIndex);
                Assert.Equal(4, first.StartColumn);
                Assert.Equal(6, first.EndColumn);
                Assert.False(first.ContinuesBefore);
                Assert.True(first.ContinuesAfter);
            },
            second =>
            {
                Assert.Equal("long", second.Entry.ItemId);
                Assert.Equal(1, second.WeekIndex);
                Assert.Equal(0, second.StartColumn);
                Assert.Equal(3, second.EndColumn);
                Assert.True(second.ContinuesBefore);
                Assert.False(second.ContinuesAfter);
                Assert.Equal(layout.Segments[0].Lane, second.Lane);
                Assert.Same(layout.Segments[0].Entry, second.Entry);
            });
    }

    [Fact]
    public void Build_assigns_deterministic_lanes_and_reports_exact_hidden_entries_per_day()
    {
        var entries = new[]
        {
            Entry("a", createdDay: 24, completedDay: 26),
            Entry("b", createdDay: 24, completedDay: 25),
            Entry("c", createdDay: 24, completedDay: 24),
            Entry("d", createdDay: 24, completedDay: 24)
        };
        var first = TodoBoardActivityCalendarLayout.Build(
            Snapshot(entries),
            new DateOnly(2026, 8, 24),
            weekCount: 1,
            visibleLaneCount: 2);
        var second = TodoBoardActivityCalendarLayout.Build(
            Snapshot(entries.Reverse().ToArray()),
            new DateOnly(2026, 8, 24),
            weekCount: 1,
            visibleLaneCount: 2);

        Assert.Equal(
            first.Segments.Select(segment => (segment.Entry.ItemId, segment.Lane)),
            second.Segments.Select(segment => (segment.Entry.ItemId, segment.Lane)));
        var overflow = Assert.Single(
            first.OverflowDays,
            day => day.Date == new DateOnly(2026, 8, 24));
        Assert.Equal(["c", "d"], overflow.HiddenEntries.Select(entry => entry.ItemId));
        Assert.DoesNotContain(
            first.OverflowDays,
            day => day.Date == new DateOnly(2026, 8, 26));
    }

    [Fact]
    public void Build_compacts_lanes_at_week_boundaries_after_conflicts_end()
    {
        var snapshot = Snapshot(
            Entry("first", createdDay: 24, completedDay: 30),
            Entry("second", createdDay: 24, completedDay: 30),
            Entry("third", createdDay: 24, completedDay: 30),
            Entry("continuing", createdDay: 28, completedDay: 3, completedMonth: 9));

        var layout = TodoBoardActivityCalendarLayout.Build(
            snapshot,
            new DateOnly(2026, 8, 24),
            weekCount: 2,
            visibleLaneCount: 2);

        var continuingSegments = layout.Segments
            .Where(segment => segment.Entry.ItemId == "continuing")
            .OrderBy(segment => segment.WeekIndex)
            .ToList();
        Assert.Equal(3, continuingSegments[0].Lane);
        Assert.False(continuingSegments[0].IsVisible);
        Assert.Equal(0, continuingSegments[1].Lane);
        Assert.True(continuingSegments[1].IsVisible);
        Assert.DoesNotContain(
            layout.OverflowDays,
            day => day.Date == new DateOnly(2026, 8, 31));
    }

    [Fact]
    public void Build_uses_shared_query_results_but_ignores_table_sort_rules()
    {
        var paper = new PaperData
        {
            Id = "paper",
            Type = PaperTypes.Todo,
            Items =
            [
                Item("include", "match later", createdDay: 25),
                Item("exclude", "other", createdDay: 24)
            ]
        };
        var ascending = TodoBoardProjection.Build(
            [paper],
            _ => "Tasks",
            Query() with
            {
                SearchText = "match",
                SortRules = [new TodoBoardSortRule(TodoBoardSortFields.Task, false)]
            });
        var descending = TodoBoardProjection.Build(
            [paper],
            _ => "Tasks",
            Query() with
            {
                SearchText = "match",
                SortRules = [new TodoBoardSortRule(TodoBoardSortFields.Task, true)]
            });

        var first = TodoBoardActivityCalendarLayout.Build(
            ascending,
            new DateOnly(2026, 8, 24),
            weekCount: 1,
            visibleLaneCount: 3);
        var second = TodoBoardActivityCalendarLayout.Build(
            descending,
            new DateOnly(2026, 8, 24),
            weekCount: 1,
            visibleLaneCount: 3);

        Assert.Equal(["include"], first.Segments.Select(segment => segment.Entry.ItemId).Distinct());
        Assert.Equal(
            first.Segments.Select(segment => (segment.Entry.ItemId, segment.Lane)),
            second.Segments.Select(segment => (segment.Entry.ItemId, segment.Lane)));
    }

    private static TodoBoardSnapshot Snapshot(params TodoBoardEntry[] entries)
    {
        var paper = new PaperData
        {
            Id = "paper",
            Type = PaperTypes.Todo,
            Items = entries.Select(entry => Item(
                entry.ItemId,
                entry.Text,
                DateOnly.FromDateTime(entry.CreatedAt.UtcDateTime).Day,
                entry.CompletedAt.HasValue
                    ? DateOnly.FromDateTime(entry.CompletedAt.Value.UtcDateTime).Day
                    : null,
                entry.CompletedAt.HasValue
                    ? DateOnly.FromDateTime(entry.CompletedAt.Value.UtcDateTime).Month
                    : 8)).ToList()
        };
        return TodoBoardProjection.Build([paper], _ => "Tasks", Query());
    }

    private static TodoBoardEntry Entry(
        string id,
        int createdDay,
        int? completedDay = null,
        int completedMonth = 8)
    {
        var item = Item(id, id, createdDay, completedDay, completedMonth);
        return new TodoBoardEntry(
            "paper",
            item.Id,
            "Tasks",
            item.Text,
            "",
            item.Done,
            item.CreatedAt,
            item.CompletedAt,
            null,
            null,
            0,
            0,
            item.Done ? "Done" : "Active",
            item.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            item.CompletedAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            null,
            null);
    }

    private static PaperItem Item(
        string id,
        string text,
        int createdDay,
        int? completedDay = null,
        int completedMonth = 8) => new()
        {
            Id = id,
            Text = text,
            Done = completedDay.HasValue,
            CreatedAt = new DateTimeOffset(2026, 8, createdDay, 12, 0, 0, TimeSpan.Zero),
            CompletedAt = completedDay.HasValue
                ? new DateTimeOffset(2026, completedMonth, completedDay.Value, 12, 0, 0, TimeSpan.Zero)
                : null
        };

    private static TodoBoardQueryContext Query() => new(
        SearchText: "",
        Sort: TodoBoardSorts.Default,
        Today: new DateOnly(2026, 8, 25),
        ComparisonCulture: CultureInfo.InvariantCulture,
        DisplayCulture: CultureInfo.InvariantCulture,
        TimeZone: TimeZoneInfo.Utc,
        PendingStatusText: "Active",
        CompletedStatusText: "Done");
}
