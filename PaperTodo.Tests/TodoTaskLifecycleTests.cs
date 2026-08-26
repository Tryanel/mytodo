namespace PaperTodo.Tests;

public sealed class TodoTaskLifecycleTests
{
    [Fact]
    public void Text_materializes_a_placeholder_at_the_supplied_time()
    {
        var item = new PaperItem();
        var createdAt = new DateTimeOffset(
            2026,
            8,
            26,
            12,
            30,
            0,
            TimeSpan.FromHours(8));

        Assert.Equal(default, item.CreatedAt);

        item.Text = "Write release notes";
        var materialized = TodoTaskLifecycle.MaterializeIfNeeded(item, createdAt);

        Assert.True(materialized);
        Assert.Equal(createdAt, item.CreatedAt);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("note")]
    [InlineData("reminder")]
    [InlineData("path")]
    [InlineData("paper")]
    public void Each_supported_content_source_materializes_the_same_way(
        string source)
    {
        var item = new PaperItem();
        var createdAt = new DateTimeOffset(
            2026,
            8,
            26,
            13,
            0,
            0,
            TimeSpan.FromHours(8));
        ApplySource(item, source);

        var materialized = TodoTaskLifecycle.MaterializeIfNeeded(item, createdAt);

        Assert.True(materialized);
        Assert.Equal(createdAt, item.CreatedAt);
    }

    private static void ApplySource(PaperItem item, string source)
    {
        switch (source)
        {
            case "text":
                item.Text = "Write release notes";
                break;
            case "note":
                item.Note = "Remember the migration note";
                break;
            case "reminder":
                item.ReminderAt = new DateTimeOffset(
                    2026,
                    8,
                    27,
                    9,
                    0,
                    0,
                    TimeSpan.FromHours(8));
                break;
            case "path":
                item.LinkPath(@"D:\release.md");
                break;
            case "paper":
                item.LinkPaper("linked-paper");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(source));
        }
    }

    [Fact]
    public void Legacy_task_without_creation_time_uses_the_supplied_migration_time()
    {
        const string json = """
            {
              "papers": [
                {
                  "type": "todo",
                  "items": [
                    { "text": "Legacy task" }
                  ]
                }
              ]
            }
            """;
        var migrationTime = new DateTimeOffset(
            2026,
            8,
            26,
            14,
            0,
            0,
            TimeSpan.FromHours(8));
        var store = new StateStore(() => migrationTime);

        var state = store.DeserializeState(json);

        var item = Assert.Single(Assert.Single(state.Papers).Items);
        Assert.Equal(migrationTime, item.CreatedAt);
    }

    [Fact]
    public void Legacy_blank_row_is_restored_as_an_unmaterialized_placeholder()
    {
        const string json = """
            {
              "papers": [
                {
                  "type": "todo",
                  "items": [
                    {
                      "text": "",
                      "createdAt": "2025-01-01T08:00:00+08:00"
                    }
                  ]
                }
              ]
            }
            """;
        var store = new StateStore(() =>
            new DateTimeOffset(
                2026,
                8,
                26,
                14,
                0,
                0,
                TimeSpan.FromHours(8)));

        var state = store.DeserializeState(json);

        var item = Assert.Single(Assert.Single(state.Papers).Items);
        Assert.Equal(default, item.CreatedAt);
        Assert.DoesNotContain("createdAt", store.SerializeState(state));
    }

    [Fact]
    public void Materialized_task_keeps_its_creation_time_across_later_changes()
    {
        var createdAt = new DateTimeOffset(
            2026,
            8,
            26,
            15,
            0,
            0,
            TimeSpan.FromHours(8));
        var item = new PaperItem { Text = "Draft release notes" };
        TodoTaskLifecycle.MaterializeIfNeeded(item, createdAt);

        item.Text = "Publish release notes";
        item.Note = "Include the migration warning";
        item.SetDone(true, createdAt.AddHours(1));
        item.SetDone(true, createdAt.AddHours(2));
        item.SetDone(false);
        item.SetPlanningDates(
            new DateOnly(2026, 8, 27),
            new DateOnly(2026, 8, 28));
        var materializedAgain = TodoTaskLifecycle.MaterializeIfNeeded(
            item,
            createdAt.AddDays(1));

        Assert.False(materializedAgain);
        Assert.Equal(createdAt, item.CreatedAt);
    }

    [Fact]
    public void Replacement_task_gets_a_new_identity_and_creation_time()
    {
        var first = new PaperItem { Text = "Old task" };
        var replacement = new PaperItem { Text = "New task" };
        var firstCreatedAt = new DateTimeOffset(
            2026,
            8,
            26,
            15,
            0,
            0,
            TimeSpan.FromHours(8));
        var replacementCreatedAt = firstCreatedAt.AddHours(2);

        TodoTaskLifecycle.MaterializeIfNeeded(first, firstCreatedAt);
        TodoTaskLifecycle.MaterializeIfNeeded(replacement, replacementCreatedAt);

        Assert.NotEqual(first.Id, replacement.Id);
        Assert.Equal(replacementCreatedAt, replacement.CreatedAt);
    }

    [Fact]
    public void Completion_state_alone_does_not_materialize_a_placeholder()
    {
        var item = new PaperItem();
        item.SetDone(
            true,
            new DateTimeOffset(
                2026,
                8,
                26,
                15,
                0,
                0,
                TimeSpan.FromHours(8)));

        var materialized = TodoTaskLifecycle.MaterializeIfNeeded(
            item,
            new DateTimeOffset(
                2026,
                8,
                26,
                15,
                0,
                0,
                TimeSpan.FromHours(8)));

        Assert.False(materialized);
        Assert.False(item.Done);
        Assert.Null(item.CompletedAt);
        Assert.Equal(default, item.CreatedAt);
    }

    [Fact]
    public void Materialization_rejects_a_missing_creation_time()
    {
        var item = new PaperItem { Text = "Real task" };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TodoTaskLifecycle.MaterializeIfNeeded(item, default));
        Assert.Equal(default, item.CreatedAt);
    }

    [Fact]
    public void Materialized_task_keeps_its_creation_time_after_its_only_text_is_cleared_and_reloaded()
    {
        var createdAt = new DateTimeOffset(
            2026,
            8,
            26,
            16,
            0,
            0,
            TimeSpan.FromHours(8));
        var item = new PaperItem { Text = "Temporary text" };
        TodoTaskLifecycle.MaterializeIfNeeded(item, createdAt);
        item.Text = "";
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
        var store = new StateStore(() => createdAt.AddDays(1));

        var restored = store.DeserializeState(store.SerializeState(state));

        var restoredItem = Assert.Single(Assert.Single(restored.Papers).Items);
        Assert.Equal(createdAt, restoredItem.CreatedAt);
    }

    [Fact]
    public void Legacy_orphaned_reminder_flag_does_not_materialize_a_blank_row()
    {
        const string json = """
            {
              "papers": [
                {
                  "type": "todo",
                  "items": [
                    {
                      "text": "",
                      "createdAt": "2025-01-01T08:00:00+08:00",
                      "reminderTriggered": true
                    }
                  ]
                }
              ]
            }
            """;
        var store = new StateStore(() =>
            new DateTimeOffset(
                2026,
                8,
                26,
                16,
                0,
                0,
                TimeSpan.FromHours(8)));

        var state = store.DeserializeState(json);

        var item = Assert.Single(Assert.Single(state.Papers).Items);
        Assert.False(item.ReminderTriggered);
        Assert.Equal(default, item.CreatedAt);
    }
}
