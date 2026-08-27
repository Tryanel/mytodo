using System.Collections.Frozen;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Threading;
using PaperTodo.Plugin;

namespace PaperTodo.Tests;

[Collection(TodoBoardWpfSmokeCollection.Name)]
public sealed class ExternalTodoPlanningCommandTests
{
    [Fact]
    public void Planning_contract_uses_api_1_9_while_host_keeps_1_8_compatibility()
    {
        Assert.Equal("1.9", PaperBodyPluginRegistry.SupportedPluginApiVersion);
        Assert.Equal("1.8", PaperBodyPluginRegistry.MinimumPluginApiVersion);
    }

    [Fact]
    public void Shared_commands_cover_plugin_and_mcp_read_write_clear_rollback_and_event_order()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RunScenario();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "External command test timed out.");
        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void RunScenario()
    {
        var stateFile = FileSnapshot.Capture(Path.Combine(AppContext.BaseDirectory, "data.json"));
        var backupFile = FileSnapshot.Capture(Path.Combine(AppContext.BaseDirectory, "data.backup.json"));
        AppController? controller = null;
        IDisposable? subscription = null;
        try
        {
            controller = new AppController(Dispatcher.CurrentDispatcher);
            controller.State.Papers.Clear();
            controller.State.McpEnabled = true;
            controller.State.McpAllowBlankWrites = true;
            controller.State.McpAllowFullWrites = true;

            var item = new PaperItem
            {
                Id = "task",
                Text = "Existing task",
                CreatedAt = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero)
            };
            var paper = new PaperData
            {
                Id = "todo-paper",
                Type = PaperTypes.Todo,
                Title = "External tasks",
                Items = [item]
            };
            controller.State.Papers.Add(paper);
            controller.SaveNow(sync: true);

            var eventHub = controller.PaperBodyPluginEvents;
            var events = new List<TodoChangedEvent>();
            TodoChangedEvent? redactedEvent = null;
            Exception? eventFailure = null;
            subscription = eventHub.Subscribe(
                Guid.NewGuid(),
                "observer",
                new PaperTodoEventFilter
                {
                    Kinds = new[] { PaperTodoEventKind.TodoChanged }
                        .ToFrozenSet(),
                    ExcludeOwnOperations = false
                },
                value =>
                {
                    if (value is not TodoChangedEvent changed)
                    {
                        return;
                    }
                    try
                    {
                        var persisted = new StateStore().DeserializeState(
                            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data.json")));
                        var persistedItem = Assert.Single(Assert.Single(persisted.Papers).Items);
                        Assert.Equal(changed.After.PlannedStartDate, persistedItem.PlannedStartDate);
                        Assert.Equal(changed.After.DueDate, persistedItem.DueDate);
                        events.Add(changed);
                    }
                    catch (Exception ex)
                    {
                        eventFailure = ex;
                    }
                });

            using var observerWithoutRead = new PaperBodyPluginHostApi(
                controller,
                controller.PaperCommands,
                paper.Id,
                "observe-only",
                [PaperTodoPermissionNames.TodosObserve],
                () => true,
                () => true);
            using var redactedSubscription = observerWithoutRead.Subscribe(
                new PaperTodoEventFilter
                {
                    Kinds = new[] { PaperTodoEventKind.TodoChanged }.ToFrozenSet(),
                    ExcludeOwnOperations = false
                },
                value => redactedEvent = value as TodoChangedEvent);

            using var plugin = new PaperBodyPluginHostApi(
                controller,
                controller.PaperCommands,
                paper.Id,
                "planning-test",
                PaperTodoPermissionNames.All,
                () => true,
                () => true);
            plugin.UpdateTodo(new UpdateTodoRequest
            {
                PaperId = paper.Id,
                TodoId = item.Id,
                Planning = new TodoPlanningUpdate(
                    new DateOnly(2026, 9, 1),
                    new DateOnly(2026, 9, 30))
            });

            var pluginSnapshot = Assert.Single(plugin.ListTodos(paper.Id));
            Assert.Equal(new DateOnly(2026, 9, 1), pluginSnapshot.PlannedStartDate);
            Assert.Equal(new DateOnly(2026, 9, 30), pluginSnapshot.DueDate);
            var changedEvent = Assert.Single(events);
            Assert.True(changedEvent.ChangedFields.HasFlag(TodoChangedFields.PlanningDates));
            Assert.Equal(PaperTodoEventOrigin.Plugin, changedEvent.Metadata.Origin);
            Assert.NotNull(redactedEvent);
            Assert.Null(redactedEvent.Before.PlannedStartDate);
            Assert.Null(redactedEvent.Before.DueDate);
            Assert.Null(redactedEvent.After.PlannedStartDate);
            Assert.Null(redactedEvent.After.DueDate);
            Assert.Null(eventFailure);

            var mcp = new McpCommandService(controller, controller.PaperCommands);
            var read = Execute(mcp, "get_paper", new { paper_id = paper.Id });
            var readTodo = Assert.Single(read.GetProperty("todos").EnumerateArray());
            Assert.Equal("2026-09-01", readTodo.GetProperty("planned_start_date").GetString());
            Assert.Equal("2026-09-30", readTodo.GetProperty("due_date").GetString());

            events.Clear();
            var cleared = Execute(mcp, "update_todo", new
            {
                paper_id = paper.Id,
                todo_id = item.Id,
                planned_start_date = "",
                due_date = ""
            });
            Assert.Equal(JsonValueKind.Null, cleared.GetProperty("planned_start_date").ValueKind);
            Assert.Equal(JsonValueKind.Null, cleared.GetProperty("due_date").ValueKind);
            Assert.Null(item.PlannedStartDate);
            Assert.Null(item.DueDate);
            Assert.Single(events);

            Execute(mcp, "update_todo", new
            {
                paper_id = paper.Id,
                todo_id = item.Id,
                planned_start_date = "2026-10-02",
                due_date = "2026-10-08"
            });
            events.Clear();

            var invalid = Assert.Throws<McpApiException>(() => Execute(
                mcp,
                "update_todo",
                new
                {
                    paper_id = paper.Id,
                    todo_id = item.Id,
                    planned_start_date = "2026-10-09"
                }));
            Assert.Equal("invalid_params", invalid.Code);
            Assert.Equal(new DateOnly(2026, 10, 2), item.PlannedStartDate);
            Assert.Equal(new DateOnly(2026, 10, 8), item.DueDate);
            Assert.Empty(events);

            Execute(mcp, "update_todo", new
            {
                paper_id = paper.Id,
                todo_id = item.Id,
                text = "MCP legacy-shaped update"
            });
            Assert.Equal(new DateOnly(2026, 10, 2), item.PlannedStartDate);
            Assert.Equal(new DateOnly(2026, 10, 8), item.DueDate);

            plugin.UpdateTodo(new UpdateTodoRequest
            {
                PaperId = paper.Id,
                TodoId = item.Id,
                Text = "Legacy-shaped update"
            });
            Assert.Equal(new DateOnly(2026, 10, 2), item.PlannedStartDate);
            Assert.Equal(new DateOnly(2026, 10, 8), item.DueDate);

            events.Clear();
            using var failingPlugin = new PaperBodyPluginHostApi(
                controller,
                new PaperCommandService(controller, () => false),
                paper.Id,
                "failing-planning-test",
                PaperTodoPermissionNames.All,
                () => true,
                () => true);
            var saveFailure = Assert.Throws<PaperTodoPluginException>(() =>
                failingPlugin.UpdateTodo(new UpdateTodoRequest
                {
                    PaperId = paper.Id,
                    TodoId = item.Id,
                    Planning = new TodoPlanningUpdate(
                        new DateOnly(2026, 11, 1),
                        new DateOnly(2026, 11, 2))
                }));
            Assert.Equal("save_failed", saveFailure.Code);
            Assert.Equal(new DateOnly(2026, 10, 2), item.PlannedStartDate);
            Assert.Equal(new DateOnly(2026, 10, 8), item.DueDate);
            Assert.Empty(events);

            var countBeforeInvalidAppend = paper.Items.Count;
            var invalidAppend = Assert.Throws<McpApiException>(() => Execute(
                mcp,
                "add_todos",
                new
                {
                    paper_id = paper.Id,
                    todos = new[]
                    {
                        new
                        {
                            text = "Invalid range",
                            planned_start_date = "2026-12-06",
                            due_date = "2026-12-05"
                        }
                    }
                }));
            Assert.Equal("invalid_params", invalidAppend.Code);
            Assert.Equal(countBeforeInvalidAppend, paper.Items.Count);

            var added = Execute(mcp, "add_todos", new
            {
                paper_id = paper.Id,
                todos = new[]
                {
                    new
                    {
                        text = "Created with planning",
                        planned_start_date = "2026-12-01",
                        due_date = "2026-12-05"
                    }
                }
            });
            var addedTodo = Assert.Single(added.GetProperty("added").EnumerateArray());
            Assert.Equal("2026-12-01", addedTodo.GetProperty("planned_start_date").GetString());
            Assert.Equal("2026-12-05", addedTodo.GetProperty("due_date").GetString());
        }
        finally
        {
            subscription?.Dispose();
            controller?.Dispose();
            stateFile.Restore();
            backupFile.Restore();
        }
    }

    private static JsonElement Execute(
        McpCommandService service,
        string method,
        object parameters) =>
        JsonSerializer.SerializeToElement(service.Execute(
            JsonSerializer.SerializeToElement(new
            {
                method,
                @params = parameters
            })));

    private sealed record FileSnapshot(string Path, byte[]? Contents)
    {
        public static FileSnapshot Capture(string path) =>
            new(path, File.Exists(path) ? File.ReadAllBytes(path) : null);

        public void Restore()
        {
            if (Contents == null)
            {
                File.Delete(Path);
                return;
            }
            File.WriteAllBytes(Path, Contents);
        }
    }
}
