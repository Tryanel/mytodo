namespace PaperTodo.Tests;

public sealed class TodoBoardQueryPersistenceTests
{
    [Fact]
    public void Board_view_filters_and_multi_sort_round_trip_without_search_text()
    {
        var board = new PaperData
        {
            Id = "board",
            Type = PaperTypes.Board,
            BoardView = TodoBoardViews.Calendar,
            BoardFilters = new TodoBoardFilterState
            {
                Statuses = [TodoBoardFilterStatuses.Pending],
                PaperIds = ["paper-a", "paper-b"],
                CreatedFrom = new DateOnly(2026, 8, 1),
                CompletedTo = new DateOnly(2026, 8, 31),
                PlannedFrom = new DateOnly(2026, 9, 1),
                PlannedTo = new DateOnly(2026, 9, 30),
                Note = TodoBoardNoteFilters.WithoutNote
            },
            BoardSortRules =
            [
                new TodoBoardSortRule(TodoBoardSortFields.Status, false),
                new TodoBoardSortRule(TodoBoardSortFields.Due, true)
            ]
        };
        var state = new AppState { Papers = [board] };
        var store = new StateStore();

        var json = store.SerializeState(state);
        var restored = Assert.Single(store.DeserializeState(json).Papers);

        Assert.DoesNotContain("search", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TodoBoardViews.Calendar, restored.BoardView);
        Assert.Equal(
            [TodoBoardFilterStatuses.Pending],
            restored.BoardFilters!.Statuses);
        Assert.Equal(["paper-a", "paper-b"], restored.BoardFilters.PaperIds);
        Assert.Equal(new DateOnly(2026, 8, 1), restored.BoardFilters.CreatedFrom);
        Assert.Equal(new DateOnly(2026, 8, 31), restored.BoardFilters.CompletedTo);
        Assert.Equal(new DateOnly(2026, 9, 1), restored.BoardFilters.PlannedFrom);
        Assert.Equal(new DateOnly(2026, 9, 30), restored.BoardFilters.PlannedTo);
        Assert.Equal(TodoBoardNoteFilters.WithoutNote, restored.BoardFilters.Note);
        Assert.Equal(board.BoardSortRules, restored.BoardSortRules);
    }

    [Fact]
    public void Invalid_persisted_query_preferences_are_normalized_on_load()
    {
        const string json = """
            {
              "papers": [
                {
                  "id": "board",
                  "type": "board",
                  "boardView": "unknown",
                  "boardFilters": {
                    "statuses": ["pending", "unknown", "pending"],
                    "paperIds": ["", "paper-a", "paper-a"],
                    "createdFrom": "2026-08-31",
                    "createdTo": "2026-08-01",
                    "note": "unknown"
                  },
                  "boardSortRules": [
                    { "field": "unknown", "descending": false },
                    { "field": "due", "descending": true },
                    { "field": "due", "descending": false }
                  ]
                }
              ]
            }
            """;

        var board = Assert.Single(new StateStore().DeserializeState(json).Papers);

        Assert.Equal(TodoBoardViews.Table, board.BoardView);
        Assert.Equal(
            [TodoBoardFilterStatuses.Pending],
            board.BoardFilters!.Statuses);
        Assert.Equal(["paper-a"], board.BoardFilters.PaperIds);
        Assert.Equal(new DateOnly(2026, 8, 1), board.BoardFilters.CreatedFrom);
        Assert.Equal(new DateOnly(2026, 8, 31), board.BoardFilters.CreatedTo);
        Assert.Equal(TodoBoardNoteFilters.Any, board.BoardFilters.Note);
        Assert.Equal(
            [new TodoBoardSortRule(TodoBoardSortFields.Due, true)],
            board.BoardSortRules);
    }

    [Fact]
    public void Legacy_single_sort_is_migrated_to_the_first_multi_sort_rule()
    {
        const string json = """
            {
              "papers": [
                {
                  "id": "board",
                  "type": "board",
                  "boardSort": "completed-desc"
                }
              ]
            }
            """;

        var board = Assert.Single(new StateStore().DeserializeState(json).Papers);

        Assert.Equal(
            [new TodoBoardSortRule(TodoBoardSortFields.Completed, true)],
            board.BoardSortRules);
    }
}
