
using ActualChat.Chat;
using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;
using ActualChat.Queues;

namespace ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

public class ChatContentIndexWorkerTests(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ActivationConstructorWorksAsExpected()
    {
        const int flushInterval = 119922;
        const int maxEventCount = 77449988;

        var contentIndexWorker = new ChatContentIndexWorker(
            flushInterval,
            maxEventCount,
            Mock.Of<IChatContentUpdateLoader>(),
            Mock.Of<ICursorStates<ChatContentCursor>>(),
            Mock.Of<IChatInfoIndexer>(),
            Mock.Of<IChatContentIndexerFactory>(),
            Mock.Of<IQueues>()
        );

        Assert.Equal(flushInterval, contentIndexWorker.FlushInterval);
        Assert.Equal(maxEventCount, contentIndexWorker.MaxEventCount);
    }

    [Theory]
    [InlineData(IndexingKind.ChatInfo)]
    [InlineData(IndexingKind.ChatContent)]
    public async Task ExecuteAsyncAlwaysCallsChatInfoIndexer(IndexingKind indexingKind)
    {
        var updateLoader = new Mock<IChatContentUpdateLoader>();
        updateLoader
            .Setup(x => x.LoadChatUpdatesAsync(It.IsAny<ChatId>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<ChatEntry>());

        var cursor = new ChatContentCursor(77, 88);
        var cursorStates = new Mock<ICursorStates<ChatContentCursor>>();
        cursorStates
            .Setup(x => x.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursor);

        var chatInfoIndexer = new Mock<IChatInfoIndexer>();
        chatInfoIndexer
            .Setup(i => i.IndexAsync(It.IsAny<ChatId>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var contentIndexerFactory = new Mock<IChatContentIndexerFactory>();
        contentIndexerFactory
            .Setup(f => f.Create(It.IsAny<ChatId>()))
            .Returns(Task.FromResult(Mock.Of<IChatContentIndexer>()));

        var queues = QueuesMock.Create();

        var contentIndexWorker = new ChatContentIndexWorker(
            updateLoader.Object,
            cursorStates.Object,
            chatInfoIndexer.Object,
            contentIndexerFactory.Object,
            queues.Object
        );

        var chatId = new ChatId(Generate.Option);
        var cancellationSource = new CancellationTokenSource();

        var job = new MLSearch_TriggerChatIndexing(chatId, indexingKind);
        await contentIndexWorker.ExecuteAsync(job, cancellationSource.Token);

        chatInfoIndexer.Verify(x => x.IndexAsync(
            chatId,
            cancellationSource.Token
        ), Times.Once);
        chatInfoIndexer.VerifyNoOtherCalls();

        if (indexingKind==IndexingKind.ChatInfo) {
            updateLoader.VerifyNoOtherCalls();
            cursorStates.VerifyNoOtherCalls();
            contentIndexerFactory.VerifyNoOtherCalls();
            queues.VerifyNoOtherCalls();
        }
        else {
            updateLoader.Verify(x => x.LoadChatUpdatesAsync(
                    chatId,
                    cursor.LastEntryVersion,
                    cursor.LastEntryLocalId,
                    It.Is<CancellationToken>(ct => ct == cancellationSource.Token)
                ),
                Times.Once);
        }
    }

    [Fact]
    public async Task ExecuteAsyncCreatesAndInitializesChatContentIndexer()
    {
        var updateLoader = new Mock<IChatContentUpdateLoader>();
        updateLoader
            .Setup(x => x.LoadChatUpdatesAsync(It.IsAny<ChatId>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<ChatEntry>());

        var cursor = new ChatContentCursor(77, 88);
        var cursorStates = new Mock<ICursorStates<ChatContentCursor>>();
        cursorStates
            .Setup(x => x.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursor);

        var chatInfoIndexer = new Mock<IChatInfoIndexer>();

        var contentIndexer = new Mock<IChatContentIndexer>();
        contentIndexer
            .Setup(x => x.InitAsync(It.IsAny<ChatContentCursor>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var contentIndexerFactory = new Mock<IChatContentIndexerFactory>();
        contentIndexerFactory
            .Setup(f => f.Create(It.IsAny<ChatId>()))
            .Returns(Task.FromResult(contentIndexer.Object));

        var queues = QueuesMock.Create();

        var contentIndexWorker = new ChatContentIndexWorker(
            updateLoader.Object,
            cursorStates.Object,
            chatInfoIndexer.Object,
            contentIndexerFactory.Object,
            queues.Object
        );

        var chatId = new ChatId(Generate.Option);
        var cancellationSource = new CancellationTokenSource();

        var job = new MLSearch_TriggerChatIndexing(chatId, IndexingKind.ChatContent);
        await contentIndexWorker.ExecuteAsync(job, cancellationSource.Token);

        contentIndexerFactory.Verify(x => x.Create(
            It.Is<ChatId>(id => id == chatId)
        ), Times.Once);
        contentIndexer.Verify(x => x.InitAsync(
            It.Is<ChatContentCursor>(c => c == cursor),
            It.Is<CancellationToken>(ct => ct == cancellationSource.Token)
        ), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsyncSendsAllUpdatedEntriesToContentIndexer()
    {
        const int updateCount = 101;
        var chatId = new ChatId(Generate.Option);
        var updates = GenerateUpdates(chatId, updateCount);
        var updateLoader = new Mock<IChatContentUpdateLoader>();
        updateLoader
            .Setup(x => x.LoadChatUpdatesAsync(It.IsAny<ChatId>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(updates.ToAsyncEnumerable());

        var cursorStates = new Mock<ICursorStates<ChatContentCursor>>();
        var chatInfoIndexer = new Mock<IChatInfoIndexer>();

        var appliedEntries = new List<ChatEntry>();
        var contentIndexer = new Mock<IChatContentIndexer>();
        contentIndexer
            .Setup(x => x.ApplyAsync(It.IsAny<ChatEntry>(), It.IsAny<CancellationToken>()))
            .Returns<ChatEntry, CancellationToken>((entry, _) => {
                appliedEntries.Add(entry);
                return ValueTask.CompletedTask;
            });

        var contentIndexerFactory = new Mock<IChatContentIndexerFactory>();
        contentIndexerFactory
            .Setup(f => f.Create(It.IsAny<ChatId>()))
            .Returns(Task.FromResult(contentIndexer.Object));

        var queues = QueuesMock.Create();

        var contentIndexWorker = new ChatContentIndexWorker(
            updateLoader.Object,
            cursorStates.Object,
            chatInfoIndexer.Object,
            contentIndexerFactory.Object,
            queues.Object
        ) {
            MaxEventCount = updateCount + 1,
            FlushInterval = updateCount + 1
        };

        var cancellationSource = new CancellationTokenSource();

        var job = new MLSearch_TriggerChatIndexing(chatId, IndexingKind.ChatContent);
        await contentIndexWorker.ExecuteAsync(job, cancellationSource.Token);

        contentIndexer.Verify(x => x.ApplyAsync(
            It.IsAny<ChatEntry>(),
            It.Is<CancellationToken>(ct => ct == cancellationSource.Token)
        ), Times.Exactly(updateCount));

        Assert.Equal(updates.AsEnumerable(), appliedEntries);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(99)]
    [InlineData(999)]
    public async Task ExecuteAsyncFlushesChangesPeriodicallyAndInTheEnd(int updateCount)
    {
        var chatId = new ChatId(Generate.Option);
        var updates = GenerateUpdates(chatId, updateCount);
        var updateLoader = new Mock<IChatContentUpdateLoader>();
        updateLoader
            .Setup(x => x.LoadChatUpdatesAsync(It.IsAny<ChatId>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(updates.ToAsyncEnumerable());

        var cursorStates = new Mock<ICursorStates<ChatContentCursor>>();
        cursorStates
            .Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<ChatContentCursor>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var chatInfoIndexer = new Mock<IChatInfoIndexer>();

        var cursor = new ChatContentCursor(839470, 98237);
        var contentIndexer = new Mock<IChatContentIndexer>();
        contentIndexer
            .Setup(x => x.FlushAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(cursor));

        var contentIndexerFactory = new Mock<IChatContentIndexerFactory>();
        contentIndexerFactory
            .Setup(f => f.Create(It.IsAny<ChatId>()))
            .Returns(Task.FromResult(contentIndexer.Object));

        var queues = QueuesMock.Create();

        const int flushInterval = 33;
        var contentIndexWorker = new ChatContentIndexWorker(
            updateLoader.Object,
            cursorStates.Object,
            chatInfoIndexer.Object,
            contentIndexerFactory.Object,
            queues.Object
        ) {
            MaxEventCount = int.MaxValue,
            FlushInterval = flushInterval
        };

        var cancellationSource = new CancellationTokenSource();

        var job = new MLSearch_TriggerChatIndexing(chatId, IndexingKind.ChatContent);
        await contentIndexWorker.ExecuteAsync(job, cancellationSource.Token);

        var flushCount = (updateCount / flushInterval) + 1;

        contentIndexer.Verify(x => x.FlushAsync(
            It.Is<CancellationToken>(ct => ct == cancellationSource.Token)
        ), Times.Exactly(flushCount));

        cursorStates.Verify(x => x.SaveAsync(
            It.Is<string>(key => key == chatId),
            It.Is<ChatContentCursor>(c => c == cursor),
            It.Is<CancellationToken>(ct => ct == cancellationSource.Token)
        ), Times.Exactly(flushCount));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(101)]
    [InlineData(1001)]
    public async Task ExecuteAsyncDoNotProcessMoreThanMaxAmountOfChanges(int updateCount)
    {
        const int maxUpdateCount = 101;
        var chatId = new ChatId(Generate.Option);
        var updates = GenerateUpdates(chatId, updateCount);
        var updateLoader = new Mock<IChatContentUpdateLoader>();
        updateLoader
            .Setup(x => x.LoadChatUpdatesAsync(It.IsAny<ChatId>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(updates.ToAsyncEnumerable());

        var cursorStates = new Mock<ICursorStates<ChatContentCursor>>();
        var chatInfoIndexer = new Mock<IChatInfoIndexer>();

        var contentIndexer = new Mock<IChatContentIndexer>();
        contentIndexer
            .Setup(x => x.ApplyAsync(It.IsAny<ChatEntry>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var contentIndexerFactory = new Mock<IChatContentIndexerFactory>();
        contentIndexerFactory
            .Setup(f => f.Create(It.IsAny<ChatId>()))
            .Returns(Task.FromResult(contentIndexer.Object));

        QueuedCommand? command = null;
        var queues = QueuesMock.Create((cmd, _) => {
            command = cmd;
            return Task.CompletedTask;
        });

        var contentIndexWorker = new ChatContentIndexWorker(
            updateLoader.Object,
            cursorStates.Object,
            chatInfoIndexer.Object,
            contentIndexerFactory.Object,
            queues.Object
        ) {
            MaxEventCount = maxUpdateCount,
        };

        var cancellationSource = new CancellationTokenSource();

        var job = new MLSearch_TriggerChatIndexing(chatId, IndexingKind.ChatContent);
        await contentIndexWorker.ExecuteAsync(job, cancellationSource.Token);

        contentIndexer.Verify(x => x.ApplyAsync(
            It.IsAny<ChatEntry>(),
            It.Is<CancellationToken>(ct => ct == cancellationSource.Token)
        ), Times.Exactly(Math.Min(updateCount, maxUpdateCount)));

        Assert.NotNull(command);
        if (updateCount < maxUpdateCount) {
            Assert.IsType<MLSearch_TriggerChatIndexingCompletion>(command.UntypedCommand);
            Assert.Equal(chatId, ((MLSearch_TriggerChatIndexingCompletion)command.UntypedCommand).Id);
        }
        else {
            Assert.Equal(job, command.UntypedCommand);
        }
    }

    private IReadOnlyCollection<ChatEntry> GenerateUpdates(ChatId chatId, int updateCount)
        => [.. Enumerable.Range(1, updateCount).Select(id => {
            var (entryId, content) = (new ChatEntryId(chatId, ChatEntryKind.Text, id, AssumeValid.Option), $"Content #{id}");
            return new ChatEntry(entryId, id +  100) {
                Content = content,
            };
        })];
}
