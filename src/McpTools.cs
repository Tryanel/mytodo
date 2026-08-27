using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace PaperTodo;

[McpServerToolType]
internal sealed class McpTools
{
    private readonly McpPipeClient _client;

    public McpTools(McpPipeClient client)
    {
        _client = client;
    }

    [McpServerTool(
        Name = "list_papers",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("List PaperTodo papers and compact metadata. Use this first to discover paper IDs.")]
    public Task<JsonElement> ListPapers(
        [Description("Optional filter: 'todo', 'note', or the read-only global 'board' paper.")] string? type = null,
        CancellationToken cancellationToken = default)
        => _client.InvokeAsync(
            "list_papers",
            new { type },
            cancellationToken);

    [McpServerTool(
        Name = "get_paper",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Read one PaperTodo paper in full, including ordered todos, note content, or board projection metadata.")]
    public Task<JsonElement> GetPaper(
        [Description("Exact paper ID returned by list_papers.")] string paper_id,
        CancellationToken cancellationToken = default)
        => _client.InvokeAsync(
            "get_paper",
            new { paper_id },
            cancellationToken);

    [McpServerTool(
        Name = "create_todo_paper",
        ReadOnly = false,
        Destructive = false,
        OpenWorld = false)]
    [Description("Create a new todo paper. Requires PaperTodo blank/additive writes.")]
    public Task<JsonElement> CreateTodoPaper(
        [Description("Optional paper title.")] string? title = null,
        [Description("Optional ordered todo steps.")] IReadOnlyList<McpTodoInput>? todos = null,
        [Description("Show the new paper immediately.")] bool show = true,
        CancellationToken cancellationToken = default)
        => _client.InvokeAsync(
            "create_todo_paper",
            new { title, todos, show },
            cancellationToken);

    [McpServerTool(
        Name = "create_note",
        ReadOnly = false,
        Destructive = false,
        OpenWorld = false)]
    [Description("Create a new note with optional Markdown content. Requires PaperTodo blank/additive writes.")]
    public Task<JsonElement> CreateNote(
        [Description("Optional paper title.")] string? title = null,
        [Description("Initial note content.")] string content = "",
        [Description("Show the new paper immediately.")] bool show = true,
        CancellationToken cancellationToken = default)
        => _client.InvokeAsync(
            "create_note",
            new { title, content, show },
            cancellationToken);

    [McpServerTool(
        Name = "add_todos",
        ReadOnly = false,
        Destructive = false,
        OpenWorld = false)]
    [Description("Append new todo steps to an existing todo paper. Requires PaperTodo blank/additive writes.")]
    public Task<JsonElement> AddTodos(
        [Description("Exact todo paper ID.")] string paper_id,
        [Description("One or more ordered todo steps.")] IReadOnlyList<McpTodoInput> todos,
        CancellationToken cancellationToken = default)
        => _client.InvokeAsync(
            "add_todos",
            new { paper_id, todos },
            cancellationToken);

    [McpServerTool(
        Name = "update_todo",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Fill or replace todo text or note, change completion state, and/or update planning dates. Filling a blank text field needs additive writes; replacing existing content, state, or planning needs full writes.")]
    public Task<JsonElement> UpdateTodo(
        [Description("Exact todo paper ID.")] string paper_id,
        [Description("Exact todo item ID.")] string todo_id,
        [Description("Replacement text. Omit to keep text unchanged.")] string? text = null,
        [Description("Replacement todo note. Omit to keep it unchanged; use an empty string to clear it.")] string? note = null,
        [Description("Replacement completion state. Omit to keep it unchanged.")] bool? done = null,
        [Description("ISO calendar date (yyyy-MM-dd). Omit to keep the planned start unchanged; use an empty string to clear it.")] string? planned_start_date = null,
        [Description("ISO calendar date (yyyy-MM-dd). Omit to keep the due date unchanged; use an empty string to clear it.")] string? due_date = null,
        CancellationToken cancellationToken = default)
        => _client.InvokeAsync(
            "update_todo",
            OptionalUpdateParameters(
                paper_id,
                todo_id,
                text,
                note,
                done,
                planned_start_date,
                due_date),
            cancellationToken);

    [McpServerTool(
        Name = "set_todo_reminder",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Set, replace, or cancel a lightweight todo reminder. Requires reminders and full writes in PaperTodo.")]
    public Task<JsonElement> SetTodoReminder(
        [Description("Exact todo paper ID.")] string paper_id,
        [Description("Exact todo item ID.")] string todo_id,
        [Description("ISO 8601 future date/time with UTC offset, or null to cancel.")] string? reminder_at,
        CancellationToken cancellationToken = default)
        => _client.InvokeAsync(
            "set_todo_reminder",
            new { paper_id, todo_id, reminder_at },
            cancellationToken);

    [McpServerTool(
        Name = "write_note",
        ReadOnly = false,
        Destructive = true,
        OpenWorld = false)]
    [Description("Fill, append, or replace a note. Fill/append needs additive writes; replacing existing content needs full writes.")]
    public Task<JsonElement> WriteNote(
        [Description("Exact note paper ID.")] string paper_id,
        [Description("Text to fill or append.")] string content,
        [Description("Mode: 'fill_blank', 'append', or 'replace'.")] string mode = "fill_blank",
        CancellationToken cancellationToken = default)
        => _client.InvokeAsync(
            "write_note",
            new { paper_id, content, mode },
            cancellationToken);

    [McpServerTool(
        Name = "delete_paper",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Delete a paper. Requires PaperTodo's direct-delete permission.")]
    public Task<JsonElement> DeletePaper(
        [Description("Exact paper ID.")] string paper_id,
        CancellationToken cancellationToken = default)
        => _client.InvokeAsync(
            "delete_paper",
            new { paper_id },
            cancellationToken);

    [McpServerTool(
        Name = "delete_todo",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Delete a todo. Requires PaperTodo's direct-delete permission.")]
    public Task<JsonElement> DeleteTodo(
        [Description("Exact todo paper ID.")] string paper_id,
        [Description("Exact todo item ID.")] string todo_id,
        CancellationToken cancellationToken = default)
        => _client.InvokeAsync(
            "delete_todo",
            new { paper_id, todo_id },
            cancellationToken);

    private static Dictionary<string, object?> OptionalUpdateParameters(
        string paperId,
        string todoId,
        string? text,
        string? note,
        bool? done,
        string? plannedStartDate,
        string? dueDate)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["paper_id"] = paperId,
            ["todo_id"] = todoId
        };
        if (text != null)
        {
            parameters["text"] = text;
        }
        if (note != null)
        {
            parameters["note"] = note;
        }
        if (done.HasValue)
        {
            parameters["done"] = done.Value;
        }
        if (plannedStartDate != null)
        {
            parameters["planned_start_date"] = plannedStartDate;
        }
        if (dueDate != null)
        {
            parameters["due_date"] = dueDate;
        }
        return parameters;
    }
}

internal sealed record McpTodoInput
{
    [JsonPropertyName("text")]
    [Description("Todo text. Uses the same length limit as PaperTodo's normal editor.")]
    public required string Text { get; init; }

    [JsonPropertyName("note")]
    [Description("Optional multiline todo note.")]
    public string? Note { get; init; }

    [JsonPropertyName("done")]
    [Description("Whether the todo starts completed. Setting true requires PaperTodo full writes.")]
    public bool Done { get; init; }

    [JsonPropertyName("reminder_at")]
    [Description("Optional ISO 8601 future reminder date/time with UTC offset. Requires PaperTodo full writes.")]
    public string? ReminderAt { get; init; }

    [JsonPropertyName("planned_start_date")]
    [Description("Optional ISO calendar date (yyyy-MM-dd). Requires PaperTodo full writes.")]
    public string? PlannedStartDate { get; init; }

    [JsonPropertyName("due_date")]
    [Description("Optional ISO calendar date (yyyy-MM-dd). Requires PaperTodo full writes.")]
    public string? DueDate { get; init; }
}
