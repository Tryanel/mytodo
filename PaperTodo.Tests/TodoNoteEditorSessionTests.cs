namespace PaperTodo.Tests;

public sealed class TodoNoteEditorSessionTests
{
    [Fact]
    public void Clean_session_switches_targets_without_a_decision()
    {
        var session = Session("a", "Alpha", "first");

        var transition = session.RequestSwitch(Target("b", "Beta", "second"));

        Assert.Equal(TodoNoteSessionTransition.TargetChanged, transition);
        Assert.Equal("b", session.Target.ItemId);
        Assert.Equal("second", session.Draft);
        Assert.False(session.IsDirty);
        Assert.Equal(TodoNoteDraftIntent.None, session.PendingIntent);
    }

    [Fact]
    public void Dirty_switch_requires_a_decision_without_changing_the_active_draft()
    {
        var session = Session("a", "Alpha", "first");
        session.SetDraft("unfinished");

        var transition = session.RequestSwitch(Target("b", "Beta", "second"));

        Assert.Equal(TodoNoteSessionTransition.DecisionRequired, transition);
        Assert.Equal("a", session.Target.ItemId);
        Assert.Equal("unfinished", session.Draft);
        Assert.Equal(TodoNoteDraftIntent.SwitchTarget, session.PendingIntent);
        Assert.Equal("b", session.PendingTarget?.ItemId);
    }

    [Fact]
    public void Cancel_switch_preserves_the_original_target_and_dirty_draft()
    {
        var session = Session("a", "Alpha", "first");
        session.SetDraft("unfinished");
        session.RequestSwitch(Target("b", "Beta", "second"));

        var transition = session.ResolvePending(TodoNoteDraftResolution.Cancel);

        Assert.Equal(TodoNoteSessionTransition.None, transition);
        Assert.Equal("a", session.Target.ItemId);
        Assert.Equal("unfinished", session.Draft);
        Assert.True(session.IsDirty);
        Assert.Equal(TodoNoteDraftIntent.None, session.PendingIntent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Save_or_discard_completes_a_pending_switch(int resolutionValue)
    {
        var resolution = resolutionValue == 0
            ? TodoNoteDraftResolution.Save
            : TodoNoteDraftResolution.Discard;
        var session = Session("a", "Alpha", "first");
        session.SetDraft("unfinished");
        session.RequestSwitch(Target("b", "Beta", "second"));

        var transition = session.ResolvePending(resolution);

        Assert.Equal(TodoNoteSessionTransition.TargetChanged, transition);
        Assert.Equal("b", session.Target.ItemId);
        Assert.Equal("second", session.Draft);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Dirty_close_requires_a_decision_and_continue_editing_preserves_the_session()
    {
        var session = Session("a", "Alpha", "first");
        session.SetDraft("unfinished");

        Assert.Equal(
            TodoNoteSessionTransition.DecisionRequired,
            session.RequestClose());
        Assert.Equal(TodoNoteDraftIntent.Close, session.PendingIntent);

        Assert.Equal(
            TodoNoteSessionTransition.None,
            session.ResolvePending(TodoNoteDraftResolution.Cancel));
        Assert.Equal("a", session.Target.ItemId);
        Assert.Equal("unfinished", session.Draft);
        Assert.True(session.IsDirty);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Save_or_discard_completes_a_pending_close(int resolutionValue)
    {
        var resolution = resolutionValue == 0
            ? TodoNoteDraftResolution.Save
            : TodoNoteDraftResolution.Discard;
        var session = Session("a", "Alpha", "first");
        session.SetDraft("unfinished");
        session.RequestClose();

        Assert.Equal(
            TodoNoteSessionTransition.Close,
            session.ResolvePending(resolution));
    }

    [Fact]
    public void Requesting_the_current_target_reactivates_without_losing_the_draft()
    {
        var session = Session("a", "Alpha", "first");
        session.SetDraft("unfinished");

        var transition = session.RequestSwitch(Target("a", "Renamed", "external"));

        Assert.Equal(TodoNoteSessionTransition.Reactivate, transition);
        Assert.Equal("unfinished", session.Draft);
        Assert.Equal("a", session.Target.ItemId);
    }

    private static TodoNoteEditorSession Session(
        string itemId,
        string taskText,
        string note) =>
        new(Target(itemId, taskText, note));

    private static TodoNoteEditorTarget Target(
        string itemId,
        string taskText,
        string note) =>
        new(itemId, taskText, note);
}
