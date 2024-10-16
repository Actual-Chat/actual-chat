using ActualChat.Chat;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

public class ChatContentIndexerInitTests(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task InitMethodLoadsTailDocuments()
    {
        const int maxTailSetSize = 333;
        var tailDocuments = ChatContentTestHelpers.CreateDocuments();
        var chats = Mock.Of<IChatsBackend>();
        var docLoader = new Mock<IChatContentDocumentLoader>();
        docLoader
            .Setup(x => x.LoadTailAsync(
                It.IsAny<ChatId>(),
                It.IsAny<ChatContentCursor>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tailDocuments);
        var docMapper = Mock.Of<IChatContentMapper>();
        var sink = Mock.Of<ISink<ChatSlice, string>>();
        var contentArranger = Mock.Of<IChatContentArranger>();

        var chatId = new ChatId(Generate.Option);
        var contentIndexer = new ChatContentIndexer(chatId, chats, docLoader.Object, docMapper, contentArranger, sink) {
            MaxTailSetSize = maxTailSetSize,
        };

        var cancellationSource = new CancellationTokenSource();
        var cursor = new ChatContentCursor(0, 0);
        await contentIndexer.InitAsync(cursor, cancellationSource.Token);

        docLoader.Verify(x => x.LoadTailAsync(
            It.Is<ChatId>(id => id == chatId),
            It.Is<ChatContentCursor>(c => c == cursor),
            It.Is<int>(sz => sz == maxTailSetSize),
            It.Is<CancellationToken>(ct => ct == cancellationSource.Token)
        ), Times.Once);

        Assert.Equal(cursor, contentIndexer.Cursor);
        Assert.Equal(tailDocuments.Length, contentIndexer.TailDocs.Count);
        foreach (var doc in tailDocuments) {
            Assert.True(contentIndexer.TailDocs.TryGetValue(doc.Id, out var tailDoc) && ReferenceEquals(doc, tailDoc));
        }
    }

    [Fact]
    public async Task InitMethodDoesNotSwallowExceptions()
    {
        var chats = Mock.Of<IChatsBackend>();
        var docLoader = new Mock<IChatContentDocumentLoader>();
        docLoader
            .Setup(x => x.LoadTailAsync(
                It.IsAny<ChatId>(),
                It.IsAny<ChatContentCursor>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromException<IReadOnlyCollection<ChatSlice>>(new UniqueException()));
        var docMapper = Mock.Of<IChatContentMapper>();
        var sink = Mock.Of<ISink<ChatSlice, string>>();
        var contentArranger = Mock.Of<IChatContentArranger>();

        var contentIndexer = new ChatContentIndexer(ChatId.None, chats, docLoader.Object, docMapper, contentArranger, sink);

        var cursor = new ChatContentCursor(0, 0);
        await Assert.ThrowsAsync<UniqueException>(() => contentIndexer.InitAsync(cursor, CancellationToken.None));
    }
}
