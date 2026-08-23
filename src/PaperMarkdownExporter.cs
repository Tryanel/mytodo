using System.Globalization;
using System.Text;

namespace PaperTodo;

internal static class PaperMarkdownExporter
{
    public static string Build(
        PaperData paper,
        string title,
        string? currentNoteContent = null)
    {
        var markdown = new StringBuilder();
        markdown.Append("# ").AppendLine(EscapeInline(title));

        if (paper.Type == PaperTypes.Todo)
        {
            AppendTodos(markdown, paper);
        }
        else
        {
            AppendPaperContent(markdown, paper, currentNoteContent);
        }

        return markdown.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string BuildBoard(
        string title,
        IEnumerable<(PaperData Paper, string Title)> sources)
    {
        var markdown = new StringBuilder();
        markdown.Append("# ").AppendLine(EscapeInline(title));

        var hasTasks = false;
        foreach (var source in sources)
        {
            var items = source.Paper.Items
                .Where(item => !TodoRules.IsPlaceholder(item))
                .OrderBy(item => item.Order)
                .ToList();
            if (items.Count == 0)
            {
                continue;
            }

            hasTasks = true;
            markdown.AppendLine().Append("## ")
                .AppendLine(EscapeInline(source.Title));
            AppendTodoItems(markdown, items);
        }

        if (!hasTasks)
        {
            markdown.AppendLine().AppendLine(Strings.Get("TodoBoardEmpty"));
        }

        return markdown.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendTodos(StringBuilder markdown, PaperData paper)
    {
        markdown.AppendLine().Append("## ")
            .AppendLine(Strings.Get("ExportMarkdownTasks"));

        AppendTodoItems(
            markdown,
            paper.Items
            .Where(item => !TodoRules.IsPlaceholder(item))
            .OrderBy(item => item.Order));
    }

    private static void AppendTodoItems(
        StringBuilder markdown,
        IEnumerable<PaperItem> items)
    {
        foreach (var item in items)
        {
            var text = string.Join(
                " ",
                (item.Text ?? "").Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(text))
            {
                text = Strings.Get("TodoBoardTask");
            }

            markdown.Append("- [")
                .Append(item.Done ? 'x' : ' ')
                .Append("] ")
                .AppendLine(EscapeInline(text.Trim()));
            markdown.Append("  - ")
                .Append(Strings.Get("ExportMarkdownCreated"))
                .Append(": ")
                .AppendLine(FormatTimestamp(item.CreatedAt));
            markdown.Append("  - ")
                .Append(Strings.Get("ExportMarkdownCompleted"))
                .Append(": ")
                .AppendLine(item.CompletedAt.HasValue
                    ? FormatTimestamp(item.CompletedAt.Value)
                    : Strings.Get("ExportMarkdownNotCompleted"));

            if (!string.IsNullOrWhiteSpace(item.Note))
            {
                markdown.Append("  - ")
                    .Append(Strings.Get("ExportMarkdownNote"))
                    .AppendLine(":");
                foreach (var line in NormalizeLines(item.Note))
                {
                    markdown.Append("    > ").AppendLine(line);
                }
            }
        }
    }

    private static void AppendPaperContent(
        StringBuilder markdown,
        PaperData paper,
        string? currentNoteContent)
    {
        markdown.AppendLine().Append("## ")
            .AppendLine(Strings.Get("ExportMarkdownPaperContent"));

        var content = currentNoteContent ?? paper.Content ?? "";
        if (string.IsNullOrWhiteSpace(content) &&
            !string.IsNullOrWhiteSpace(paper.BodyCapsuleText))
        {
            content = paper.BodyCapsuleText;
        }

        markdown.AppendLine().AppendLine(content.TrimEnd());
    }

    private static IEnumerable<string> NormalizeLines(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
        {
            yield return line.Replace(">", "\\>", StringComparison.Ordinal);
        }
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToLocalTime().ToString(
            "yyyy-MM-dd HH:mm:ss zzz",
            CultureInfo.InvariantCulture);

    private static string EscapeInline(string value) =>
        (value ?? "")
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("#", "\\#", StringComparison.Ordinal);
}
