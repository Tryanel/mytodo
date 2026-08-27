using System.Windows;

namespace PaperTodo;

public sealed partial class AppController
{
    private TodoNoteExitDraftBatch? _todoNoteExitDraftBatch;
    private Dictionary<string, PaperWindow>? _todoNoteExitDraftParticipants;

    private bool BeginTodoNoteExitDraftTransaction()
    {
        if (_todoNoteExitDraftBatch != null)
        {
            return true;
        }

        var participants = State.Papers
            .Select(paper => _windows.TryGetValue(paper.Id, out var window)
                ? (paper.Id, Window: window)
                : default)
            .Where(entry =>
                entry.Window != null &&
                entry.Window.HasDirtyTodoNoteEditor)
            .ToList();
        if (participants.Count == 0)
        {
            return false;
        }

        _todoNoteExitDraftParticipants = participants.ToDictionary(
            entry => entry.Id,
            entry => entry.Window!,
            StringComparer.Ordinal);
        _todoNoteExitDraftBatch = new TodoNoteExitDraftBatch(
            participants.Select(entry => entry.Id));
        RequestCurrentTodoNoteExitDecision();
        return true;
    }

    private void RequestCurrentTodoNoteExitDecision()
    {
        var batch = _todoNoteExitDraftBatch;
        var participants = _todoNoteExitDraftParticipants;
        var participantId = batch?.CurrentParticipantId;
        if (batch == null ||
            participants == null ||
            participantId == null ||
            !participants.TryGetValue(participantId, out var window))
        {
            CancelTodoNoteExitDraftTransaction();
            return;
        }

        window.RequestTodoNoteExitDecision(resolution =>
        {
            if (!ReferenceEquals(batch, _todoNoteExitDraftBatch))
            {
                return;
            }

            switch (batch.Record(resolution))
            {
                case TodoNoteExitBatchTransition.Continue:
                    RequestCurrentTodoNoteExitDecision();
                    break;
                case TodoNoteExitBatchTransition.Ready:
                    CommitTodoNoteExitDraftTransaction();
                    break;
                default:
                    CancelTodoNoteExitDraftTransaction();
                    break;
            }
        });
    }

    private void CommitTodoNoteExitDraftTransaction()
    {
        var batch = _todoNoteExitDraftBatch;
        var participants = _todoNoteExitDraftParticipants;
        if (batch == null || participants == null)
        {
            return;
        }

        var saveMutations = new List<TodoNoteExitSaveMutation>();
        foreach (var decision in batch.ApprovedDecisions)
        {
            if (!participants.TryGetValue(decision.ParticipantId, out var window) ||
                !window.PrepareTodoNoteExitDecision(
                    decision.Resolution,
                    out var saveMutation))
            {
                CancelTodoNoteExitDraftTransaction();
                window?.InvalidateTodoNoteEditorIfTargetMissing();
                return;
            }
            if (saveMutation != null)
            {
                saveMutations.Add(saveMutation);
            }
        }

        var saveTransaction = new TodoNoteExitSaveTransaction(saveMutations);
        if (!saveTransaction.TryPersist(() => TrySaveNow(sync: true)))
        {
            CancelTodoNoteExitDraftTransaction();
            MessageBox.Show(
                Strings.Get("ExitSaveFailureMessage"),
                Strings.Get("SaveFailureTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _todoNoteExitDraftBatch = null;
        _todoNoteExitDraftParticipants = null;
        foreach (var decision in batch.ApprovedDecisions)
        {
            if (participants.TryGetValue(decision.ParticipantId, out var window))
            {
                window.CommitTodoNoteExitDecision(decision.Resolution);
            }
        }
        CompleteNormalExit(stateAlreadySaved: true);
    }

    private void CancelTodoNoteExitDraftTransaction()
    {
        var participants = _todoNoteExitDraftParticipants;
        _todoNoteExitDraftBatch = null;
        _todoNoteExitDraftParticipants = null;
        if (participants == null)
        {
            return;
        }
        foreach (var window in participants.Values)
        {
            window.CancelTodoNoteExitDecision();
        }
    }
}
