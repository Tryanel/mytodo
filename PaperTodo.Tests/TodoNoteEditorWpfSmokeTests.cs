using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PaperTodo.Tests;

[Collection(TodoBoardWpfSmokeCollection.Name)]
public sealed class TodoNoteEditorWpfSmokeTests
{
    [Fact]
    public void Draft_survives_owner_hide_and_switch_close_decisions_are_explicit()
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
        PaperWindow? otherWindow = null;
        try
        {
            controller = new AppController(Dispatcher.CurrentDispatcher);
            controller.State.Papers.Clear();
            controller.State.EnableAnimations = false;
            controller.State.UseCapsuleMode = true;
            controller.State.UseDeepCapsuleMode = false;

            var first = Task("first", "Original task", "initial note", 0);
            var second = Task("second", "Next task", "second note", 1);
            var paper = TodoPaper("note-session-owner", "Owner paper", first, second);
            var otherPaper = TodoPaper(
                "note-session-other",
                "Other paper",
                Task("other", "Other task", "other note", 0));
            controller.State.Papers.Add(paper);
            controller.State.Papers.Add(otherPaper);

            window = ShowOffscreen(controller, paper, -30000);
            otherWindow = ShowOffscreen(controller, otherPaper, -29000);
            OpenNote(window, first);
            Drain(window.Dispatcher);

            var dialog = Assert.IsType<TodoNoteDialog>(window.TodoNoteEditorWindow);
            Assert.True(dialog.IsVisible);
            Assert.Null(dialog.Owner);
            Assert.Empty(window.OwnedWindows.Cast<Window>());
            Assert.Equal(first.Id, dialog.ItemId);

            dialog.Editor.Text = "draft kept through hide";
            dialog.Editor.Select(3, 5);
            Drain(window.Dispatcher);
            Assert.True(dialog.IsDirty);

            window.SetCollapsedState(true, animate: false);
            Drain(window.Dispatcher);
            Assert.True(paper.IsCollapsed);
            Assert.True(dialog.IsVisible);
            Assert.Equal("draft kept through hide", dialog.Editor.Text);

            otherWindow.SetCollapsedState(true, animate: false);
            otherWindow.SetCollapsedState(false, animate: false);
            Drain(window.Dispatcher);
            Assert.False(otherPaper.IsCollapsed);
            Assert.True(dialog.IsVisible);

            controller.HidePaper(paper);
            Drain(window.Dispatcher);
            Assert.False(paper.IsVisible);
            Assert.True(dialog.IsVisible);
            Assert.Equal(first.Id, window.TodoNoteEditorItemId);
            Assert.Equal("draft kept through hide", dialog.Editor.Text);

            controller.ShowPaper(paper, activate: false);
            window.SetCollapsedState(false, animate: false);
            Drain(window.Dispatcher);
            OpenNote(window, second);
            Drain(window.Dispatcher);
            Assert.Equal(TodoNoteDraftIntent.SwitchTarget, dialog.PendingIntent);
            Assert.Equal(first.Id, dialog.ItemId);
            Assert.Equal("draft kept through hide", dialog.Editor.Text);

            ClickVisibleButton(dialog, Strings.Get("TodoNoteCancelSwitch"));
            Drain(window.Dispatcher);
            Assert.Equal(TodoNoteDraftIntent.None, dialog.PendingIntent);
            Assert.Equal(first.Id, dialog.ItemId);
            Assert.Equal("draft kept through hide", dialog.Editor.Text);
            Assert.Equal(3, dialog.Editor.SelectionStart);
            Assert.Equal(5, dialog.Editor.SelectionLength);

            OpenNote(window, second);
            Drain(window.Dispatcher);
            ClickVisibleButton(dialog, Strings.Get("CommonSave"));
            Drain(window.Dispatcher);
            Assert.Equal("draft kept through hide", first.Note);
            Assert.Equal(second.Id, dialog.ItemId);
            Assert.Equal("second note", dialog.Editor.Text);
            Assert.False(dialog.IsDirty);

            dialog.Editor.Text = "unfinished second draft";
            dialog.Editor.Select(2, 6);
            PressEscape(dialog);
            Drain(window.Dispatcher);
            Assert.True(dialog.IsVisible);
            Assert.Equal(TodoNoteDraftIntent.Close, dialog.PendingIntent);
            ClickVisibleButton(dialog, Strings.Get("TodoNoteContinueEditing"));
            Drain(window.Dispatcher);
            Assert.True(dialog.IsVisible);
            Assert.Equal(second.Id, dialog.ItemId);
            Assert.Equal("unfinished second draft", dialog.Editor.Text);
            Assert.Equal(2, dialog.Editor.SelectionStart);
            Assert.Equal(6, dialog.Editor.SelectionLength);

            dialog.Close();
            Drain(window.Dispatcher);
            ClickVisibleButton(dialog, Strings.Get("TodoNoteDiscardAndClose"));
            Drain(window.Dispatcher);
            Assert.False(dialog.IsVisible);
            Assert.Null(window.TodoNoteEditorWindow);
            Assert.Equal("second note", second.Note);

            OpenNote(window, first);
            Drain(window.Dispatcher);
            dialog = Assert.IsType<TodoNoteDialog>(window.TodoNoteEditorWindow);
            dialog.Editor.Text = "saved while owner hidden";
            controller.HidePaper(paper);
            Drain(window.Dispatcher);
            Assert.True(dialog.IsVisible);
            ClickVisibleButton(dialog, Strings.Get("CommonSave"));
            Drain(window.Dispatcher);
            Assert.False(paper.IsVisible);
            Assert.False(dialog.IsVisible);
            Assert.Equal("saved while owner hidden", first.Note);

            controller.SaveNow(sync: true);
            var restored = new StateStore().Load();
            var restoredPaper = Assert.Single(restored.Papers, candidate => candidate.Id == paper.Id);
            var restoredItem = Assert.Single(restoredPaper.Items, candidate => candidate.Id == first.Id);
            Assert.Equal("saved while owner hidden", restoredItem.Note);

            controller.State.Theme = Theme.IsDark ? "light" : "dark";
            Theme.Invalidate();
            window.UpdateTheme();
            paper.AlwaysOnTop = true;
            window.RefreshEffectiveTopmost();
            OpenNote(window, first);
            Drain(window.Dispatcher);
            dialog = Assert.IsType<TodoNoteDialog>(window.TodoNoteEditorWindow);
            Assert.True(dialog.Topmost);
            Assert.Equal(Theme.TextBrush.ToString(), dialog.Editor.Foreground.ToString());

            dialog.Editor.Text = "orphaned draft";
            Assert.True(paper.Items.Remove(first));
            ClickVisibleButton(dialog, Strings.Get("CommonSave"));
            Drain(window.Dispatcher);
            Assert.True(dialog.IsVisible);
            Assert.True(dialog.IsDirty);
            Assert.Contains(
                Descendants<TextBlock>(dialog),
                text => text.IsVisible && text.Text == Strings.Get("TodoNoteSaveFailed"));
        }
        finally
        {
            window?.TodoNoteEditorWindow?.ForceClose();
            if (window is { IsClosed: false })
            {
                window.CloseForReal(saveBeforeClose: false);
            }
            if (otherWindow is { IsClosed: false })
            {
                otherWindow.CloseForReal(saveBeforeClose: false);
            }
            controller?.Dispose();
            stateFile.Restore();
            backupFile.Restore();
        }
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
        IsCollapsed = false,
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
        var row = Descendants<Border>(window).Single(border =>
            border.Tag is string id && id == item.Id);
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
    }

    private static void ClickVisibleButton(DependencyObject root, string content)
    {
        var button = Descendants<Button>(root).Single(candidate =>
            candidate.IsVisible &&
            candidate.IsEnabled &&
            string.Equals(candidate.Content as string, content, StringComparison.Ordinal));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void PressEscape(Window window)
    {
        window.RaiseEvent(new KeyEventArgs(
            Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(window),
            Environment.TickCount,
            Key.Escape)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        });
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
