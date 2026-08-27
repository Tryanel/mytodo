namespace PaperTodo;

internal sealed record TodoNoteExitDraftDecision(
    string ParticipantId,
    TodoNoteDraftResolution Resolution);

internal enum TodoNoteExitBatchTransition
{
    Continue,
    Ready,
    Cancelled
}

/// <summary>
/// Stages exit decisions until every dirty editor resolves. A cancellation publishes no approved
/// decisions, so the UI coordinator can restore every participant before shutdown begins.
/// </summary>
internal sealed class TodoNoteExitDraftBatch
{
    private readonly IReadOnlyList<string> _participantIds;
    private readonly List<TodoNoteExitDraftDecision> _staged = [];
    private bool _finished;

    public TodoNoteExitDraftBatch(IEnumerable<string> participantIds)
    {
        _participantIds = participantIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public string? CurrentParticipantId =>
        _finished || _staged.Count >= _participantIds.Count
            ? null
            : _participantIds[_staged.Count];

    public IReadOnlyList<TodoNoteExitDraftDecision> ApprovedDecisions { get; private set; } =
        Array.Empty<TodoNoteExitDraftDecision>();

    public TodoNoteExitBatchTransition Record(TodoNoteDraftResolution resolution)
    {
        if (_finished || CurrentParticipantId == null)
        {
            throw new InvalidOperationException("The exit draft batch is already complete.");
        }

        if (resolution == TodoNoteDraftResolution.Cancel)
        {
            _finished = true;
            _staged.Clear();
            ApprovedDecisions = Array.Empty<TodoNoteExitDraftDecision>();
            return TodoNoteExitBatchTransition.Cancelled;
        }

        _staged.Add(new TodoNoteExitDraftDecision(
            CurrentParticipantId,
            resolution));
        if (_staged.Count < _participantIds.Count)
        {
            return TodoNoteExitBatchTransition.Continue;
        }

        _finished = true;
        ApprovedDecisions = _staged.ToArray();
        return TodoNoteExitBatchTransition.Ready;
    }
}
