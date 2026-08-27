using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PaperTodo.Plugin;

namespace PaperTodo.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TodoBoardWpfSmokeCollection
{
    public const string Name = "Todo Board WPF smoke";
}

[Collection(TodoBoardWpfSmokeCollection.Name)]
public sealed class TodoBoardActivityCalendarWpfSmokeTests
{
    [Fact]
    public void Board_temporal_views_resize_open_overflow_and_navigate_to_the_owning_todo()
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
        Application? application = null;
        AppController? controller = null;
        PaperWindow? boardWindow = null;
        try
        {
            application = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            controller = new AppController();
            controller.State.Papers.Clear();
            controller.State.EnableAnimations = false;
            controller.State.EnableToolTips = true;
            controller.State.UseCapsuleMode = false;
            controller.State.UseDeepCapsuleMode = false;

            var items = Enumerable.Range(0, 7)
                .Select(index => new PaperItem
                {
                    Id = $"task-{index}",
                    Text = index == 6 ? "" : $"Task {index + 1}",
                    Note = index == 6 ? "Note-only task" : "",
                    Order = index,
                    CreatedAt = new DateTimeOffset(
                        DateTime.Today.Year,
                        DateTime.Today.Month,
                        Math.Max(1, DateTime.Today.Day - 2),
                        9,
                        index,
                        0,
                        TimeZoneInfo.Local.GetUtcOffset(DateTime.Now))
                })
                .ToList();
            Assert.Equal(
                TodoPlanningUpdateResult.Updated,
                items[0].SetPlanningDates(
                    DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
                    DateOnly.FromDateTime(DateTime.Today.AddDays(2))));
            Assert.Equal(
                TodoPlanningUpdateResult.Updated,
                items[1].SetPlanningDates(
                    DateOnly.FromDateTime(DateTime.Today),
                    null));
            var todo = new PaperData
            {
                Id = "wpf-smoke-todo",
                Type = PaperTypes.Todo,
                Title = "Smoke tasks",
                IsVisible = false,
                IsCollapsed = true,
                Items = items
            };
            var board = new PaperData
            {
                Id = "wpf-smoke-board",
                Type = PaperTypes.Board,
                Title = "Activity calendar",
                BoardView = TodoBoardViews.Calendar,
                IsVisible = true,
                Width = 900,
                Height = 440,
                X = -30000,
                Y = -30000
            };
            controller.State.Papers.Add(todo);
            controller.State.Papers.Add(board);

            boardWindow = controller.GetOrCreatePaperWindow(board);
            boardWindow.ShowActivated = false;
            boardWindow.ShowInTaskbar = false;
            boardWindow.Left = -30000;
            boardWindow.Top = -30000;
            boardWindow.Width = 900;
            boardWindow.Height = 440;
            boardWindow.Show();
            Drain(boardWindow.Dispatcher);

            var smallVisibleBars = CalendarTaskBars(boardWindow).Count;
            Assert.True(smallVisibleBars > 0);

            boardWindow.Height = 1000;
            Drain(boardWindow.Dispatcher);
            var largeVisibleBars = CalendarTaskBars(boardWindow).Count;
            Assert.True(
                largeVisibleBars > smallVisibleBars,
                $"Expected more visible lanes after resize, got {smallVisibleBars} then {largeVisibleBars}.");

            boardWindow.Height = 440;
            Drain(boardWindow.Dispatcher);
            var overflow = Descendants<Button>(boardWindow)
                .First(button =>
                    AutomationProperties.GetName(button) is { } name &&
                    name == Strings.Format("TodoBoardCalendarOverflowToolTip", 7));
            overflow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(boardWindow.Dispatcher);

            Assert.Equal(7, PopupTaskButtons().Count);
            Assert.DoesNotContain(
                PopupTaskButtons(),
                button => string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));

            boardWindow.Left += 12;
            Drain(boardWindow.Dispatcher);
            Assert.Empty(PopupTaskButtons());

            overflow = Descendants<Button>(boardWindow)
                .First(button =>
                    AutomationProperties.GetName(button) is { } name &&
                    name == Strings.Format("TodoBoardCalendarOverflowToolTip", 7));
            overflow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(boardWindow.Dispatcher);
            var popupTask = PopupTaskButtons()
                .First(button =>
                    AutomationProperties.GetName(button).Contains(
                        "Task 1",
                        StringComparison.Ordinal));
            popupTask.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(boardWindow.Dispatcher);

            Assert.True(todo.IsVisible);
            Assert.False(todo.IsCollapsed);
            Assert.DoesNotContain(
                CalendarTaskBars(boardWindow),
                bar => string.IsNullOrWhiteSpace(AutomationProperties.GetName(bar)));

            var timelineButton = Descendants<Button>(boardWindow)
                .First(button => Descendants<TextBlock>(button)
                    .Any(text => text.Text == Strings.Get("TodoBoardTimelineView")));
            timelineButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(boardWindow.Dispatcher);
            Assert.Equal(TodoBoardViews.Timeline, board.BoardView);

            Assert.Contains(
                Descendants<Border>(boardWindow),
                border => AutomationProperties.GetName(border).Contains(
                    "Task 1",
                    StringComparison.Ordinal));
            Assert.Contains(
                Descendants<Border>(boardWindow),
                border => AutomationProperties.GetName(border).Contains(
                    "Task 2",
                    StringComparison.Ordinal));
            var unscheduledButtons = Descendants<Button>(boardWindow)
                .Where(button =>
                    AutomationProperties.GetName(button).StartsWith(
                        Strings.Get("TodoBoardTimelineUnscheduledAutomationPrefix"),
                        StringComparison.Ordinal))
                .ToList();
            Assert.Equal(5, unscheduledButtons.Count);

            var monthButton = Descendants<Button>(boardWindow)
                .First(button => Descendants<TextBlock>(button)
                    .Any(text => text.Text == Strings.Get("TodoBoardTimelineMonth")));
            monthButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(boardWindow.Dispatcher);
            Assert.Equal(TodoBoardTimelineScales.Month, board.BoardTimelineScale);

            todo.IsVisible = false;
            todo.IsCollapsed = true;
            unscheduledButtons = Descendants<Button>(boardWindow)
                .Where(button =>
                    AutomationProperties.GetName(button).StartsWith(
                        Strings.Get("TodoBoardTimelineUnscheduledAutomationPrefix"),
                        StringComparison.Ordinal))
                .ToList();
            unscheduledButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain(boardWindow.Dispatcher);
            Assert.True(todo.IsVisible);
            Assert.False(todo.IsCollapsed);

            controller.PaperCommands.UpdateTodo(
                new UpdateTodoRequest
                {
                    PaperId = todo.Id,
                    TodoId = items[2].Id,
                    Planning = new TodoPlanningUpdate(
                        DateOnly.FromDateTime(DateTime.Today),
                        null)
                },
                PaperOperationContext.Mcp());
            Drain(boardWindow.Dispatcher);
            Assert.Equal(DateOnly.FromDateTime(DateTime.Today), items[2].PlannedStartDate);
            Assert.Equal(TodoBoardViews.Timeline, board.BoardView);
            var planningNames = Descendants<Border>(boardWindow)
                .Select(AutomationProperties.GetName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
            Assert.True(
                planningNames.Any(name => name.Contains("Task 3", StringComparison.Ordinal)),
                $"Expected Task 3 planning bar. Visible names: {string.Join(" | ", planningNames)}");
            Assert.Equal(
                4,
                Descendants<Button>(boardWindow).Count(button =>
                    AutomationProperties.GetName(button).StartsWith(
                        Strings.Get("TodoBoardTimelineUnscheduledAutomationPrefix"),
                        StringComparison.Ordinal)));
        }
        finally
        {
            if (boardWindow is { IsClosed: false })
            {
                boardWindow.CloseForReal(saveBeforeClose: false);
            }
            controller?.Dispose();
            application?.Shutdown();
            stateFile.Restore();
            backupFile.Restore();
        }
    }

    private static IReadOnlyList<Border> CalendarTaskBars(DependencyObject root) =>
        Descendants<Border>(root)
            .Where(border =>
                AutomationProperties.GetName(border).Contains(
                    "Task ",
                    StringComparison.Ordinal))
            .ToList();

    private static IReadOnlyList<Button> PopupTaskButtons() =>
        PresentationSource.CurrentSources
            .Cast<PresentationSource>()
            .Select(source => source.RootVisual)
            .OfType<DependencyObject>()
            .SelectMany(Descendants<Button>)
            .Where(button =>
                AutomationProperties.GetName(button).Contains(
                    "Smoke tasks",
                    StringComparison.Ordinal))
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
