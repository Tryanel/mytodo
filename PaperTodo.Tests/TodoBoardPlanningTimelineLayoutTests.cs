using System.Globalization;

namespace PaperTodo.Tests;

public sealed class TodoBoardPlanningTimelineLayoutTests
{
    [Fact]
    public void Build_week_window_clips_spans_and_keeps_single_dates_as_markers()
    {
        var snapshot = Snapshot(
            Planned("span", new DateOnly(2026, 8, 23), new DateOnly(2026, 9, 1)),
            Planned("start-only", new DateOnly(2026, 8, 26), null),
            Planned("due-only", null, new DateOnly(2026, 8, 30)),
            Planned("unscheduled", null, null));

        var layout = TodoBoardPlanningTimelineLayout.Build(
            snapshot,
            new DateOnly(2026, 8, 26),
            TodoBoardTimelineScales.Week,
            DayOfWeek.Monday);

        Assert.Equal(new DateOnly(2026, 8, 24), layout.WindowStart);
        Assert.Equal(new DateOnly(2026, 8, 30), layout.WindowEnd);
        Assert.Equal(7, layout.DayCount);
        Assert.Collection(
            layout.ScheduledItems,
            span =>
            {
                Assert.Equal("span", span.Entry.ItemId);
                Assert.Equal(TodoBoardPlanningItemKinds.Span, span.Kind);
                Assert.Equal(0, span.StartIndex);
                Assert.Equal(6, span.EndIndex);
                Assert.True(span.ContinuesBefore);
                Assert.True(span.ContinuesAfter);
            },
            marker =>
            {
                Assert.Equal("start-only", marker.Entry.ItemId);
                Assert.Equal(TodoBoardPlanningItemKinds.Marker, marker.Kind);
                Assert.Equal(2, marker.StartIndex);
                Assert.Equal(2, marker.EndIndex);
            },
            marker =>
            {
                Assert.Equal("due-only", marker.Entry.ItemId);
                Assert.Equal(TodoBoardPlanningItemKinds.Marker, marker.Kind);
                Assert.Equal(6, marker.StartIndex);
                Assert.Equal(6, marker.EndIndex);
            });
        Assert.Equal(
            ["unscheduled"],
            layout.UnscheduledEntries.Select(entry => entry.ItemId));
    }

    [Fact]
    public void Build_month_window_uses_the_exact_calendar_month()
    {
        var snapshot = Snapshot(
            Planned("whole-month", new DateOnly(2026, 7, 20), new DateOnly(2026, 9, 4)),
            Planned("same-day-span", new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 31)));

        var layout = TodoBoardPlanningTimelineLayout.Build(
            snapshot,
            new DateOnly(2026, 8, 26),
            TodoBoardTimelineScales.Month,
            DayOfWeek.Sunday);

        Assert.Equal(new DateOnly(2026, 8, 1), layout.WindowStart);
        Assert.Equal(new DateOnly(2026, 8, 31), layout.WindowEnd);
        Assert.Equal(31, layout.DayCount);
        Assert.Equal((0, 30), (
            layout.ScheduledItems[0].StartIndex,
            layout.ScheduledItems[0].EndIndex));
        Assert.Equal(TodoBoardPlanningItemKinds.Span, layout.ScheduledItems[1].Kind);
        Assert.Equal(30, layout.ScheduledItems[1].StartIndex);
    }

    [Fact]
    public void Build_uses_shared_query_results_and_ignores_table_sorting()
    {
        var filteredByNote = Planned(
            "filtered-by-note",
            new DateOnly(2026, 8, 27),
            null,
            "match filtered");
        filteredByNote.Note = "has note";
        var paper = new PaperData
        {
            Id = "paper",
            Type = PaperTypes.Todo,
            Items =
            [
                Planned("included-zeta", new DateOnly(2026, 8, 25), null, "match zeta"),
                Planned("included-alpha", new DateOnly(2026, 8, 26), null, "match alpha"),
                Planned("included-unscheduled", null, null, "match middle"),
                Planned("excluded-by-search", new DateOnly(2026, 8, 26), null, "other"),
                filteredByNote
            ]
        };
        var ascending = TodoBoardProjection.Build(
            [paper],
            _ => "Tasks",
            Query() with
            {
                SearchText = "match",
                Filters = new TodoBoardFilterState
                {
                    Note = TodoBoardNoteFilters.WithoutNote
                },
                SortRules = [new TodoBoardSortRule(TodoBoardSortFields.Task, false)]
            });
        var descending = TodoBoardProjection.Build(
            [paper],
            _ => "Tasks",
            Query() with
            {
                SearchText = "match",
                Filters = new TodoBoardFilterState
                {
                    Note = TodoBoardNoteFilters.WithoutNote
                },
                SortRules = [new TodoBoardSortRule(TodoBoardSortFields.Task, true)]
            });

        var first = TodoBoardPlanningTimelineLayout.Build(
            ascending,
            new DateOnly(2026, 8, 26),
            TodoBoardTimelineScales.Week,
            DayOfWeek.Monday);
        var second = TodoBoardPlanningTimelineLayout.Build(
            descending,
            new DateOnly(2026, 8, 26),
            TodoBoardTimelineScales.Week,
            DayOfWeek.Monday);

        Assert.Equal(
            ["included-zeta", "included-alpha"],
            first.ScheduledItems.Select(item => item.Entry.ItemId));
        Assert.Equal(
            ["included-unscheduled"],
            first.UnscheduledEntries.Select(entry => entry.ItemId));
        Assert.Equal(first.ScheduledItems, second.ScheduledItems);
        Assert.Equal(first.UnscheduledEntries, second.UnscheduledEntries);
    }

    [Fact]
    public void Build_excludes_scheduled_tasks_outside_the_visible_window()
    {
        var layout = TodoBoardPlanningTimelineLayout.Build(
            Snapshot(
                Planned("before", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 2)),
                Planned("after", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2))),
            new DateOnly(2026, 8, 26),
            TodoBoardTimelineScales.Week,
            DayOfWeek.Monday);

        Assert.Empty(layout.ScheduledItems);
        Assert.Empty(layout.UnscheduledEntries);
    }

    [Theory]
    [InlineData("unknown", "week")]
    [InlineData("week", "week")]
    [InlineData("month", "month")]
    public void Scale_normalization_is_stable(string value, string expected)
    {
        Assert.Equal(expected, TodoBoardTimelineScales.Normalize(value));
    }

    private static TodoBoardSnapshot Snapshot(params PaperItem[] items)
    {
        var paper = new PaperData
        {
            Id = "paper",
            Type = PaperTypes.Todo,
            Items = items.ToList()
        };
        return TodoBoardProjection.Build([paper], _ => "Tasks", Query());
    }

    private static PaperItem Planned(
        string id,
        DateOnly? start,
        DateOnly? due,
        string? text = null)
    {
        var item = new PaperItem
        {
            Id = id,
            Text = text ?? id,
            CreatedAt = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero)
        };
        Assert.Equal(
            start.HasValue || due.HasValue
                ? TodoPlanningUpdateResult.Updated
                : TodoPlanningUpdateResult.Unchanged,
            item.SetPlanningDates(start, due));
        return item;
    }

    private static TodoBoardQueryContext Query() => new(
        SearchText: "",
        Sort: TodoBoardSorts.Default,
        Today: new DateOnly(2026, 8, 26),
        ComparisonCulture: CultureInfo.InvariantCulture,
        DisplayCulture: CultureInfo.InvariantCulture,
        TimeZone: TimeZoneInfo.Utc,
        PendingStatusText: "Active",
        CompletedStatusText: "Done");
}
