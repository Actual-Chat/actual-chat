using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.Initializer;
using ActualChat.Performance;

namespace ActualChat.MLSearch.UnitTests.Indexing.Initializer;

public partial class ChatIndexInitializerShardTests
{
    private readonly TimeSpan _stallTimeout = TimeSpan.FromSeconds(101);

    [Fact]
    public async Task UpdateCursorMethodDoesNothingIfNoJobCompletedSincePreviousRun()
    {
        const int eventCount = 123;
        var now = new Moment(DateTime.Now);
        var state = new ChatIndexInitializerShard.SharedState(_fakeCursor, 1) {
            EventCount = eventCount,
            PrevEventCount = eventCount,
        };

        var cursorStates = Mock.Of<ICursorStates<ChatIndexInitializerShard.Cursor>>();
        var log = Mock.Of<ILogger>();
        var isExitWithNoUpdates = false;
        var tracer = new Tracer("TestTracer", tp => {
            if (tp.Label==ChatIndexInitializerShard.UpdateCursorStages.NoUpdates) {
                isExitWithNoUpdates = true;
            }
        });
        await ChatIndexInitializerShard.UpdateCursorAsync(now, state, _stallTimeout, cursorStates, log, tracer, CancellationToken.None);

        Assert.True(isExitWithNoUpdates);
    }

    [Theory]
    [InlineData(888, 999, 1111)]
    [InlineData(888, 1111, 999)]
    [InlineData(1111, 888, 999)]
    public async Task UpdateCursorMethodAdvancesCursorUpToThePointWhereAllJobsCompleted(
        long chat1Version, long chat2Version, long maxVersion
    )
    {
        const int eventCount = 123;
        var now = new Moment(DateTime.Now);
        var cursor = new ChatIndexInitializerShard.Cursor(maxVersion);
        var state = new ChatIndexInitializerShard.SharedState(cursor, 1) {
            EventCount = eventCount,
            PrevEventCount = 0,
        };
        var chatId1 = new ChatId(Generate.Option);
        var chatId2 = new ChatId(Generate.Option);
        state.ScheduledJobs[chatId1] = (chat1Version, now);
        state.ScheduledJobs[chatId2] = (chat2Version, now);

        var cursorStates = new Mock<ICursorStates<ChatIndexInitializerShard.Cursor>>();
        cursorStates
            .Setup(x => x.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<ChatIndexInitializerShard.Cursor>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var log = LogMock.Create<ChatIndexInitializerShard>();

        await ChatIndexInitializerShard.UpdateCursorAsync(
            now, state, _stallTimeout, cursorStates.Object, log.Object, cancellationToken: CancellationToken.None);

        var expectedVersion = (new [] { chat1Version, chat2Version, maxVersion + 1 }).Min();
        Assert.Equal(eventCount, state.PrevEventCount);
        cursorStates.Verify(
            x => x.SaveAsync(
                It.IsAny<string>(),
                It.Is<ChatIndexInitializerShard.Cursor>(c => c.LastVersion == expectedVersion),
                It.IsAny<CancellationToken>()),
            Times.Once);

        log.Verify(
            LogMock.GetLogMethodExpression<ChatIndexInitializerShard>(LogLevel.Information),
            Times.Once);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 5)]
    [InlineData(5, 7)]
    public async Task UpdateCursorMethodEvictsStallJobsAfterTimeout(int chat1Delay, int chat2Delay)
    {
        const int eventCount = 123;
        var now = new Moment(DateTime.Now);
        var stallTimeout = TimeSpan.FromSeconds(3);
        var cursor = new ChatIndexInitializerShard.Cursor(0);
        var state = new ChatIndexInitializerShard.SharedState(cursor, 5) {
            EventCount = eventCount,
            PrevEventCount = 0,
        };
        var chatId1 = new ChatId(Generate.Option);
        var chatId2 = new ChatId(Generate.Option);
        state.ScheduledJobs[chatId1] = (1, now - TimeSpan.FromSeconds(chat1Delay));
        state.ScheduledJobs[chatId2] = (1, now - TimeSpan.FromSeconds(chat2Delay));
        await state.Semaphore.WaitAsync();
        await state.Semaphore.WaitAsync();

        var expectedStallCount = state.ScheduledJobs.Values.Count(info => {
            var (_, moment) = info;
            return moment <= now - stallTimeout;
        });
        var expectedSemaphoreSlots = state.Semaphore.CurrentCount + expectedStallCount;

        var cursorStates = Mock.Of<ICursorStates<ChatIndexInitializerShard.Cursor>>();
        var log = LogMock.Create<ChatIndexInitializerShard>();

        await ChatIndexInitializerShard.UpdateCursorAsync(
            now, state, stallTimeout, cursorStates, log.Object, cancellationToken: CancellationToken.None);

        log.Verify(
            LogMock.GetLogMethodExpression<ChatIndexInitializerShard>(LogLevel.Warning),
            Times.Exactly(expectedStallCount));

        Assert.Equal(expectedSemaphoreSlots, state.Semaphore.CurrentCount);
    }


    [Fact]
    public async Task UpdateCursorMethodWorksCorrectlyIfJobCompletesWhileEvicting()
    {
        const int eventCount = 123;
        const long chatDelay = 5;
        var now = new Moment(DateTime.Now);
        var stallTimeout = TimeSpan.FromSeconds(3);
        var cursor = new ChatIndexInitializerShard.Cursor(0);
        var state = new ChatIndexInitializerShard.SharedState(cursor, 5) {
            EventCount = eventCount,
            PrevEventCount = 0,
        };
        var chatId1 = new ChatId(Generate.Option);
        var chatId2 = new ChatId(Generate.Option);
        state.ScheduledJobs[chatId1] = (1, now - TimeSpan.FromSeconds(chatDelay));
        state.ScheduledJobs[chatId2] = (1, now - TimeSpan.FromSeconds(chatDelay));
        await state.Semaphore.WaitAsync();
        await state.Semaphore.WaitAsync();

        var cursorStates = Mock.Of<ICursorStates<ChatIndexInitializerShard.Cursor>>();
        var log = LogMock.Create<ChatIndexInitializerShard>();
        var tracer = new Tracer("TestTracer", tp => {
            if (tp.Label==ChatIndexInitializerShard.UpdateCursorStages.EvictStallJobs) {
                // Emulate stall job completion
                state.ScheduledJobs.TryRemove(chatId1, out var _);
                state.Semaphore.Release();
            }
        });

        await ChatIndexInitializerShard.UpdateCursorAsync(
            now, state, stallTimeout, cursorStates, log.Object, tracer, CancellationToken.None);

        log.Verify(
            LogMock.GetLogMethodExpression<ChatIndexInitializerShard>(LogLevel.Warning),
            Times.Once);
    }

    [Fact]
    public async Task UpdateCursorMethodDoesNotLogCancellationError()
    {
        var now = new Moment(DateTime.Now);
        var stallTimeout = TimeSpan.FromSeconds(3);
        var cursor = new ChatIndexInitializerShard.Cursor(0);
        var state = new ChatIndexInitializerShard.SharedState(cursor, 5) {
            EventCount = 1,
            PrevEventCount = 0,
        };

        var cancellationSource = new CancellationTokenSource();
        var log = LogMock.Create<ChatIndexInitializer>();
        var cursorStates = new Mock<ICursorStates<ChatIndexInitializerShard.Cursor>>();
        cursorStates
            .Setup(x => x.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<ChatIndexInitializerShard.Cursor>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ChatIndexInitializerShard.Cursor, CancellationToken>(static (_, _, ct) =>
                ActualLab.Async.TaskExt.NewNeverEndingUnreferenced().WaitAsync(TimeSpan.FromSeconds(1), ct));

        var updateCursorTask = ChatIndexInitializerShard.UpdateCursorAsync(
                now, state, stallTimeout, cursorStates.Object, log.Object, cancellationToken: cancellationSource.Token)
            .AsTask();
        await cancellationSource.CancelAsync();

        var e = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await updateCursorTask);
        Assert.True(e.IsCancellationOf(cancellationSource.Token));
        log.Verify(
            LogMock.GetLogMethodExpression<ChatIndexInitializer>(LogLevel.Error),
            Times.Never);
    }

    [Fact]
    public async Task UpdateCursorMethodLogsErrors()
    {
        var now = new Moment(DateTime.Now);
        var stallTimeout = TimeSpan.FromSeconds(3);
        var cursor = new ChatIndexInitializerShard.Cursor(0);
        var state = new ChatIndexInitializerShard.SharedState(cursor, 5) {
            EventCount = 1,
            PrevEventCount = 0,
        };

        var log = LogMock.Create<ChatIndexInitializer>();
        var cursorStates = new Mock<ICursorStates<ChatIndexInitializerShard.Cursor>>();
        cursorStates
            .Setup(x => x.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<ChatIndexInitializerShard.Cursor>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ChatIndexInitializerShard.Cursor, CancellationToken>(static (_, _, _) =>
                Task.FromException(new UniqueException()));

        await Assert.ThrowsAsync<UniqueException>(
            async () => await ChatIndexInitializerShard.UpdateCursorAsync(
                now, state, stallTimeout, cursorStates.Object, log.Object, cancellationToken: CancellationToken.None));

        log.Verify(
            LogMock.GetLogMethodExpression<ChatIndexInitializer>(LogLevel.Error),
            Times.Once);
    }
}
