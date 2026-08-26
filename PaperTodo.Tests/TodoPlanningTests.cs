namespace PaperTodo.Tests;

public sealed class TodoPlanningTests
{
    public static TheoryData<DateOnly?, DateOnly?> ValidPlanningDates => new()
    {
        { null, null },
        { new DateOnly(2026, 9, 1), null },
        { null, new DateOnly(2026, 9, 30) },
        { new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30) },
        { new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 15) }
    };

    [Theory]
    [MemberData(nameof(ValidPlanningDates))]
    public void Task_accepts_each_supported_planning_date_combination(
        DateOnly? plannedStartDate,
        DateOnly? dueDate)
    {
        var item = new PaperItem { Text = "Plan release" };

        var result = item.SetPlanningDates(plannedStartDate, dueDate);

        Assert.NotEqual(TodoPlanningUpdateResult.InvalidRange, result);
        Assert.Equal(plannedStartDate, item.PlannedStartDate);
        Assert.Equal(dueDate, item.DueDate);
    }

    [Fact]
    public void Task_rejects_a_reversed_range_without_changing_existing_planning_dates()
    {
        var item = new PaperItem { Text = "Plan release" };
        item.SetPlanningDates(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30));

        var result = item.SetPlanningDates(
            new DateOnly(2026, 10, 2),
            new DateOnly(2026, 10, 1));

        Assert.Equal(TodoPlanningUpdateResult.InvalidRange, result);
        Assert.Equal(new DateOnly(2026, 9, 1), item.PlannedStartDate);
        Assert.Equal(new DateOnly(2026, 9, 30), item.DueDate);
    }

    [Fact]
    public void Task_can_clear_both_planning_dates()
    {
        var item = new PaperItem { Text = "Plan release" };
        item.SetPlanningDates(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30));

        var result = item.SetPlanningDates(null, null);

        Assert.Equal(TodoPlanningUpdateResult.Updated, result);
        Assert.Null(item.PlannedStartDate);
        Assert.Null(item.DueDate);
    }

    [Fact]
    public void Planning_dates_round_trip_through_task_undo_and_redo()
    {
        var item = new PaperItem { Id = "task", Text = "Plan release" };
        item.SetPlanningDates(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30));
        var history = new TodoUndoHistory(maxDepth: 10);
        history.Record([item]);
        item.SetPlanningDates(
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31));

        var undone = Assert.IsType<TodoHistoryTransition>(history.Undo([item]));
        var undoneItem = Assert.Single(undone.Items);
        Assert.Equal(new DateOnly(2026, 9, 1), undoneItem.PlannedStartDate);
        Assert.Equal(new DateOnly(2026, 9, 30), undoneItem.DueDate);

        var redone = Assert.IsType<TodoHistoryTransition>(history.Redo(undone.Items));
        var redoneItem = Assert.Single(redone.Items);
        Assert.Equal(new DateOnly(2026, 10, 1), redoneItem.PlannedStartDate);
        Assert.Equal(new DateOnly(2026, 10, 31), redoneItem.DueDate);
    }

    [Fact]
    public void Planning_dates_round_trip_through_the_core_state_protocol()
    {
        var item = new PaperItem { Text = "Plan release" };
        item.SetPlanningDates(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30));
        var state = new AppState
        {
            Papers =
            [
                new PaperData
                {
                    Type = PaperTypes.Todo,
                    Items = [item]
                }
            ]
        };
        var store = new StateStore();

        var json = store.SerializeState(state);
        var restored = store.DeserializeState(json);

        Assert.Contains("\"plannedStartDate\": \"2026-09-01\"", json);
        Assert.Contains("\"dueDate\": \"2026-09-30\"", json);
        var restoredItem = Assert.Single(Assert.Single(restored.Papers).Items);
        Assert.Equal(new DateOnly(2026, 9, 1), restoredItem.PlannedStartDate);
        Assert.Equal(new DateOnly(2026, 9, 30), restoredItem.DueDate);
    }

    [Fact]
    public void Completion_restoration_text_and_note_edits_preserve_planning_dates()
    {
        var item = new PaperItem { Text = "Plan release", Note = "Initial" };
        item.SetPlanningDates(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30));

        item.SetDone(true, new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));
        item.Text = "Plan stable release";
        item.Note = "Updated";
        item.SetDone(false);

        Assert.Equal(new DateOnly(2026, 9, 1), item.PlannedStartDate);
        Assert.Equal(new DateOnly(2026, 9, 30), item.DueDate);
    }

    [Fact]
    public void State_without_planning_fields_loads_as_an_unscheduled_task()
    {
        var state = new AppState
        {
            Papers =
            [
                new PaperData
                {
                    Type = PaperTypes.Todo,
                    Items = [new PaperItem { Text = "Legacy task" }]
                }
            ]
        };
        var store = new StateStore();

        var json = store.SerializeState(state);
        var restored = store.DeserializeState(json);

        Assert.DoesNotContain("plannedStartDate", json);
        Assert.DoesNotContain("dueDate", json);
        var restoredItem = Assert.Single(Assert.Single(restored.Papers).Items);
        Assert.Null(restoredItem.PlannedStartDate);
        Assert.Null(restoredItem.DueDate);
    }

    [Fact]
    public void Placeholder_cannot_be_materialized_by_planning_dates_alone()
    {
        var item = new PaperItem();
        var result = item.SetPlanningDates(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30));

        Assert.Equal(TodoPlanningUpdateResult.NotTask, result);
        Assert.Null(item.PlannedStartDate);
        Assert.Null(item.DueDate);
    }

    [Fact]
    public void Planning_dates_retain_an_existing_task_after_its_text_is_cleared()
    {
        var item = new PaperItem { Text = "Write release notes" };
        item.SetPlanningDates(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30));
        item.Text = "";
        var paper = new PaperData
        {
            Type = PaperTypes.Todo,
            Items = [item]
        };

        var snapshot = TodoBoardProjection.Build(
            [paper],
            _ => "Release",
            TodoBoardProjectionTestQuery());

        Assert.Equal(item.Id, Assert.Single(snapshot.AllEntries).ItemId);
    }

    private static TodoBoardQueryContext TodoBoardProjectionTestQuery() => new(
        SearchText: "",
        Sort: TodoBoardSorts.Default,
        Today: new DateOnly(2026, 9, 1),
        ComparisonCulture: System.Globalization.CultureInfo.InvariantCulture,
        DisplayCulture: System.Globalization.CultureInfo.InvariantCulture,
        TimeZone: TimeZoneInfo.Utc,
        PendingStatusText: "Active",
        CompletedStatusText: "Done");
}
