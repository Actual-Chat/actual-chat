using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.Initializer;
using ActualChat.Performance;
using ActualLab.Resilience;

namespace ActualChat.MLSearch.UnitTests.Indexing.Initializer;

public partial class ChatIndexInitializerShardTests(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task UseAsyncMethodRunsThreeFlowsWithExpectedParams()
    {
        var cancellationSource = new CancellationTokenSource();
        var scheduleJobSignal = new TaskCompletionSource();
        var completeJobSignal = new TaskCompletionSource();
        var updateCursorSignal = new TaskCompletionSource();

        var now = new Moment(1_999_000);
        var updateCursorInterval = TimeSpan.FromSeconds(123);
        var stallJobTimeout = TimeSpan.FromMinutes(123);
        var scheduleJobRetrySettings =
            new ChatIndexInitializerShard.RetrySettings(399,
                RetryDelaySeq.Exp(0.2, 10),
                TransiencyResolvers.PreferTransient);
        var chatToIndexId = new ChatId(Generate.Option);
        const int chatToIndexVersion = 999;
        const int maxConcurrency = 555;

        var mockScheduleJob = MockScheduleJobHandler((_, _, _, _, _, _, _) => {
            scheduleJobSignal.SetResult();
            return ValueTask.CompletedTask;
        });
        var mockCompleteJob = MockCompleteJobHandler((_, _) => completeJobSignal.SetResult());
        var mockUpdateCursor = MockUpdateCursorHandler((_, _, _, _, _, _, _) => {
            updateCursorSignal.SetResult();
            return ValueTask.CompletedTask;
        });

        // Setup clock to generate one event for the update cursor flow
        var delayCallCount = 0;
        var clock = new Mock<IMomentClock>();
        clock.Setup(x => x.Delay(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<TimeSpan, CancellationToken>((ts, _) => {
                if (ts == updateCursorInterval && delayCallCount++ == 0) {
                    return Task.CompletedTask;
                }
                return ActualLab.Async.TaskExt.NewNeverEndingUnreferenced().WaitAsync(cancellationSource.Token);
            });
        clock.SetupGet(x => x.Now).Returns(now);

        // Setup infinite chat sequence to provide single chat for the scheduling flow
        async IAsyncEnumerable<(ChatId, long)> GetChatsAsync([EnumeratorCancellation] CancellationToken cancellationToken) {
            yield return (chatToIndexId, chatToIndexVersion);
            await ActualLab.Async.TaskExt.NewNeverEndingUnreferenced().WaitAsync(cancellationToken);
        }
        var infiniteChatSequence = new Mock<IInfiniteChatSequence>();
        infiniteChatSequence
            .Setup(x => x.LoadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns<long, CancellationToken>((_, ct) => GetChatsAsync(ct));

        var commander = Mock.Of<ICommander>();
        var cursorStates = Mock.Of<ICursorStates<ChatIndexInitializerShard.Cursor>>();
        var log = Mock.Of<ILogger<ChatIndexInitializerShard>>();

        var initializerShard = new ChatIndexInitializerShard(
            clock.Object,
            commander,
            infiniteChatSequence.Object,
            cursorStates,
            log
        ) {
            OnScheduleJob = mockScheduleJob.Object,
            OnCompleteJob = mockCompleteJob.Object,
            OnUpdateCursor = mockUpdateCursor.Object,
            UpdateCursorInterval = updateCursorInterval,
            StallJobTimeout = stallJobTimeout,
            MaxConcurrency = maxConcurrency,
            ScheduleJobRetrySettings = scheduleJobRetrySettings,
        };

        // Run shard
        var useTask = initializerShard.UseAsync(cancellationSource.Token);

        // Trigger job completion event
        var completionEvt = new MLSearch_TriggerChatIndexingCompletion(new ChatId(Generate.Option));
        await initializerShard.PostAsync(completionEvt);

        // Wait for all flows to start
        await Task.WhenAll(scheduleJobSignal.Task, completeJobSignal.Task, updateCursorSignal.Task)
            .WaitAsync(TimeSpan.FromSeconds(1));

        // Trigger cancellation
        await cancellationSource.CancelAsync();

        // Wait for completion and check all flows properly cancelled
        var e = await Assert.ThrowsAnyAsync<Exception>(async () => await useTask);
        Assert.True(e.IsCancellationOf(cancellationSource.Token));
        // if (e is AggregateException aggregateException) {
        //     foreach (var innerException in aggregateException.InnerExceptions) {
        //         Assert.True(innerException.IsCancellationOf(cancellationSource.Token));
        //     }
        // }
        // else {
        //    Assert.True(e.IsCancellationOf(cancellationSource.Token));
        // }

        // Verify flows received expected arguments
        var states = new HashSet<ChatIndexInitializerShard.SharedState>();
        mockScheduleJob
            .Verify(handler => handler(
                It.Is<ChatIndexInitializerShard.ChatInfo>(x => x.ChatId == chatToIndexId && x.Version == chatToIndexVersion),
                It.Is<ChatIndexInitializerShard.SharedState>(x => states.Add(x) || true),
                It.Is<ChatIndexInitializerShard.RetrySettings>(x => x.AttemptCount == scheduleJobRetrySettings.AttemptCount),
                It.Is<ICommander>(x => x == commander),
                It.Is<IMomentClock>(x => x == clock.Object),
                It.Is<ILogger>(x => x == log),
                It.Is<CancellationToken>(x => x == cancellationSource.Token)));
        mockCompleteJob
            .Verify(handler => handler(
                It.Is<MLSearch_TriggerChatIndexingCompletion>(x => x == completionEvt),
                It.Is<ChatIndexInitializerShard.SharedState>(x => states.Add(x) || true)));
        mockUpdateCursor
            .Verify(handler => handler(
                It.Is<Moment>(x => x == now),
                It.Is<ChatIndexInitializerShard.SharedState>(x => states.Add(x) || true),
                It.Is<TimeSpan>(x => x == stallJobTimeout),
                It.Is<ICursorStates<ChatIndexInitializerShard.Cursor>>(x => x == cursorStates),
                It.Is<ILogger>(x => x == log),
                It.Is<Tracer?>(x => x == null),
                It.Is<CancellationToken>(x => x == cancellationSource.Token)));
        Assert.Single(states);
        Assert.Equal(maxConcurrency, states.First().Semaphore.CurrentCount);
    }

    [Fact]
    public async Task PostAsyncMethodWaitsIfInternalBufferIsAtCapacity()
    {
        const int maxBufferCapacity = 10;

        var infiniteChatSequence = new Mock<IInfiniteChatSequence>();
        infiniteChatSequence
            .Setup(x => x.LoadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns<long, CancellationToken>((_, _) => AsyncEnumerable.Empty<(ChatId, long)>());

        var initializerShard = new ChatIndexInitializerShard(
            Mock.Of<IMomentClock>(),
            Mock.Of<ICommander>(),
            infiniteChatSequence.Object,
            Mock.Of<ICursorStates<ChatIndexInitializerShard.Cursor>>(),
            Mock.Of<ILogger<ChatIndexInitializerShard>>()
        ) {
            OnScheduleJob = MockScheduleJobHandler().Object,
            OnCompleteJob = MockCompleteJobHandler().Object,
            OnUpdateCursor = MockUpdateCursorHandler(async (_, _, _, _, _, _, ct) => {
                await Task.Delay(25, ct);
            }).Object,
            InputBufferCapacity = maxBufferCapacity,
        };

        var postTasks = Enumerable.Range(0, maxBufferCapacity)
            .Select(_ => initializerShard.PostAsync(new MLSearch_TriggerChatIndexingCompletion(new ChatId(Generate.Option))).AsTask())
            .ToArray();
        // Ensure all posts up to buffer capacity are completed
        await Task.WhenAll(postTasks).WaitAsync(TimeSpan.FromSeconds(1));

        // Initiate over capacity Post call
        var overCapacityPostTask = initializerShard.PostAsync(new MLSearch_TriggerChatIndexingCompletion(new ChatId(Generate.Option)));
        Assert.False(overCapacityPostTask.IsCompleted);

        // Prepare using of shard
        var cancellationSource = new CancellationTokenSource();

        // Assert that over capacity call is still not completed
        Assert.False(overCapacityPostTask.IsCompleted);

        // Start using the shard
        var useTask = initializerShard.UseAsync(cancellationSource.Token);

        // Ensure our over capacity call is completed
        await overCapacityPostTask.AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        // Cancel execution to complete test
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => useTask.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    private static Mock<ChatIndexInitializerShard.ScheduleJobHandler> MockScheduleJobHandler(
        Func<
            ChatIndexInitializerShard.ChatInfo,
            ChatIndexInitializerShard.SharedState,
            ChatIndexInitializerShard.RetrySettings,
            ICommander,
            IMomentClock,
            ILogger,
            CancellationToken,
            ValueTask>? onCall = null
    )
    {
        var mock = new Mock<ChatIndexInitializerShard.ScheduleJobHandler>();
        mock.Setup(handler => handler(
                It.IsAny<ChatIndexInitializerShard.ChatInfo>(),
                It.IsAny<ChatIndexInitializerShard.SharedState>(),
                It.IsAny<ChatIndexInitializerShard.RetrySettings>(),
                It.IsAny<ICommander>(),
                It.IsAny<IMomentClock>(),
                It.IsAny<ILogger>(),
                It.IsAny<CancellationToken>()))
            .Returns(onCall ?? ((_, _, _, _, _, _, _) => ValueTask.CompletedTask));
        return mock;
    }

    private static Mock<ChatIndexInitializerShard.CompleteJobHandler> MockCompleteJobHandler(
        Action<MLSearch_TriggerChatIndexingCompletion, ChatIndexInitializerShard.SharedState>? onCall = null
    )
    {
        var mock = new Mock<ChatIndexInitializerShard.CompleteJobHandler>();
        mock.Setup(handler => handler(
                It.IsAny<MLSearch_TriggerChatIndexingCompletion>(),
                It.IsAny<ChatIndexInitializerShard.SharedState>()))
            .Callback(onCall ?? ((_, _) => {}));
        return mock;
    }

    private static Mock<ChatIndexInitializerShard.UpdateCursorHandler> MockUpdateCursorHandler(
        Func<Moment, ChatIndexInitializerShard.SharedState, TimeSpan,
            ICursorStates<ChatIndexInitializerShard.Cursor>, ILogger, Tracer?, CancellationToken, ValueTask>? onCall = null
    )
    {
        var mock = new Mock<ChatIndexInitializerShard.UpdateCursorHandler>();
        mock.Setup(handler => handler(
                It.IsAny<Moment>(),
                It.IsAny<ChatIndexInitializerShard.SharedState>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<ICursorStates<ChatIndexInitializerShard.Cursor>>(),
                It.IsAny<ILogger>(),
                It.IsAny<Tracer?>(),
                It.IsAny<CancellationToken>()))
            .Returns(onCall ?? ((_, _, _, _, _, _, _) => ValueTask.CompletedTask));
        return mock;
    }
}
