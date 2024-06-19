using ActualChat.Chat;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

public class ChatContentIndexerTests(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task InitMethodLoadsTailDocuments()
    {
        const int maxTailSetSize = 333;
        var tailDocuments = ContentHelpers.CreateDocuments();
        var chats = Mock.Of<IChatsBackend>();
        var docLoader = new Mock<IChatContentDocumentLoader>();
        docLoader
            .Setup(x => x.LoadTailAsync(
                It.IsAny<ChatContentCursor>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tailDocuments);
        var docMapper = Mock.Of<IChatContentMapper>();
        var sink = Mock.Of<ISink<ChatSlice, string>>();

        var contentIndexer = new ChatContentIndexer(chats, docLoader.Object, docMapper, sink) {
            MaxTailSetSize = maxTailSetSize,
        };

        var cancellationSource = new CancellationTokenSource();
        var cursor = new ChatContentCursor(0, 0);
        await contentIndexer.InitAsync(cursor, cancellationSource.Token);

        docLoader.Verify(x => x.LoadTailAsync(
            It.Is<ChatContentCursor>(x => x == cursor),
            It.Is<int>(x => x == maxTailSetSize),
            It.Is<CancellationToken>(x => x == cancellationSource.Token)
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
                It.IsAny<ChatContentCursor>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromException<IReadOnlyCollection<ChatSlice>>(new UniqueException()));
        var docMapper = Mock.Of<IChatContentMapper>();
        var sink = Mock.Of<ISink<ChatSlice, string>>();

        var contentIndexer = new ChatContentIndexer(chats, docLoader.Object, docMapper, sink);

        var cursor = new ChatContentCursor(0, 0);
        await Assert.ThrowsAsync<UniqueException>(() => contentIndexer.InitAsync(cursor, CancellationToken.None));
    }

    public class EntryToDocMap(int numDocs, int? start = default, int? end = default) : IXunitSerializable
    {
        public int NumDocs => numDocs;
        public int? Start => start;
        public int? End => end;

        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public EntryToDocMap() : this(default, default, default)
        { }

        public void Deserialize(IXunitSerializationInfo info)
        {
            numDocs = info.GetValue<int>(nameof(numDocs));
            start = info.GetValue<int?>(nameof(start));
            end = info.GetValue<int?>(nameof(end));
        }

        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(numDocs), numDocs);
            info.AddValue(nameof(start), start);
            info.AddValue(nameof(end), end);
        }
    }

    public static TheoryData<EntryToDocMap> EntryMappings
    {
        get {
            int?[] starts = [null, 0, 100];
            int?[] ends =  [200, null];
            var theoryData = new TheoryData<EntryToDocMap>();
            for (var docNumber = 1; docNumber < 4; docNumber++) {
                foreach (var start in starts) {
                    foreach (var end in ends) {
                        theoryData.Add(new EntryToDocMap(docNumber, start, end));
                    }
                }
            }
            return theoryData;
        }
    }

    [Theory]
    [MemberData(nameof(EntryMappings))]
    public async Task ApplyingRemoveEventProperlyUpdatesUnderlyingIndex(EntryToDocMap entryToDocMap)
    {

    }
}
