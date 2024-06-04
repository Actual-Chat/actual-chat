
using ActualChat.MLSearch.Indexing.Initializer;
using ActualLab.Resilience;

namespace ActualChat.MLSearch.UnitTests.Indexing.Initializer;

public partial class ChatIndexInitializerShardTests
{
    private readonly ChatIndexInitializerShard.RetrySettings _scheduleJobRetrySettings =
        new (3, RetryDelaySeq.Exp(0.3, 3), TransiencyResolvers.PreferTransient);
    [Fact]
    public async Task ScheduleJobMethodWaitsForSemaphoreSlot()
    {
        const int maxConcurrency = 5;
        var chatInfo = new ChatIndexInitializerShard.ChatInfo(ChatId.None, 0);
        var cursor = new ChatIndexInitializerShard.Cursor(333);
        var state = new ChatIndexInitializerShard.SharedState(cursor, maxConcurrency);
        var commander = MockCommander().Object;
        var clock = Mock.Of<IMomentClock>();
        var log = Mock.Of<ILogger>();
        foreach (var _ in Enumerable.Range(0, maxConcurrency)) {
            // We can successfully start up to maxConcurrency task
            await ChatIndexInitializerShard.ScheduleIndexingJobAsync(
                    chatInfo, state, _scheduleJobRetrySettings, commander, clock, log, CancellationToken.None)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(1));
        }
        var nextJob = ChatIndexInitializerShard.ScheduleIndexingJobAsync(
            chatInfo, state, _scheduleJobRetrySettings, commander, clock, log, CancellationToken.None);
        Assert.False(nextJob.IsCompleted);
        // Let's free one semaphore slot
        state.Semaphore.Release();
        // Now our job must complete successfully
        await nextJob.AsTask().WaitAsync(TimeSpan.FromSeconds(1));
    }

    private static Mock<ICommander> MockCommander()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory
            .Setup(x => x.CreateScope())
            .Returns(Mock.Of<IServiceScope>());
        var services = new Mock<IServiceProvider>();
        services
            .Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(scopeFactory.Object);
        var commander = new Mock<ICommander>();
        commander
            .SetupGet(x => x.Services)
            .Returns(services.Object);
        commander
            .Setup(x => x.Run(
                It.IsAny<CommandContext>(),
                It.IsAny<CancellationToken>()))
            .Returns<CommandContext, CancellationToken>(
                (context, ct) => {
                    context.TryComplete(ct);
                    return Task.CompletedTask;
                });
        return commander;
    }

}
