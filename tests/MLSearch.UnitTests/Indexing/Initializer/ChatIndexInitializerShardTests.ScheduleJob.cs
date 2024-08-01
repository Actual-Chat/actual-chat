
using ActualChat.MLSearch.Indexing.Initializer;
using ActualChat.Queues;
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
        var queues = MockQueues().Object;
        var clock = Mock.Of<MomentClock>();
        var log = Mock.Of<ILogger>();
        foreach (var _ in Enumerable.Range(0, maxConcurrency)) {
            // We can successfully start up to maxConcurrency task
            await ChatIndexInitializerShard.ScheduleIndexingJobAsync(
                    chatInfo, state, _scheduleJobRetrySettings, queues, clock, log, CancellationToken.None)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(1));
        }
        var nextJob = ChatIndexInitializerShard.ScheduleIndexingJobAsync(
            chatInfo, state, _scheduleJobRetrySettings, queues, clock, log, CancellationToken.None);
        Assert.False(nextJob.IsCompleted);
        // Let's free one semaphore slot
        state.Semaphore.Release();
        // Now our job must complete successfully
        await nextJob.AsTask().WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ScheduleJobMethodDoesNotLogCancellationError()
    {
        const int maxConcurrency = 5;
        var chatInfo = new ChatIndexInitializerShard.ChatInfo(ChatId.None, 0);
        var cursor = new ChatIndexInitializerShard.Cursor(333);
        var state = new ChatIndexInitializerShard.SharedState(cursor, maxConcurrency);

        var clock = Mock.Of<MomentClock>();
        var cancellationSource = new CancellationTokenSource();
        var log = LogMock.Create<ChatIndexInitializer>();
        var queues = MockQueues(static (_, ct) =>
            ActualLab.Async.TaskExt.NewNeverEndingUnreferenced().WaitAsync(TimeSpan.FromSeconds(1), ct));

        var scheduleTask = ChatIndexInitializerShard.ScheduleIndexingJobAsync(
                chatInfo, state, _scheduleJobRetrySettings, queues.Object, clock, log.Object, cancellationSource.Token)
            .AsTask();
        await cancellationSource.CancelAsync();

        var e = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await scheduleTask);
        Assert.True(e.IsCancellationOf(cancellationSource.Token));
        log.Verify(
            LogMock.GetLogMethodExpression<ChatIndexInitializer>(LogLevel.Error),
            Times.Never);
    }

    [Fact]
    public async Task ScheduleJobMethodLogsErrors()
    {
        const int maxConcurrency = 5;
        var chatInfo = new ChatIndexInitializerShard.ChatInfo(ChatId.None, 0);
        var cursor = new ChatIndexInitializerShard.Cursor(333);
        var state = new ChatIndexInitializerShard.SharedState(cursor, maxConcurrency);

        var clock = Mock.Of<MomentClock>();
        var log = LogMock.Create<ChatIndexInitializer>();
        var queues = MockQueues(static (_, _) => Task.FromException(new UniqueException()));

        ChatIndexInitializerShard.RetrySettings retrySettings =
            new (0, RetryDelaySeq.Exp(0.3, 3), TransiencyResolvers.PreferTransient);

        await Assert.ThrowsAsync<UniqueException>(
            async () => await ChatIndexInitializerShard.ScheduleIndexingJobAsync(
                chatInfo, state, retrySettings, queues.Object, clock, log.Object, CancellationToken.None));

        log.Verify(
            LogMock.GetLogMethodExpression<ChatIndexInitializer>(LogLevel.Error),
            Times.Once);
    }

    [Fact]
    public async Task ScheduleJobMethodRetriesOnError()
    {
        const int maxConcurrency = 1;
        const int attemptCount = 7;

        ChatIndexInitializerShard.RetrySettings retrySettings =
            new (attemptCount, RetryDelaySeq.Exp(0.3, 3, 0), TransiencyResolvers.PreferTransient);

        var chatInfo = new ChatIndexInitializerShard.ChatInfo(ChatId.None, 0);
        var cursor = new ChatIndexInitializerShard.Cursor(0);
        var state = new ChatIndexInitializerShard.SharedState(cursor, maxConcurrency);

        var observedDelays = new List<TimeSpan>();
        var clock = new Mock<MomentClock>();
        clock.Setup(x => x.Delay(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<TimeSpan, CancellationToken>((ts, _) => {
                observedDelays.Add(ts);
                return Task.CompletedTask;
            });

        var log = Mock.Of<ILogger>();
        var queues = MockQueues(static (_, _) => Task.FromException(new UniqueException()));

        await Assert.ThrowsAsync<UniqueException>(async () =>
            await ChatIndexInitializerShard.ScheduleIndexingJobAsync(
                chatInfo, state, retrySettings, queues.Object, clock.Object, log, CancellationToken.None));

        queues.Verify(x => x.GetSender(It.IsAny<QueueRef>()), Times.Exactly(retrySettings.AttemptCount));

        var expectedDelays = Enumerable.Range(1, retrySettings.AttemptCount - 1)
            .Select(attempt => retrySettings.RetryDelaySeq[attempt]);

        Assert.Equal(expectedDelays, observedDelays);
    }

    [Fact]
    public async Task ScheduleJobMethodAddsJobInfoToTheState()
    {
        const int maxConcurrency = 1;
        const int chatVersion = 55;
        var scheduleMoment = new Moment(DateTime.Now);

        var chatId = new ChatId(Generate.Option);
        var chatInfo = new ChatIndexInitializerShard.ChatInfo(chatId, chatVersion);
        var cursor = new ChatIndexInitializerShard.Cursor(0);
        var state = new ChatIndexInitializerShard.SharedState(cursor, maxConcurrency);

        var clock = new Mock<MomentClock>();
        clock.SetupGet(x => x.Now).Returns(scheduleMoment);
        var log = Mock.Of<ILogger>();
        var queues = MockQueues();
        await ChatIndexInitializerShard.ScheduleIndexingJobAsync(
            chatInfo, state, _scheduleJobRetrySettings, queues.Object, clock.Object, log, CancellationToken.None);

        Assert.True(state.ScheduledJobs.TryGetValue(chatId, out var jobInfo));
        var (version, moment) = jobInfo;
        Assert.Equal(chatVersion, version);
        Assert.Equal(scheduleMoment, moment);
    }

    [Fact]
    public async Task ScheduleJobMethodSendsCommandAsExpected()
    {
        var chatId = new ChatId(Generate.Option);
        var chatInfo = new ChatIndexInitializerShard.ChatInfo(chatId, 0);
        var cursor = new ChatIndexInitializerShard.Cursor(0);
        var state = new ChatIndexInitializerShard.SharedState(cursor, 1);

        var clock = Mock.Of<MomentClock>();
        var log = Mock.Of<ILogger>();

        ICommand? observedCommand = null;
        var queues = MockQueues((context, _) => {
            observedCommand = context.UntypedCommand;
            return Task.CompletedTask;
        });
        await ChatIndexInitializerShard.ScheduleIndexingJobAsync(
            chatInfo, state, _scheduleJobRetrySettings, queues.Object, clock, log, CancellationToken.None);

        Assert.True(observedCommand is MLSearch_TriggerChatIndexing { ChatId: var observedChatId } && chatId == observedChatId);
    }

    private static Mock<IQueues> MockQueues(Func<QueuedCommand, CancellationToken, Task>? action = null)
    {
        var queueRefResolver = new Mock<IQueueRefResolver>();
        queueRefResolver
            .Setup(x => x.GetQueueShardRef(It.IsAny<ICommand>(), It.IsAny<Requester>()))
            .Returns(new QueueShardRef(ShardScheme.EventQueue, 1));
        var services = new Mock<IServiceProvider>();
        services
            .Setup(x => x.GetService(typeof(IQueueRefResolver)))
            .Returns(queueRefResolver.Object);
        var queues = new Mock<IQueues>();
        queues
            .SetupGet(x => x.Services)
            .Returns(services.Object);

        var queueSender = new Mock<IQueueSender>();
        queueSender
            .Setup(x => x.Enqueue(
                It.IsAny<QueueShardRef>(),
                It.IsAny<QueuedCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns<QueueShardRef, QueuedCommand, CancellationToken>(
                (_, queuedCommand, ct) => {
                    return action?.Invoke(queuedCommand, ct) ?? Task.CompletedTask;
                }
            );
        queues
            .Setup(x => x.GetSender(It.IsAny<QueueRef>()))
            .Returns(queueSender.Object);
        return queues;
    }
}
