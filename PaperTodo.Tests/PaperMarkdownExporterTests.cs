namespace PaperTodo.Tests;

public sealed class PaperMarkdownExporterTests
{
    [Fact]
    public void Todo_export_includes_both_planning_fields_for_every_task()
    {
        var planned = Task("planned", "Planned task", 0);
        Assert.Equal(
            TodoPlanningUpdateResult.Updated,
            planned.SetPlanningDates(
                new DateOnly(2026, 9, 1),
                new DateOnly(2026, 9, 30)));
        var paper = new PaperData
        {
            Type = PaperTypes.Todo,
            Items = [planned, Task("unscheduled", "Unscheduled task", 1)]
        };

        var markdown = PaperMarkdownExporter.Build(paper, "Tasks");

        Assert.Equal(2, Occurrences(markdown, Strings.Get("ExportMarkdownPlannedStart")));
        Assert.Equal(2, Occurrences(markdown, Strings.Get("ExportMarkdownDue")));
        Assert.Contains("2026-09-01", markdown, StringComparison.Ordinal);
        Assert.Contains("2026-09-30", markdown, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(markdown, Strings.Get("ExportMarkdownNotSet")));
    }

    [Fact]
    public void Board_export_uses_all_authoritative_todos_grouped_by_owning_paper()
    {
        var first = Task("first", "Matches current query", 0);
        Assert.Equal(
            TodoPlanningUpdateResult.Updated,
            first.SetPlanningDates(new DateOnly(2026, 10, 2), null));
        var second = Task("second", "Filtered out in the Board UI", 1);
        Assert.Equal(
            TodoPlanningUpdateResult.Updated,
            second.SetPlanningDates(null, new DateOnly(2026, 10, 8)));

        var markdown = PaperMarkdownExporter.BuildBoard(
            "Board",
            [
                (new PaperData
                {
                    Id = "paper-a",
                    Type = PaperTypes.Todo,
                    Items = [first]
                }, "Paper A"),
                (new PaperData
                {
                    Id = "paper-b",
                    Type = PaperTypes.Todo,
                    Items = [second]
                }, "Paper B")
            ]);

        Assert.Contains("## Paper A", markdown, StringComparison.Ordinal);
        Assert.Contains("## Paper B", markdown, StringComparison.Ordinal);
        Assert.Contains("Matches current query", markdown, StringComparison.Ordinal);
        Assert.Contains("Filtered out in the Board UI", markdown, StringComparison.Ordinal);
        Assert.Contains("2026-10-02", markdown, StringComparison.Ordinal);
        Assert.Contains("2026-10-08", markdown, StringComparison.Ordinal);
    }

    private static PaperItem Task(string id, string text, int order) => new()
    {
        Id = id,
        Text = text,
        Order = order,
        CreatedAt = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero)
    };

    private static int Occurrences(string value, string text) =>
        value.Split(text, StringSplitOptions.None).Length - 1;
}
