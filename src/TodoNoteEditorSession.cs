namespace PaperTodo;

internal sealed record TodoNoteEditorTarget(
    string ItemId,
    string TaskText,
    string Note);

internal enum TodoNoteDraftIntent
{
    None,
    SwitchTarget,
    Close
}

internal enum TodoNoteDraftResolution
{
    Save,
    Discard,
    Cancel
}

internal enum TodoNoteSessionTransition
{
    None,
    Reactivate,
    TargetChanged,
    DecisionRequired,
    Close
}

/// <summary>
/// Owns the identity and draft transition rules for one todo paper's modeless note editor.
/// The WPF surface presents this state but does not duplicate the switch/close policy.
/// </summary>
internal sealed class TodoNoteEditorSession
{
    private string _originalNote;

    public TodoNoteEditorSession(TodoNoteEditorTarget target)
    {
        Target = Normalize(target);
        _originalNote = Target.Note;
        Draft = _originalNote;
    }

    public TodoNoteEditorTarget Target { get; private set; }
    public string Draft { get; private set; }
    public bool IsDirty => !string.Equals(
        Draft,
        _originalNote,
        StringComparison.Ordinal);
    public TodoNoteDraftIntent PendingIntent { get; private set; }
    public TodoNoteEditorTarget? PendingTarget { get; private set; }

    public void SetDraft(string? draft)
    {
        Draft = draft ?? "";
    }

    public TodoNoteSessionTransition RequestSwitch(TodoNoteEditorTarget target)
    {
        target = Normalize(target);
        if (string.Equals(Target.ItemId, target.ItemId, StringComparison.Ordinal))
        {
            ClearPending();
            return TodoNoteSessionTransition.Reactivate;
        }

        if (IsDirty)
        {
            PendingIntent = TodoNoteDraftIntent.SwitchTarget;
            PendingTarget = target;
            return TodoNoteSessionTransition.DecisionRequired;
        }

        Adopt(target);
        return TodoNoteSessionTransition.TargetChanged;
    }

    public TodoNoteSessionTransition RequestClose()
    {
        if (!IsDirty)
        {
            ClearPending();
            return TodoNoteSessionTransition.Close;
        }

        PendingIntent = TodoNoteDraftIntent.Close;
        PendingTarget = null;
        return TodoNoteSessionTransition.DecisionRequired;
    }

    /// <summary>
    /// Resolves the current intent after the caller has successfully persisted the draft when
    /// <paramref name="resolution"/> is Save.
    /// </summary>
    public TodoNoteSessionTransition ResolvePending(
        TodoNoteDraftResolution resolution)
    {
        if (PendingIntent == TodoNoteDraftIntent.None)
        {
            return TodoNoteSessionTransition.None;
        }

        if (resolution == TodoNoteDraftResolution.Cancel)
        {
            ClearPending();
            return TodoNoteSessionTransition.None;
        }

        var intent = PendingIntent;
        var target = PendingTarget;
        ClearPending();

        if (intent == TodoNoteDraftIntent.Close)
        {
            if (resolution == TodoNoteDraftResolution.Save)
            {
                _originalNote = Draft;
            }
            return TodoNoteSessionTransition.Close;
        }

        if (target == null)
        {
            return TodoNoteSessionTransition.None;
        }

        Adopt(target);
        return TodoNoteSessionTransition.TargetChanged;
    }

    public void MarkSaved()
    {
        _originalNote = Draft;
        Target = Target with { Note = Draft };
    }

    private void Adopt(TodoNoteEditorTarget target)
    {
        Target = target;
        _originalNote = target.Note;
        Draft = target.Note;
        ClearPending();
    }

    private void ClearPending()
    {
        PendingIntent = TodoNoteDraftIntent.None;
        PendingTarget = null;
    }

    private static TodoNoteEditorTarget Normalize(TodoNoteEditorTarget target) =>
        target with
        {
            ItemId = target.ItemId ?? "",
            TaskText = target.TaskText ?? "",
            Note = target.Note ?? ""
        };
}
