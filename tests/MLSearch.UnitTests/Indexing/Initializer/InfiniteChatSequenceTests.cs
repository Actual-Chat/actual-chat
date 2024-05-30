using ActualChat.Chat;
using ActualChat.MLSearch.Indexing.Initializer;
using System.Linq.Expressions;

namespace ActualChat.MLSearch.UnitTests.Indexing.Initializer;

public class InfiniteChatSequenceTests
{
    private readonly IMomentClock Clock = Mock.Of<IMomentClock>();
    private readonly IChatsBackend Chats = Mock.Of<IChatsBackend>();
    private readonly ILogger<InfiniteChatSequence> Log = Mock.Of<ILogger<InfiniteChatSequence>>();
    private readonly Expression<Func<IChatsBackend, Task<ApiArray<Chat.Chat>>>> ListChangedCall =
        x => x.ListChanged(
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<ChatId>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>());

    [Fact]
    public async Task ChatsAreBeingLoadedInBatches()
    {
        var chats = new Mock<IChatsBackend>();
        chats
            .Setup(ListChangedCall)
            .Returns<long, long, ChatId, int, CancellationToken>(
                (minVersion, _, _, batchSize, _) => GetNextBatch(minVersion, batchSize)
            );

        const int batchSize = 5;

        var sequence = new InfiniteChatSequence(Clock, chats.Object, Log) {
            BatchSize = batchSize,
        };

        const int maxChatCount = 23;
        var prevVersion = 0L;
        await foreach (var (chatId, version) in sequence.LoadAsync(1, CancellationToken.None).Take(maxChatCount)) {
            Assert.True(version > prevVersion);
            prevVersion = version;
        }

        chats.Verify(
            ListChangedCall,
            Times.Exactly((maxChatCount + batchSize - 1) / batchSize)
        );
    }

    private static Task<ApiArray<Chat.Chat>> GetNextBatch(long lastVersion, int batchSize)
    {
        var batch = new Chat.Chat[batchSize];
        for (var i = 0; i < batchSize; i++) {
            var chatId = new ChatId(Generate.Option);
            batch[i] = new Chat.Chat(chatId, lastVersion + i + 1);
        }
        return Task.FromResult(new ApiArray<Chat.Chat>(batch));
    }

    [Fact]
    public async Task LoadMethodThrowsIfCancellationRequestedBeforeCall()
    {
        var sequence = new InfiniteChatSequence(Clock, Chats, Log);

        var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();
        var e = await Assert.ThrowsAsync<OperationCanceledException>(async () => {
            await foreach (var _ in sequence.LoadAsync(1, cancellationSource.Token).Take(1));
        });
        Assert.True(e.IsCancellationOf(cancellationSource.Token));
    }

    [Fact]
    public async Task LoadMethodRethrowsCancellationOfChatsBackend()
    {
        var cancellationSource = new CancellationTokenSource();

        var chatsBackend = new Mock<IChatsBackend>();
        chatsBackend
            .Setup(ListChangedCall)
            .Throws<long, long, ChatId, int, CancellationToken, TaskCanceledException>(
                (_, _, _, _, ct) => {
                    cancellationSource.Cancel();
                    return new TaskCanceledException("", null, ct);
                });

        var sequence = new InfiniteChatSequence(Clock, chatsBackend.Object, Log);

        var e = await Assert.ThrowsAsync<TaskCanceledException>(async () => {
            await foreach (var _ in sequence.LoadAsync(1, cancellationSource.Token).Take(1));
        });
        Assert.True(e.IsCancellationOf(cancellationSource.Token));
    }

    [Fact]
    public async Task LoadMethodRethrowsCancellationDuringRecoveryDelay()
    {
        var cancellationSource = new CancellationTokenSource();

        var clock = new Mock<IMomentClock>();
        clock
            .Setup(x => x.Delay(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Throws<TimeSpan, CancellationToken, TaskCanceledException>(
                (_, ct) => new TaskCanceledException("", null, ct)
            );

        var chats = new Mock<IChatsBackend>();
        chats
            .Setup(ListChangedCall)
            .Throws(() => {
                cancellationSource.Cancel();
                return new InvalidOperationException("Something is wrong.");
            });

        var sequence = new InfiniteChatSequence(clock.Object, chats.Object, Log);

        var e = await Assert.ThrowsAsync<TaskCanceledException>(async () => {
            await foreach (var _ in sequence.LoadAsync(1, cancellationSource.Token).Take(1));
        });
        Assert.True(e.IsCancellationOf(cancellationSource.Token));
    }

    [Fact]
    public async Task LoadMethodRethrowsCancellationDuringEmptyBatchDelay()
    {
        var emptyBatchDelay = TimeSpan.FromSeconds(11111);
        var cancellationSource = new CancellationTokenSource();

        var clock = new Mock<IMomentClock>();
        clock
            .Setup(x => x.Delay(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Throws<TimeSpan, CancellationToken, TaskCanceledException>(
                (_, ct) => new TaskCanceledException("", null, ct)
            );

        var chats = new Mock<IChatsBackend>();
        chats
            .Setup(ListChangedCall)
            .ReturnsAsync(() => {
                cancellationSource.Cancel();
                return new ApiArray<Chat.Chat>([]);
            });

        var sequence = new InfiniteChatSequence(clock.Object, chats.Object, Log) {
            NoChatsIdleInterval = emptyBatchDelay
        };

        var e = await Assert.ThrowsAsync<TaskCanceledException>(async () => {
            await foreach (var _ in sequence.LoadAsync(1, cancellationSource.Token).Take(1));
        });
        Assert.True(e.IsCancellationOf(cancellationSource.Token));

        clock.Verify(
            x => x.Delay(It.Is<TimeSpan>(ts => ts==emptyBatchDelay), It.IsAny<CancellationToken>()), Times.Once);
        clock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LoadMethodLogsExceptionAndRetriesAfterBatchLoadError()
    {
        var retryDelay = TimeSpan.FromSeconds(777);
        var emptyBatchDelay = TimeSpan.FromSeconds(11111);
        var cancellationSource = new CancellationTokenSource();

        var clock = new Mock<IMomentClock>();
        clock
            .Setup(x => x.Delay(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var batchNum = 0;
        var chats = new Mock<IChatsBackend>();
        chats
            .Setup(ListChangedCall)
            .Returns<long, long, ChatId, int, CancellationToken>(
                (minVersion, _, _, batchSize, _) => {
                    batchNum++;
                    if (batchNum==2) {
                        throw new InvalidOperationException("Something is wrong.");
                    }
                    return GetNextBatch(minVersion, batchSize);
                });

        var log = LogMock.Create<InfiniteChatSequence>();

        const int batchSize = 5;
        var sequence = new InfiniteChatSequence(clock.Object, chats.Object, log.Object) {
            BatchSize = batchSize,
            NoChatsIdleInterval = emptyBatchDelay,
            RetryInterval = retryDelay,
        };

        await foreach (var _ in sequence.LoadAsync(1, cancellationSource.Token).Take(3*batchSize / 2));

        clock.Verify(
            x => x.Delay(It.Is<TimeSpan>(ts => ts==retryDelay), It.IsAny<CancellationToken>()), Times.Once);
        clock.VerifyNoOtherCalls();

        log.Verify(
            LogMock.GetLogMethodExpression<InfiniteChatSequence>(LogLevel.Error),
            Times.Once);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public async Task LoadMethodThrowsIfCanceledDuringBatchEnumeration(int canceledAfter)
    {
        var clock = new Mock<IMomentClock>();
        clock
            .Setup(x => x.Delay(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var chats = new Mock<IChatsBackend>();
        chats
            .Setup(ListChangedCall)
            .Returns<long, long, ChatId, int, CancellationToken>(
                (minVersion, _, _, batchSize, _) => GetNextBatch(minVersion, batchSize)
            );

        const int batchSize = 5;

        var sequence = new InfiniteChatSequence(clock.Object, chats.Object, Log) {
            BatchSize = batchSize,
        };

        var numBatchLoads = (canceledAfter + batchSize - 1) / batchSize;
        var cancellationSource = new CancellationTokenSource();
        var e = await Assert.ThrowsAsync<OperationCanceledException>(async () => {
            var count = 0;
            var chatSeq = sequence.LoadAsync(1, cancellationSource.Token).Take((batchSize * numBatchLoads) + 1);
            await foreach (var _ in chatSeq) {
                if (++count >= canceledAfter) {
                    await cancellationSource.CancelAsync();
                }
            }
        });
        Assert.True(e.IsCancellationOf(cancellationSource.Token));

        chats.Verify(ListChangedCall, Times.Exactly(numBatchLoads));
        clock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LoadMethodReceivesVersionAndIdOfTheLastSeenChat()
    {
        const long version = 1;
        var (lastSeenId, lastSeenVersion) = (ChatId.None, version);
        var chats = new Mock<IChatsBackend>();
        var allChecksPassed = true;
        var batchCount = 0;
        chats
            .Setup(ListChangedCall)
            .Returns<long, long, ChatId, int, CancellationToken>(
                (lastVersion, _, lastId, batchSize, _) => {
                    batchCount += 1;
                    allChecksPassed |= (lastSeenId, lastSeenVersion) == (lastId, lastVersion);
                    return GetNextBatch(lastVersion, batchSize);
                });

        const int batchSize = 5;

        var sequence = new InfiniteChatSequence(Clock, chats.Object, Log) {
            BatchSize = batchSize,
        };
        var chatSequence = sequence.LoadAsync(version, CancellationToken.None).Take((5 * batchSize) + 1);
        await foreach (var (chatId, chatVersion) in chatSequence) {
            (lastSeenId, lastSeenVersion) = (chatId, chatVersion);
        }
        Assert.True(allChecksPassed, "Unexpected parameters of ListChanged method detected.");
        Assert.True(batchCount > 0, "No batches were loaded.");
    }
}
