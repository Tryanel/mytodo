using System.Windows.Threading;

namespace PaperTodo;

public sealed partial class AppController
{
    internal void OpenTodoBoardPaper()
    {
        if (IsExiting)
        {
            return;
        }

        var board = State.Papers.FirstOrDefault(paper =>
            paper.Type == PaperTypes.Board) ??
            CreatePaper(PaperTypes.Board, show: false);
        if (board != null)
        {
            ShowTodoBoardPaper(board);
        }
    }

    private void ShowTodoBoardPaper(PaperData board)
    {
        SetPaperCollapsedRuntime(
            board,
            collapsed: false,
            animate: false,
            saveGeometry: false);
        ShowPaper(board);
        if (!_windows.TryGetValue(board.Id, out var window))
        {
            return;
        }

        window.RefreshTodoBoardForExternalChange();
        ForceWindowToFront(window);
        RefreshTrayMenu();
        MarkDirty();
    }

    private void NotifyTodoBoardStateChanged()
    {
        foreach (var board in State.Papers.Where(paper =>
            paper.Type == PaperTypes.Board))
        {
            if (_windows.TryGetValue(board.Id, out var window))
            {
                window.ScheduleTodoBoardRefresh();
            }
        }
    }

    internal void OpenTodoFromBoard(string paperId, string todoId)
    {
        var paper = FindPaper(paperId);
        if (paper == null || paper.Type != PaperTypes.Todo)
        {
            return;
        }

        SetPaperCollapsedRuntime(
            paper,
            collapsed: false,
            animate: false,
            saveGeometry: false);
        ShowPaper(paper);
        if (!_windows.TryGetValue(paper.Id, out var window))
        {
            return;
        }

        ForceWindowToFront(window);
        _ = window.Dispatcher.InvokeAsync(
            () => window.FocusTodoFromBoard(todoId),
            DispatcherPriority.ContextIdle);
        RefreshTrayMenu();
        MarkDirty();
    }
}
