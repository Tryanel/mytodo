using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PaperTodo.Plugin;

namespace PaperTodo.Tests;

[Collection(TodoBoardWpfSmokeCollection.Name)]
public sealed class TodoNoteDestructiveTransactionWpfSmokeTests
{
    [Fact]
    public void Delete_external_race_and_exit_cancel_preserve_transaction_boundaries()
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
        var windows = new List<PaperWindow>();
        TodoNoteDialog? invalidDialog = null;
        try
        {
            controller = new AppController(Dispatcher.CurrentDispatcher);
            controller.State.Papers.Clear();
            controller.State.EnableAnimations = false;
            controller.State.UseCapsuleMode = false;
            controller.State.UseDeepCapsuleMode = false;

            var taskA = Task("task-a", "Task A", "original A", 0);
            taskA.LinkPaper("paper-b");
            var paperA = TodoPaper("paper-a", "Paper A", taskA);
            var taskB = Task("task-b", "Task B", "original B", 0);
            var paperB = TodoPaper("paper-b", "Paper B", taskB);
            controller.State.Papers.Add(paperA);
            controller.State.Papers.Add(paperB);
            var windowA = ShowOffscreen(controller, paperA, -30000);
            var windowB = ShowOffscreen(controller, paperB, -29000);
            windows.Add(windowA);
            windows.Add(windowB);

            OpenNote(windowA, taskA);
            var dialogA = Assert.IsType<TodoNoteDialog>(windowA.TodoNoteEditorWindow);
            dialogA.Editor.Text = "latest A";
            dialogA.Editor.Select(2, 4);
            ClickDeleteTask(windowA, taskA.Id);
            Drain(windowA.Dispatcher);
            Assert.Contains(taskA, paperA.Items);
            Assert.Equal(TodoNoteDraftIntent.DeleteTask, dialogA.PendingIntent);

            ClickVisibleButton(dialogA, Strings.Get("TodoNoteCancelDelete"));
            Drain(windowA.Dispatcher);
            Assert.Contains(taskA, paperA.Items);
            Assert.Equal("latest A", dialogA.Editor.Text);
            Assert.Equal(2, dialogA.Editor.SelectionStart);
            Assert.Equal(4, dialogA.Editor.SelectionLength);

            ClickDeleteTask(windowA, taskA.Id);
            ClickVisibleButton(dialogA, Strings.Get("CommonSave"));
            Drain(windowA.Dispatcher);
            Assert.DoesNotContain(paperA.Items, item => item.Id == taskA.Id);
            Assert.Null(windowA.TodoNoteEditorWindow);

            InvokeUndo(windowA);
            Drain(windowA.Dispatcher);
            taskA = Assert.Single(paperA.Items, item => item.Id == "task-a");
            Assert.Equal("latest A", taskA.Note);
            Assert.Equal(paperB.Id, taskA.LinkedPaperId);

            OpenNote(windowA, taskA);
            dialogA = Assert.IsType<TodoNoteDialog>(windowA.TodoNoteEditorWindow);
            dialogA.Editor.Text = "paper deletion draft";
            controller.DeletePaper(paperA);
            Drain(windowA.Dispatcher);
            Assert.Contains(paperA, controller.State.Papers);
            Assert.Equal(TodoNoteDraftIntent.DeletePaper, dialogA.PendingIntent);
            ClickVisibleButton(dialogA, Strings.Get("TodoNoteCancelDelete"));
            Assert.Contains(paperA, controller.State.Papers);
            Assert.True(dialogA.IsVisible);

            controller.DeletePaper(paperA);
            ClickVisibleButton(dialogA, Strings.Get("TodoNoteDiscardAndDelete"));
            Drain(windowA.Dispatcher);
            Assert.DoesNotContain(paperA, controller.State.Papers);
            Assert.True(windowA.IsClosed);

            OpenNote(windowB, taskB);
            var externalDialog = Assert.IsType<TodoNoteDialog>(windowB.TodoNoteEditorWindow);
            externalDialog.Editor.Text = "orphaned external draft";
            controller.PaperCommands.DeleteTodo(
                new DeleteTodoRequest
                {
                    PaperId = paperB.Id,
                    TodoId = taskB.Id
                },
                PaperOperationContext.Mcp());
            Drain(windowB.Dispatcher);
            invalidDialog = externalDialog;
            Assert.Null(windowB.TodoNoteEditorWindow);
            Assert.True(externalDialog.IsVisible);
            Assert.True(externalDialog.Editor.IsReadOnly);
            Assert.Equal("orphaned external draft", externalDialog.Editor.Text);
            Assert.Contains(
                Descendants<TextBlock>(externalDialog),
                text => text.IsVisible && text.Text == Strings.Get("TodoNoteTaskInvalidated"));
            Assert.DoesNotContain(paperB.Items, item => item.Id == taskB.Id);
            Assert.DoesNotContain(
                paperB.Items,
                item => item.Note == "orphaned external draft");
            ClickVisibleButton(externalDialog, Strings.Get("CommonClose"));
            Assert.False(externalDialog.IsVisible);
            invalidDialog = null;

            var taskE = Task("task-e", "Task E", "original E", 0);
            var paperE = TodoPaper("paper-e", "Paper E", taskE);
            controller.State.Papers.Add(paperE);
            var windowE = ShowOffscreen(controller, paperE, -28500);
            windows.Add(windowE);
            OpenNote(windowE, taskE);
            var externalPaperDialog = Assert.IsType<TodoNoteDialog>(
                windowE.TodoNoteEditorWindow);
            externalPaperDialog.Editor.Text = "orphaned paper draft";
            controller.PaperCommands.DeletePaper(
                paperE.Id,
                PaperOperationContext.Plugin("smoke.plugin"));
            Drain(windowE.Dispatcher);
            invalidDialog = externalPaperDialog;
            Assert.True(windowE.IsClosed);
            Assert.DoesNotContain(paperE, controller.State.Papers);
            Assert.True(externalPaperDialog.IsVisible);
            Assert.True(externalPaperDialog.Editor.IsReadOnly);
            Assert.Contains(
                Descendants<TextBlock>(externalPaperDialog),
                text => text.IsVisible && text.Text == Strings.Get("TodoNotePaperInvalidated"));
            ClickVisibleButton(externalPaperDialog, Strings.Get("CommonClose"));
            Assert.False(externalPaperDialog.IsVisible);
            invalidDialog = null;

            var taskC = Task("task-c", "Task C", "original C", 0);
            var taskD = Task("task-d", "Task D", "original D", 0);
            var paperC = TodoPaper("paper-c", "Paper C", taskC);
            var paperD = TodoPaper("paper-d", "Paper D", taskD);
            controller.State.Papers.Add(paperC);
            controller.State.Papers.Add(paperD);
            var windowC = ShowOffscreen(controller, paperC, -28000);
            var windowD = ShowOffscreen(controller, paperD, -27000);
            windows.Add(windowC);
            windows.Add(windowD);
            OpenNote(windowC, taskC);
            OpenNote(windowD, taskD);
            var dialogC = Assert.IsType<TodoNoteDialog>(windowC.TodoNoteEditorWindow);
            var dialogD = Assert.IsType<TodoNoteDialog>(windowD.TodoNoteEditorWindow);
            dialogC.Editor.Text = "draft C";
            dialogD.Editor.Text = "draft D";

            Assert.True(controller.HandleSystemSessionEnding());
            Drain(windowC.Dispatcher);
            Assert.Equal(TodoNoteDraftIntent.Exit, dialogC.PendingIntent);
            ClickVisibleButton(dialogC, Strings.Get("CommonSave"));
            Drain(windowC.Dispatcher);
            Assert.Equal("original C", taskC.Note);
            Assert.Equal(TodoNoteDraftIntent.Exit, dialogD.PendingIntent);
            ClickVisibleButton(dialogD, Strings.Get("TodoNoteCancelExit"));
            Drain(windowC.Dispatcher);

            Assert.False(windowC.IsClosed);
            Assert.False(windowD.IsClosed);
            Assert.True(dialogC.IsVisible);
            Assert.True(dialogD.IsVisible);
            Assert.False(dialogC.Editor.IsReadOnly);
            Assert.False(dialogD.Editor.IsReadOnly);
            Assert.Equal("draft C", dialogC.Editor.Text);
            Assert.Equal("draft D", dialogD.Editor.Text);
            Assert.Equal("original C", taskC.Note);
            Assert.Equal("original D", taskD.Note);
            Assert.True(dialogC.IsDirty);
            Assert.True(dialogD.IsDirty);

            VerifyDeferredBatchTargetsStayStable(controller, windows);
        }
        finally
        {
            invalidDialog?.ForceClose();
            foreach (var window in windows)
            {
                window.TodoNoteEditorWindow?.ForceClose();
            }
            controller?.Dispose();
            stateFile.Restore();
            backupFile.Restore();
        }
    }

    private static void VerifyDeferredBatchTargetsStayStable(
        AppController controller,
        List<PaperWindow> windows)
    {
        var deleteA = Task("delete-a", "Delete A", "note A", 0);
        var deleteB = Task("delete-b", "Delete B", "", 1);
        var deleteC = Task("delete-c", "Delete C", "", 2);
        var deletePaper = TodoPaper(
            "paper-delete-selection",
            "Delete selection",
            deleteA,
            deleteB,
            deleteC);
        controller.State.Papers.Add(deletePaper);
        var deleteWindow = ShowOffscreen(controller, deletePaper, -26000);
        windows.Add(deleteWindow);
        OpenNote(deleteWindow, deleteA);
        var deleteDialog = Assert.IsType<TodoNoteDialog>(deleteWindow.TodoNoteEditorWindow);
        deleteDialog.Editor.Text = "dirty delete A";
        SetSelectedTodoIds(deleteWindow, deleteA.Id, deleteB.Id);
        InvokePrivate(deleteWindow, "DeleteSelectedTodoItems", false, null);
        SetSelectedTodoIds(deleteWindow, deleteC.Id);
        ClickVisibleButton(deleteDialog, Strings.Get("TodoNoteDiscardAndDelete"));
        Drain(deleteWindow.Dispatcher);
        Assert.DoesNotContain(deletePaper.Items, item => item.Id == deleteA.Id);
        Assert.DoesNotContain(deletePaper.Items, item => item.Id == deleteB.Id);
        Assert.Contains(deletePaper.Items, item => item.Id == deleteC.Id);

        controller.State.AutoClearCompletedTodos = true;
        var completeA = Task("complete-a", "Complete A", "note A", 0);
        var completeB = Task("complete-b", "Complete B", "", 1);
        var completeC = Task("complete-c", "Complete C", "", 2);
        var completePaper = TodoPaper(
            "paper-complete-selection",
            "Complete selection",
            completeA,
            completeB,
            completeC);
        controller.State.Papers.Add(completePaper);
        var completeWindow = ShowOffscreen(controller, completePaper, -25000);
        windows.Add(completeWindow);
        OpenNote(completeWindow, completeA);
        var completeDialog = Assert.IsType<TodoNoteDialog>(completeWindow.TodoNoteEditorWindow);
        completeDialog.Editor.Text = "dirty complete A";
        SetSelectedTodoIds(completeWindow, completeA.Id, completeB.Id);
        InvokePrivate(completeWindow, "ApplyDoneToSelectedTodos", true, false, null);
        SetSelectedTodoIds(completeWindow, completeC.Id);
        ClickVisibleButton(completeDialog, Strings.Get("TodoNoteDiscardAndDelete"));
        Drain(completeWindow.Dispatcher);
        Assert.DoesNotContain(completePaper.Items, item => item.Id == completeA.Id);
        Assert.DoesNotContain(completePaper.Items, item => item.Id == completeB.Id);
        Assert.Contains(completePaper.Items, item => item.Id == completeC.Id);

        var doneA = Task("done-a", "Done A", "note A", 0);
        doneA.SetDone(true);
        var doneB = Task("done-b", "Done B", "", 1);
        doneB.SetDone(true);
        var doneC = Task("done-c", "Done C", "", 2);
        var donePaper = TodoPaper(
            "paper-clear-done",
            "Clear done",
            doneA,
            doneB,
            doneC);
        controller.State.Papers.Add(donePaper);
        var doneWindow = ShowOffscreen(controller, donePaper, -24000);
        windows.Add(doneWindow);
        OpenNote(doneWindow, doneA);
        var doneDialog = Assert.IsType<TodoNoteDialog>(doneWindow.TodoNoteEditorWindow);
        doneDialog.Editor.Text = "dirty done A";
        InvokePrivate(doneWindow, "ClearDoneItems", false, null);
        doneB.SetDone(false);
        doneC.SetDone(true);
        ClickVisibleButton(doneDialog, Strings.Get("TodoNoteDiscardAndDelete"));
        Drain(doneWindow.Dispatcher);
        Assert.DoesNotContain(donePaper.Items, item => item.Id == doneA.Id);
        Assert.Contains(donePaper.Items, item => item.Id == doneB.Id);
        Assert.Contains(donePaper.Items, item => item.Id == doneC.Id);
    }

    private static void SetSelectedTodoIds(PaperWindow window, params string[] itemIds)
    {
        var field = typeof(PaperWindow).GetField(
            "_selectedTodoItemIds",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var selected = Assert.IsType<HashSet<string>>(field?.GetValue(window));
        selected.Clear();
        foreach (var itemId in itemIds)
        {
            selected.Add(itemId);
        }
    }

    private static void InvokePrivate(
        PaperWindow window,
        string methodName,
        params object?[] arguments)
    {
        var method = typeof(PaperWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(window, arguments);
    }

    private static void ClickDeleteTask(PaperWindow window, string itemId)
    {
        var row = TodoRow(window, itemId);
        var menu = Assert.IsType<ContextMenu>(row.ContextMenu);
        var delete = menu.Items
            .OfType<MenuItem>()
            .Single(item => string.Equals(
                item.Header as string,
                Strings.Get("MenuDeleteItem"),
                StringComparison.Ordinal));
        delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private static void InvokeUndo(PaperWindow window)
    {
        var undo = typeof(PaperWindow).GetMethod(
            "Undo",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(undo);
        undo.Invoke(window, null);
    }

    private static PaperWindow ShowOffscreen(
        AppController controller,
        PaperData paper,
        double left)
    {
        var window = controller.GetOrCreatePaperWindow(paper);
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Left = left;
        window.Top = -30000;
        window.Show();
        Drain(window.Dispatcher);
        return window;
    }

    private static PaperData TodoPaper(
        string id,
        string title,
        params PaperItem[] items) => new()
    {
        Id = id,
        Type = PaperTypes.Todo,
        Title = title,
        IsVisible = true,
        Width = 560,
        Height = 360,
        X = -30000,
        Y = -30000,
        Items = [.. items]
    };

    private static PaperItem Task(
        string id,
        string text,
        string note,
        int order) => new()
    {
        Id = id,
        Text = text,
        Note = note,
        Order = order,
        CreatedAt = DateTimeOffset.Now
    };

    private static void OpenNote(PaperWindow window, PaperItem item)
    {
        var row = TodoRow(window, item.Id);
        var indicator = Descendants<Border>(row).Single(border =>
            string.Equals(
                border.ToolTip as string,
                Strings.Format("TodoNoteToolTip", Compact(item.Note)),
                StringComparison.Ordinal));
        indicator.RaiseEvent(new MouseButtonEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent = Mouse.MouseUpEvent
        });
        Drain(window.Dispatcher);
    }

    private static Border TodoRow(DependencyObject root, string itemId) =>
        Descendants<Border>(root).Single(border =>
            border.Tag is string id && id == itemId);

    private static void ClickVisibleButton(DependencyObject root, string content)
    {
        var button = Descendants<Button>(root).Single(candidate =>
            candidate.IsVisible &&
            candidate.IsEnabled &&
            string.Equals(candidate.Content as string, content, StringComparison.Ordinal));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static string Compact(string note) => string.Join(
        " ",
        note.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0));

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
