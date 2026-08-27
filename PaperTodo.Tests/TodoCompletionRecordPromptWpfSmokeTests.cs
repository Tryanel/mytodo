using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PaperTodo.Plugin;

namespace PaperTodo.Tests;

[Collection(TodoBoardWpfSmokeCollection.Name)]
public sealed class TodoCompletionRecordPromptWpfSmokeTests
{
    [Fact]
    public void Direct_completion_offers_record_action_while_external_delete_and_hide_clear_it()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RunSmokeScenario();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF smoke thread timed out.");
        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void RunSmokeScenario()
    {
        var stateFile = FileSnapshot.Capture(Path.Combine(AppContext.BaseDirectory, "data.json"));
        var backupFile = FileSnapshot.Capture(Path.Combine(AppContext.BaseDirectory, "data.backup.json"));
        AppController? controller = null;
        PaperWindow? window = null;
        try
        {
            controller = new AppController(Dispatcher.CurrentDispatcher);
            controller.State.Papers.Clear();
            controller.State.EnableAnimations = false;
            controller.State.EnableToolTips = true;
            controller.State.AutoClearCompletedTodos = false;
            controller.State.AutoMoveCompletedTodosToBottom = false;
            controller.State.UseCapsuleMode = false;
            controller.State.UseDeepCapsuleMode = false;

            var items = new[]
            {
                Task("direct", "Direct completion", 0),
                Task("external", "External completion", 1),
                Task("hidden", "Hide cleanup", 2)
            };
            var paper = new PaperData
            {
                Id = "completion-record-smoke",
                Type = PaperTypes.Todo,
                Title = "Completion records",
                IsVisible = true,
                IsCollapsed = false,
                Width = 560,
                Height = 360,
                X = -30000,
                Y = -30000,
                Items = [.. items]
            };
            controller.State.Papers.Add(paper);

            window = controller.GetOrCreatePaperWindow(paper);
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Left = -30000;
            window.Top = -30000;
            window.Show();
            Drain(window.Dispatcher);

            var directRow = TodoRow(window, items[0].Id);
            var directEditor = Descendants<TodoTextBox>(directRow).Single();
            Keyboard.Focus(directEditor);
            var focusedBefore = Keyboard.FocusedElement;
            Descendants<CheckBox>(directRow).Single().IsChecked = true;
            Drain(window.Dispatcher);

            Assert.Same(focusedBefore, Keyboard.FocusedElement);
            Assert.Equal(items[0].Id, window.TodoCompletionRecordPromptItemId);
            var prompt = Assert.Single(CompletionPrompts(window));
            Assert.False(prompt.Focusable);
            Assert.True(ToolTipService.GetIsEnabled(prompt));

            prompt.RaiseEvent(new MouseButtonEventArgs(
                Mouse.PrimaryDevice,
                Environment.TickCount,
                MouseButton.Left)
            {
                RoutedEvent = Mouse.MouseUpEvent
            });
            Drain(window.Dispatcher);
            Assert.Null(window.TodoCompletionRecordPromptItemId);
            var editorWindow = Assert.Single(
                window.OwnedWindows.Cast<Window>(),
                candidate =>
                    candidate.IsVisible &&
                    candidate.Title == Strings.Get("TodoNoteTitle"));
            Assert.Equal(items[0].Id, window.TodoNoteEditorItemId);

            var otherRow = TodoRow(window, items[1].Id);
            var otherCheck = Descendants<CheckBox>(otherRow).Single();
            otherCheck.IsChecked = true;
            Drain(window.Dispatcher);
            Assert.True(items[1].Done);
            Assert.Null(window.TodoCompletionRecordPromptItemId);
            Assert.Equal(items[0].Id, window.TodoNoteEditorItemId);
            otherCheck.IsChecked = false;
            Drain(window.Dispatcher);

            editorWindow.Close();
            Drain(window.Dispatcher);
            Assert.Null(window.TodoNoteEditorItemId);

            controller.State.EnableToolTips = false;
            directRow = TodoRow(window, items[0].Id);
            var directCheck = Descendants<CheckBox>(directRow).Single();
            directCheck.IsChecked = false;
            directCheck.IsChecked = true;
            Drain(window.Dispatcher);
            Assert.Equal(items[0].Id, window.TodoCompletionRecordPromptItemId);
            Assert.False(ToolTipService.GetIsEnabled(
                Assert.Single(CompletionPrompts(window))));

            var deleted = controller.PaperCommands.DeleteTodo(
                new DeleteTodoRequest
                {
                    PaperId = paper.Id,
                    TodoId = items[0].Id
                },
                PaperOperationContext.Mcp());
            Drain(window.Dispatcher);
            Assert.True(deleted.Deleted);
            Assert.Null(window.TodoCompletionRecordPromptItemId);
            Assert.Empty(CompletionPrompts(window));

            controller.PaperCommands.UpdateTodo(
                new UpdateTodoRequest
                {
                    PaperId = paper.Id,
                    TodoId = items[1].Id,
                    Done = true
                },
                PaperOperationContext.Plugin("smoke.plugin"));
            Drain(window.Dispatcher);
            Assert.True(items[1].Done);
            Assert.Null(window.TodoCompletionRecordPromptItemId);
            Assert.Empty(CompletionPrompts(window));

            controller.State.EnableAnimations = true;
            var hiddenRow = TodoRow(window, items[2].Id);
            Descendants<CheckBox>(hiddenRow).Single().IsChecked = true;
            Drain(window.Dispatcher);
            Assert.Equal(items[2].Id, window.TodoCompletionRecordPromptItemId);

            controller.State.UseCapsuleMode = true;
            window.UpdateCapsuleMode();
            window.SetCollapsedState(true, animate: false);
            Drain(window.Dispatcher);
            Assert.True(paper.IsCollapsed);
            Assert.Null(window.TodoCompletionRecordPromptItemId);
            Assert.Contains(
                Descendants<TextBlock>(window),
                text => text.Text == paper.Title);

            window.SetCollapsedState(false, animate: false);
            Drain(window.Dispatcher);
            Assert.False(paper.IsCollapsed);

            controller.HidePaper(paper);
            Drain(window.Dispatcher);
            Assert.False(paper.IsVisible);
            Assert.Null(window.TodoCompletionRecordPromptItemId);
        }
        finally
        {
            if (window is { IsClosed: false })
            {
                window.CloseForReal(saveBeforeClose: false);
            }
            controller?.Dispose();
            stateFile.Restore();
            backupFile.Restore();
        }
    }

    private static PaperItem Task(string id, string text, int order) => new()
    {
        Id = id,
        Text = text,
        Order = order,
        CreatedAt = DateTimeOffset.Now
    };

    private static Border TodoRow(DependencyObject root, string itemId) =>
        Descendants<Border>(root).Single(border =>
            border.Tag is string id &&
            string.Equals(id, itemId, StringComparison.Ordinal));

    private static IReadOnlyList<Border> CompletionPrompts(DependencyObject root) =>
        Descendants<Border>(root)
            .Where(border =>
                AutomationProperties.GetName(border) ==
                Strings.Get("TodoCompletionRecordAction"))
            .ToList();

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }
            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void Drain(Dispatcher dispatcher)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            var frame = new DispatcherFrame();
            _ = dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                (Action)(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

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
