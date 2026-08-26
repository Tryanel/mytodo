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

    [Fact]
    public void Table_search_uses_comparison_culture_when_display_culture_differs()
    {
        var paper = new PaperData
        {
            Type = PaperTypes.Todo,
            Items = [new PaperItem { Text = "FILE" }]
        };

        var snapshot = TodoBoardProjection.Build(
            [paper],
            _ => "Tasks",
            Query() with
            {
                SearchText = "file",
                ComparisonCulture = CultureInfo.GetCultureInfo("tr-TR"),
                DisplayCulture = CultureInfo.GetCultureInfo("en-US")
            });

        Assert.Empty(snapshot.TableEntries);
    }

    [Fact]
    public void Search_requires_every_keyword_and_preserves_quoted_phrases()
    {
        var paper = new PaperData
        {
            Type = PaperTypes.Todo,
            Items =
            [
                new PaperItem
                {
                    Id = "full-match",
                    Text = "Write integration tests",
                    Note = "Decision note",
                    CreatedAt = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero)
                },
                new PaperItem
                {
                    Id = "words-only",
                    Text = "Write useful tests",
                    Note = "Decision note",
                    CreatedAt = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero)
                },
                new PaperItem
                {
                    Id = "phrase-only",
                    Text = "Write integration tests",
                    Note = "No conclusion yet",
                    CreatedAt = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero)
                }
            ]
        };

        var snapshot = TodoBoardProjection.Build(
            [paper],
            _ => "Tasks",
            Query() with { SearchText = "  \"integration tests\"   decision  " });

        Assert.Equal(
            ["full-match"],
            snapshot.TableEntries.Select(entry => entry.ItemId));
        Assert.Equal(
            ["full-match"],
            snapshot.ActivityEntriesOn(new DateOnly(2026, 8, 25))
                .Select(entry => entry.ItemId));
    }

    [Fact]
    public void Filters_combine_categories_with_and_and_values_with_or_for_every_view()
    {
        var papers = new[]
        {
            new PaperData
            {
                Id = "paper-a",
                Type = PaperTypes.Todo,
                Items =
                [
                    Item("a-active", "Active A", false, 20, 0),
                    Item("a-done", "Done A", true, 20, 1, 21)
                ]
            },
            new PaperData
            {
                Id = "paper-b",
                Type = PaperTypes.Todo,
                Items = [Item("b-done", "Done B", true, 20, 0, 21)]
            },
            new PaperData
            {
                Id = "paper-c",
                Type = PaperTypes.Todo,
                Items = [Item("c-done", "Done C", true, 20, 0, 21)]
            }
        };
        var filters = new TodoBoardFilterState
        {
            Statuses = [TodoBoardFilterStatuses.Done],
            PaperIds = ["paper-a", "paper-b"]
        };

        var snapshot = TodoBoardProjection.Build(
            papers,
            paper => paper.Id,
            Query() with { Filters = filters });

        Assert.Equal(
            ["a-done", "b-done"],
            snapshot.TableEntries.Select(entry => entry.ItemId));
        Assert.Equal(
            ["a-done", "b-done"],
            snapshot.ActivityEntriesOn(new DateOnly(2026, 8, 21))
                .Select(entry => entry.ItemId));
    }

    [Fact]
    public void Historical_date_filters_use_inclusive_local_date_points()
    {
        var paper = new PaperData
        {
            Type = PaperTypes.Todo,
            Items =
            [
                Item("boundary", "Boundary", true, 20, 0, 23),
                Item("created-before", "Created before", true, 19, 1, 23),
                Item("completed-after", "Completed after", true, 20, 2, 24),
                Item("still-active", "Still active", false, 20, 3)
            ]
        };
        var filters = new TodoBoardFilterState
        {
            CreatedFrom = new DateOnly(2026, 8, 20),
            CreatedTo = new DateOnly(2026, 8, 22),
            CompletedFrom = new DateOnly(2026, 8, 21),
            CompletedTo = new DateOnly(2026, 8, 23)
        };

        var snapshot = TodoBoardProjection.Build(
            [paper],
            _ => "Tasks",
            Query() with { Filters = filters });

        Assert.Equal(
            ["boundary"],
            snapshot.QueryEntries.Select(entry => entry.ItemId));
    }

    [Theory]
    [InlineData(TodoBoardNoteFilters.WithNote, "noted")]
    [InlineData(TodoBoardNoteFilters.WithoutNote, "blank-note,plain")]
    public void Note_filter_distinguishes_meaningful_notes_from_blank_text(
        string noteFilter,
        string expectedIds)
    {
        var paper = new PaperData
        {
            Type = PaperTypes.Todo,
            Items =
            [
                new PaperItem { Id = "noted", Text = "A", Note = "Details" },
                new PaperItem { Id = "blank-note", Text = "B", Note = "  \r\n " },
                new PaperItem { Id = "plain", Text = "C" }
            ]
        };

        var snapshot = TodoBoardProjection.Build(
            [paper],
            _ => "Tasks",
            Query() with
            {
                Filters = new TodoBoardFilterState { Note = noteFilter }
            });

        Assert.Equal(
            expectedIds.Split(','),
            snapshot.QueryEntries.Select(entry => entry.ItemId));
    }

    [Fact]
    public void Planning_filter_uses_inclusive_overlap_and_single_day_ranges()
    {
        var overlapping = PlannedItem(
            "overlapping",
            new DateOnly(2026, 8, 18),
            new DateOnly(2026, 8, 20));
        var startOnly = PlannedItem(
            "start-only",
            new DateOnly(2026, 8, 21),
            null);
        var dueOnly = PlannedItem(
            "due-only",
            null,
            new DateOnly(2026, 8, 22));
        var outside = PlannedItem(
            "outside",
            new DateOnly(2026, 8, 23),
            new DateOnly(2026, 8, 24));
        var unscheduled = new PaperItem { Id = "unscheduled", Text = "Unscheduled" };
        var paper = new PaperData
        {
            Type = PaperTypes.Todo,
            Items = [overlapping, startOnly, dueOnly, outside, unscheduled]
        };

        var snapshot = TodoBoardProjection.Build(
            [paper],
            _ => "Tasks",
            Query() with
            {
                Filters = new TodoBoardFilterState
                {
                    PlannedFrom = new DateOnly(2026, 8, 20),
                    PlannedTo = new DateOnly(2026, 8, 22)
                }
            });

        Assert.Equal(
            ["overlapping", "start-only", "due-only"],
            snapshot.QueryEntries.Select(entry => entry.ItemId));
    }

    [Fact]
    public void Planning_dates_are_exposed_and_searchable_as_visible_text()
    {
        var item = PlannedItem(
            "planned",
            new DateOnly(2026, 9, 10),
            new DateOnly(2026, 9, 18));
        var paper = new PaperData
        {
            Type = PaperTypes.Todo,
            Items = [item]
        };

        var snapshot = TodoBoardProjection.Build(
            [paper],
            _ => "Tasks",
            Query() with { SearchText = "2026-09-18" });

        var entry = Assert.Single(snapshot.QueryEntries);
        Assert.Equal("2026-09-10", entry.PlannedStartText);
        Assert.Equal("2026-09-18", entry.DueText);
    }

    [Fact]
    public void Deleted_paper_filter_values_are_ignored()
    {
        var paper = new PaperData
        {
            Id = "current-paper",
            Type = PaperTypes.Todo,
            Items = [new PaperItem { Id = "visible", Text = "Visible" }]
        };

        var snapshot = TodoBoardProjection.Build(
            [paper],
            source => source.Id,
            Query() with
            {
                Filters = new TodoBoardFilterState
                {
                    PaperIds = ["deleted-paper"]
                }
            });

        Assert.Equal("visible", Assert.Single(snapshot.QueryEntries).ItemId);
    }

    [Fact]
    public void Existing_empty_paper_filter_is_not_treated_as_a_deleted_paper()
    {
        var emptyPaper = new PaperData
        {
            Id = "empty-paper",
            Type = PaperTypes.Todo
        };
        var populatedPaper = new PaperData
        {
            Id = "populated-paper",
            Type = PaperTypes.Todo,
            Items = [new PaperItem { Id = "other", Text = "Other" }]
        };

        var snapshot = TodoBoardProjection.Build(
            [emptyPaper, populatedPaper],
            paper => paper.Id,
            Query() with
            {
                Filters = new TodoBoardFilterState
                {
                    PaperIds = ["empty-paper"]
                }
            });

        Assert.Empty(snapshot.QueryEntries);
    }

    [Fact]
    public void Multi_sort_applies_rules_in_priority_order_with_stable_tie_breakers()
    {
        var a = PlannedItem("a", null, new DateOnly(2026, 8, 22));
        a.Text = "Alpha";
        var b = PlannedItem("b", null, new DateOnly(2026, 8, 21));
        b.Text = "Zeta";
        var c = PlannedItem("c", null, new DateOnly(2026, 8, 21));
        c.Text = "Beta";
        var d = PlannedItem("d", null, new DateOnly(2026, 8, 20));
        d.Text = "Done";
        d.SetDone(true, new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));
        var paper = new PaperData
        {
            Type = PaperTypes.Todo,
            Items = [a, b, c, d]
        };

        var snapshot = TodoBoardProjection.Build(
            [paper],
            _ => "Tasks",
            Query() with
            {
                SortRules =
                [
                    new TodoBoardSortRule(TodoBoardSortFields.Status, Descending: false),
                    new TodoBoardSortRule(TodoBoardSortFields.Due, Descending: false),
                    new TodoBoardSortRule(TodoBoardSortFields.Task, Descending: true)
                ]
            });

        Assert.Equal(
            ["b", "c", "a", "d"],
            snapshot.TableEntries.Select(entry => entry.ItemId));
    }

    [Fact]
    public void Setting_primary_sort_moves_the_column_first_and_toggles_it_in_place()
    {
        IReadOnlyList<TodoBoardSortRule> current =
        [
            new TodoBoardSortRule(TodoBoardSortFields.Paper, Descending: true),
            new TodoBoardSortRule(TodoBoardSortFields.Due, Descending: false)
        ];

        var withTaskFirst = TodoBoardSortRules.SetPrimary(
            current,
            TodoBoardSortFields.Task,
            descendingFirst: false);
        var toggledTask = TodoBoardSortRules.SetPrimary(
            withTaskFirst,
            TodoBoardSortFields.Task,
            descendingFirst: false);
        var dueFirst = TodoBoardSortRules.SetPrimary(
            toggledTask,
            TodoBoardSortFields.Due,
            descendingFirst: false);

        Assert.Equal(
            [
                new TodoBoardSortRule(TodoBoardSortFields.Task, false),
                new TodoBoardSortRule(TodoBoardSortFields.Paper, true),
                new TodoBoardSortRule(TodoBoardSortFields.Due, false)
            ],
            withTaskFirst);
        Assert.True(toggledTask[0].Descending);
        Assert.Equal(
            [TodoBoardSortFields.Due, TodoBoardSortFields.Task, TodoBoardSortFields.Paper],
            dueFirst.Select(rule => rule.Field));
        Assert.False(dueFirst[0].Descending);
    }

    [Theory]
    [InlineData(false, "early,late,unscheduled")]
    [InlineData(true, "late,early,unscheduled")]
    public void Missing_sort_values_stay_last_in_both_directions(
        bool descending,
        string expectedIds)
    {
        var unscheduled = new PaperItem
        {
            Id = "unscheduled",
            Text = "Unscheduled"
        };
        var early = PlannedItem("early", null, new DateOnly(2026, 8, 20));
        var late = PlannedItem("late", null, new DateOnly(2026, 8, 22));
        var paper = new PaperData
        {
            Type = PaperTypes.Todo,
            Items = [unscheduled, early, late]
        };

        var snapshot = TodoBoardProjection.Build(
            [paper],
            _ => "Tasks",
            Query() with
            {
                SortRules =
                [new TodoBoardSortRule(TodoBoardSortFields.Due, descending)]
            });

        Assert.Equal(
            expectedIds.Split(','),
            snapshot.TableEntries.Select(entry => entry.ItemId));
    }

    [Fact]
    public void Build_exposes_the_status_and_timestamps_used_by_search_and_rendering()
    {
        var paper = new PaperData
        {
            Type = PaperTypes.Todo,
            Items =
            [
                new PaperItem
                {
                    Text = "Finished",
                    Done = true,
                    CreatedAt = new DateTimeOffset(2026, 8, 25, 8, 30, 0, TimeSpan.Zero),
                    CompletedAt = new DateTimeOffset(2026, 8, 26, 9, 45, 0, TimeSpan.Zero)
                }
            ]
        };
        var snapshot = TodoBoardProjection.Build(
            [paper],
            _ => "Tasks",
            Query() with
            {
                TimeZone = TimeZoneInfo.CreateCustomTimeZone(
                    "Display UTC+8",
                    TimeSpan.FromHours(8),
                    "Display UTC+8",
                    "Display UTC+8"),
                CompletedStatusText = "已完成"
            });

        var entry = Assert.Single(snapshot.AllEntries);
        Assert.Equal("已完成", entry.StatusText);
        Assert.Equal("2026-08-25 16:30", entry.CreatedText);
        Assert.Equal("2026-08-26 17:45", entry.CompletedText);
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
        ComparisonCulture: CultureInfo.InvariantCulture,
        DisplayCulture: CultureInfo.InvariantCulture,
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

    private static PaperItem PlannedItem(
        string id,
        DateOnly? plannedStartDate,
        DateOnly? dueDate)
    {
        var item = new PaperItem { Id = id, Text = id };
        Assert.Equal(
            TodoPlanningUpdateResult.Updated,
            item.SetPlanningDates(plannedStartDate, dueDate));
        return item;
    }
}
