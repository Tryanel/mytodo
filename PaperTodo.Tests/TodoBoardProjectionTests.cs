using System.Globalization;

namespace PaperTodo.Tests;

public sealed class TodoBoardProjectionTests
{
    [Fact]
    public void Build_collects_meaningful_items_from_todo_papers_in_authoritative_order()
    {
        var papers = new List<PaperData>
        {
            new()
            {
                Id = "first-paper",
                Type = PaperTypes.Todo,
                Title = "Stored title",
                Items =
                [
                    new PaperItem { Id = "text-task", Text = "Write tests", Order = 3 },
                    new PaperItem { Id = "placeholder", Text = "   ", Order = 4 },
                    new PaperItem { Id = "noted-task", Note = "Keep this", Order = 5 }
                ]
            },
            new()
            {
                Id = "note-paper",
                Type = PaperTypes.Note,
                Items = [new PaperItem { Id = "ignored", Text = "Not a task" }]
            },
            new()
            {
                Id = "second-paper",
                Type = PaperTypes.Todo,
                Items = [new PaperItem { Id = "last-task", Text = "Ship it", Order = 1 }]
            }
        };

        var snapshot = TodoBoardProjection.Build(
            papers,
            paper => $"display:{paper.Id}",
            Query());

        Assert.Collection(
            snapshot.AllEntries,
            entry =>
            {
                Assert.Equal("text-task", entry.ItemId);
                Assert.Equal("display:first-paper", entry.PaperTitle);
                Assert.Equal(0, entry.PaperOrder);
                Assert.Equal(3, entry.ItemOrder);
            },
            entry => Assert.Equal("noted-task", entry.ItemId),
            entry =>
            {
                Assert.Equal("last-task", entry.ItemId);
                Assert.Equal(2, entry.PaperOrder);
            });
    }

    [Theory]
    [InlineData("WRITE TESTS")]
    [InlineData("decision note")]
    [InlineData("display:paper")]
    [InlineData("进行中")]
    [InlineData("2026-08-25 16:30")]
    [InlineData("2026-08-26 17:45")]
    public void Table_entries_search_current_task_fields_with_explicit_locale_and_timezone(
        string searchText)
    {
        var paper = new PaperData
        {
            Id = "paper",
            Type = PaperTypes.Todo,
            Items =
            [
                new PaperItem
                {
                    Id = "match",
                    Text = "Write tests",
                    Note = "Decision note",
                    CreatedAt = new DateTimeOffset(2026, 8, 25, 8, 30, 0, TimeSpan.Zero),
                    CompletedAt = new DateTimeOffset(2026, 8, 26, 9, 45, 0, TimeSpan.Zero)
                }
            ]
        };
        var otherPaper = new PaperData
        {
            Id = "other-paper",
            Type = PaperTypes.Todo,
            Items = [new PaperItem { Id = "other", Text = "Unrelated", Done = true }]
        };
        var query = Query() with
        {
            SearchText = searchText,
            TimeZone = TimeZoneInfo.CreateCustomTimeZone(
                "Test UTC+8",
                TimeSpan.FromHours(8),
                "Test UTC+8",
                "Test UTC+8"),
            PendingStatusText = "进行中"
        };

        var snapshot = TodoBoardProjection.Build(
            [paper, otherPaper],
            source => $"display:{source.Id}",
            query);

        Assert.Equal("match", Assert.Single(snapshot.TableEntries).ItemId);
    }

    [Theory]
    [InlineData(TodoBoardSorts.Default, "a,c,d,b")]
    [InlineData(TodoBoardSorts.TaskAscending, "b,d,c,a")]
    [InlineData(TodoBoardSorts.TaskDescending, "a,c,d,b")]
    [InlineData(TodoBoardSorts.StatusAscending, "a,c,d,b")]
    [InlineData(TodoBoardSorts.StatusDescending, "d,b,a,c")]
    [InlineData(TodoBoardSorts.PaperAscending, "d,c,b,a")]
    [InlineData(TodoBoardSorts.PaperDescending, "b,a,d,c")]
    [InlineData(TodoBoardSorts.CreatedAscending, "c,a,b,d")]
    [InlineData(TodoBoardSorts.CreatedDescending, "d,b,a,c")]
    [InlineData(TodoBoardSorts.CompletedAscending, "c,d,b,a")]
    [InlineData(TodoBoardSorts.CompletedDescending, "b,d,c,a")]
    [InlineData(TodoBoardSorts.NoteAscending, "c,b,a,d")]
    [InlineData(TodoBoardSorts.NoteDescending, "b,c,a,d")]
    public void Table_entries_apply_each_supported_sort_with_stable_tie_breakers(
        string sort,
        string expectedIds)
    {
        var papers = new List<PaperData>
        {
            new()
            {
                Id = "beta-paper",
                Type = PaperTypes.Todo,
                Title = "Beta",
                Items =
                [
                    Item("a", "zeta", false, 2, 2),
                    Item("b", "Alpha", true, 3, 1, 4, "Zulu")
                ]
            },
            new()
            {
                Id = "alpha-paper",
                Type = PaperTypes.Todo,
                Title = "alpha",
                Items =
                [
                    Item("c", "middle", false, 1, 3, 2, "alpha"),
                    Item("d", "Beta", true, 4, 0, 3)
                ]
            }
        };

        var snapshot = TodoBoardProjection.Build(
            papers,
            paper => paper.Title,
            Query() with { Sort = sort });

        Assert.Equal(
            expectedIds.Split(','),
            snapshot.TableEntries.Select(entry => entry.ItemId));
    }

    [Fact]
    public void Activity_entries_use_injected_today_timezone_and_clamped_inclusive_spans()
    {
        var utcPlusEight = TimeZoneInfo.CreateCustomTimeZone(
            "Activity UTC+8",
            TimeSpan.FromHours(8),
            "Activity UTC+8",
            "Activity UTC+8");
        var paper = new PaperData
        {
            Type = PaperTypes.Todo,
            Items =
            [
                new PaperItem
                {
                    Id = "active",
                    Text = "Active",
                    CreatedAt = new DateTimeOffset(2026, 8, 23, 18, 0, 0, TimeSpan.Zero)
                },
                new PaperItem
                {
                    Id = "finished",
                    Text = "Finished",
                    Done = true,
                    CreatedAt = new DateTimeOffset(2026, 8, 22, 18, 0, 0, TimeSpan.Zero),
                    CompletedAt = new DateTimeOffset(2026, 8, 24, 1, 0, 0, TimeSpan.Zero)
                },
                new PaperItem
                {
                    Id = "clamped",
                    Text = "Clamped",
                    Done = true,
                    CreatedAt = new DateTimeOffset(2026, 8, 24, 20, 0, 0, TimeSpan.Zero),
                    CompletedAt = new DateTimeOffset(2026, 8, 23, 20, 0, 0, TimeSpan.Zero)
                }
            ]
        };
        var snapshot = TodoBoardProjection.Build(
            [paper],
            _ => "Tasks",
            Query() with
            {
                Today = new DateOnly(2026, 8, 25),
                TimeZone = utcPlusEight
            });

        Assert.Equal(
            ["active", "finished"],
            snapshot.ActivityEntriesOn(new DateOnly(2026, 8, 24))
                .Select(entry => entry.ItemId));
        Assert.Equal(
            ["active", "clamped"],
            snapshot.ActivityEntriesOn(new DateOnly(2026, 8, 25))
                .Select(entry => entry.ItemId));
        Assert.Empty(snapshot.ActivityEntriesOn(new DateOnly(2026, 8, 26)));
    }

    private static TodoBoardQueryContext Query() => new(
        SearchText: "",
        Sort: TodoBoardSorts.Default,
        Today: new DateOnly(2026, 8, 25),
        Culture: CultureInfo.InvariantCulture,
        TimeZone: TimeZoneInfo.Utc,
        PendingStatusText: "Active",
        CompletedStatusText: "Done");

    private static PaperItem Item(
        string id,
        string text,
        bool done,
        int createdDay,
        int order,
        int? completedDay = null,
        string note = "") => new()
        {
            Id = id,
            Text = text,
            Note = note,
            Done = done,
            CreatedAt = new DateTimeOffset(2026, 8, createdDay, 12, 0, 0, TimeSpan.Zero),
            CompletedAt = completedDay.HasValue
                ? new DateTimeOffset(2026, 8, completedDay.Value, 12, 0, 0, TimeSpan.Zero)
                : null,
            Order = order
        };
}
