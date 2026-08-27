namespace PaperTodo.Tests;

public sealed class TodoCompletionRecordPromptTests
{
    [Theory]
    [InlineData(0, false, true, true, true)]
    [InlineData(0, true, true, true, false)]
    [InlineData(0, true, false, true, false)]
    [InlineData(0, false, true, false, false)]
    [InlineData(1, false, true, true, false)]
    [InlineData(2, false, true, true, false)]
    public void Only_a_direct_single_checkbox_completion_offers_the_record_action(
        int originValue,
        bool wasDone,
        bool isDone,
        bool itemRemainsAvailable,
        bool expected)
    {
        var session = new TodoCompletionRecordPromptSession();

        Assert.Equal(
            expected,
            session.TryOffer(
                "task-1",
                wasDone,
                isDone,
                itemRemainsAvailable,
                (TodoCompletionRecordOrigin)originValue));
        Assert.Equal(expected ? "task-1" : null, session.ItemId);
    }

    [Fact]
    public void Session_keeps_stable_identity_and_clears_when_task_is_invalid()
    {
        var session = new TodoCompletionRecordPromptSession();
        Assert.True(session.TryOffer(
            "task-1",
            wasDone: false,
            isDone: true,
            itemRemainsAvailable: true,
            origin: TodoCompletionRecordOrigin.DirectCheckbox));

        Assert.False(session.DismissIfInvalid(itemId => itemId == "task-1"));
        Assert.Equal("task-1", session.ItemId);
        Assert.True(session.DismissIfInvalid(_ => false));
        Assert.Null(session.ItemId);
    }
}
