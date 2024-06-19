using ActualChat.MLSearch.Indexing.Initializer;

namespace ActualChat.MLSearch.UnitTests.Indexing.Initializer;

public partial class ChatIndexInitializerShardTests
{
    private readonly ChatIndexInitializerShard.Cursor _fakeCursor = new (333);

    [Fact]
    public void HandleCompletionEventMethodIncrementsEventCounter()
    {
        var evt = new MLSearch_TriggerChatIndexingCompletion(ChatId.None);
        var state = new ChatIndexInitializerShard.SharedState(_fakeCursor, 1);

        var expected = state.EventCount + 1;
        ChatIndexInitializerShard.HandleCompletionEvent(evt, state);
        Assert.Equal(expected, state.EventCount);
    }

    [Fact]
    public void HandleCompletionEventMethodRemovesJobInfoFromState()
    {
        var chatId1 = new ChatId(Generate.Option);
        var chatId2 = new ChatId(Generate.Option);
        var state = new ChatIndexInitializerShard.SharedState(_fakeCursor, 2);

        // Emulate two running jobs
        state.ScheduledJobs[chatId1] = (1, Moment.MinValue);
        state.ScheduledJobs[chatId2] = (1, Moment.MinValue);
        state.Semaphore.Wait();
        state.Semaphore.Wait();

        var evt = new MLSearch_TriggerChatIndexingCompletion(chatId1);
        ChatIndexInitializerShard.HandleCompletionEvent(evt, state);

        Assert.False(state.ScheduledJobs.ContainsKey(chatId1));
        Assert.True(state.ScheduledJobs.ContainsKey(chatId2));
    }

    [Fact]
    public void HandleCompletionEventMethodReleasesSemaphoreSlotIfJobPresent()
    {
        var chatId1 = new ChatId(Generate.Option);
        var chatId2 = new ChatId(Generate.Option);
        var state = new ChatIndexInitializerShard.SharedState(_fakeCursor, 5);

        // Emulate two running jobs
        state.ScheduledJobs[chatId1] = (1, Moment.MinValue);
        state.ScheduledJobs[chatId2] = (1, Moment.MinValue);
        state.Semaphore.Wait();
        state.Semaphore.Wait();

        var expectedSlots = state.Semaphore.CurrentCount + 1;

        foreach (var _ in Enumerable.Range(0, 2)) {
            // Handle completion event for the same chat twice
            var evt = new MLSearch_TriggerChatIndexingCompletion(chatId1);
            ChatIndexInitializerShard.HandleCompletionEvent(evt, state);
        }
        // Verify only one semaphore slot is released
        Assert.Equal(expectedSlots, state.Semaphore.CurrentCount);
    }

    [Fact]
    public void HandleCompletionEventMethodUpdatesMaxChatVersion()
    {
        long[] versions = [100, 300, 200, 500, 400];
        var state = new ChatIndexInitializerShard.SharedState(_fakeCursor, 5);
        foreach (var version in versions) {
            var chatId = new ChatId(Generate.Option);
            state.ScheduledJobs[chatId] = (version, Moment.MinValue);
            state.Semaphore.Wait();
        }
        state.MaxVersion = 0;
        while (!state.ScheduledJobs.IsEmpty) {
            var chatId = state.ScheduledJobs.Keys.First();
            var evt = new MLSearch_TriggerChatIndexingCompletion(chatId);
            ChatIndexInitializerShard.HandleCompletionEvent(evt, state);
        }
        Assert.Equal(versions.Max(), state.MaxVersion);
    }
}
