namespace PaperTodo.Tests;

public sealed class TodoNoteExitDraftBatchTests
{
    [Fact]
    public void Decisions_are_only_ready_after_every_participant_resolves()
    {
        var batch = new TodoNoteExitDraftBatch(["paper-a", "paper-b"]);

        Assert.Equal("paper-a", batch.CurrentParticipantId);
        Assert.Equal(
            TodoNoteExitBatchTransition.Continue,
            batch.Record(TodoNoteDraftResolution.Save));
        Assert.Equal("paper-b", batch.CurrentParticipantId);
        Assert.Empty(batch.ApprovedDecisions);

        Assert.Equal(
            TodoNoteExitBatchTransition.Ready,
            batch.Record(TodoNoteDraftResolution.Discard));
        Assert.Equal(
            new[]
            {
                new TodoNoteExitDraftDecision("paper-a", TodoNoteDraftResolution.Save),
                new TodoNoteExitDraftDecision("paper-b", TodoNoteDraftResolution.Discard)
            },
            batch.ApprovedDecisions);
    }

    [Fact]
    public void Any_cancel_aborts_the_whole_batch_without_approved_decisions()
    {
        var batch = new TodoNoteExitDraftBatch(["paper-a", "paper-b"]);
        batch.Record(TodoNoteDraftResolution.Save);

        Assert.Equal(
            TodoNoteExitBatchTransition.Cancelled,
            batch.Record(TodoNoteDraftResolution.Cancel));
        Assert.Empty(batch.ApprovedDecisions);
        Assert.Null(batch.CurrentParticipantId);
    }

    [Fact]
    public void Failed_persistence_rolls_back_every_staged_note_and_timestamp()
    {
        var originalCreatedAt = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var first = new PaperItem
        {
            Id = "task-a",
            Text = "Task A",
            Note = "original A",
            CreatedAt = originalCreatedAt
        };
        var second = new PaperItem
        {
            Id = "task-b",
            Note = "",
            CreatedAt = default
        };
        var transaction = new TodoNoteExitSaveTransaction(
        [
            new TodoNoteExitSaveMutation(first, "draft A"),
            new TodoNoteExitSaveMutation(second, "draft B")
        ]);

        Assert.False(transaction.TryPersist(() =>
        {
            Assert.Equal("draft A", first.Note);
            Assert.Equal("draft B", second.Note);
            Assert.NotEqual(default, second.CreatedAt);
            return false;
        }));

        Assert.Equal("original A", first.Note);
        Assert.Equal(originalCreatedAt, first.CreatedAt);
        Assert.Equal("", second.Note);
        Assert.Equal(default, second.CreatedAt);
    }

    [Fact]
    public void Successful_persistence_keeps_the_whole_staged_batch()
    {
        var first = new PaperItem { Text = "Task A", Note = "original A" };
        var second = new PaperItem { Text = "Task B", Note = "original B" };
        var transaction = new TodoNoteExitSaveTransaction(
        [
            new TodoNoteExitSaveMutation(first, "draft A"),
            new TodoNoteExitSaveMutation(second, "draft B")
        ]);

        Assert.True(transaction.TryPersist(() => true));

        Assert.Equal("draft A", first.Note);
        Assert.Equal("draft B", second.Note);
    }
}
