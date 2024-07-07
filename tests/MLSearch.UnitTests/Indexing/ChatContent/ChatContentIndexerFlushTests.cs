using ActualChat.Chat;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

public class ChatContentIndexerFlushTests(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly string[] NewContent = [
        "I made myself a snowball",
        "As perfect as could be.",
        "I thought I'd keep it as a pet",
        "And let it sleep with me.",

        "I made it some pajamas",
        "And a pillow for its head.",
        "Then last night it ran away,",
        "But first it wet the bed."
    ];

    [Fact]
    public async Task FlushMethodCallUpdatesBuffersAndCursorAndTailDocs()
    {
        var tailDocuments = ChatContentTestHelpers.CreateDocuments();
        var updatedDoc = tailDocuments[tailDocuments.Length / 2];
        var removedDoc = tailDocuments[(tailDocuments.Length / 2)  + 1];
        var updatedEntryId = updatedDoc.Metadata.ChatEntries.Single().Id;
        var removedEntryId = removedDoc.Metadata.ChatEntries.Single().Id;
        var chatId = updatedEntryId.ChatId;
        var lastId = tailDocuments
            .SelectMany(doc => doc.Metadata.ChatEntries)
            .Max(entry => entry.Id.LocalId);

        // There must be no calls to GetTile, as updatedDoc contains single entry,
        // so we don't setup GetTile call in the mock.
        var chats = Mock.Of<IChatsBackend>();

        var docLoader = new Mock<IChatContentDocumentLoader>();
        // Emulate loading tail documents
        docLoader
            .Setup(x => x.LoadTailAsync(
                It.IsAny<ChatId>(),
                It.IsAny<ChatContentCursor>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tailDocuments);
        // On update, we get the document from index by entry id
        docLoader
            .Setup(x => x.LoadByEntryIdsAsync(It.IsAny<IEnumerable<ChatEntryId>>(), It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<ChatEntryId>, CancellationToken>(
                (ids, _) => {
                    var idSet = ids.ToHashSet();
                    var docs = tailDocuments
                        .Where(doc => doc.Metadata.ChatEntries.Any(e => idSet.Contains(e.Id)));
                    return Task.FromResult<IReadOnlyCollection<ChatSlice>>([..docs]);
                });

        var newDocIds = new List<string>();
        var docMapper = ChatContentTestHelpers.MockDocMapper(doc => {
            if (doc.Id == updatedDoc.Id) {
                updatedDoc = doc;
            }
            else {
                newDocIds.Add(doc.Id);
            }
        });

        var contentArranger = new Mock<IChatContentArranger>();
        contentArranger
            .Setup(x => x.ArrangeAsync(
                It.IsAny<IReadOnlyCollection<ChatEntry>>(),
                It.IsAny<IReadOnlyCollection<ChatSlice>>(),
                It.IsAny<CancellationToken>()))
            .Returns<IReadOnlyCollection<ChatEntry>, IReadOnlyCollection<ChatSlice>, CancellationToken>(
                (entries, _, _) => {
                    var sources = entries.Chunk(2)
                        .Select(chunk => new SourceEntries(null, null, chunk)).ToList();
                    return sources.AsAsyncEnumerable();
                }
            );

        IReadOnlyCollection<ChatSlice>? actualUpdates = null;
        IReadOnlyCollection<string>? actualRemoves = null;
        var sink = new Mock<ISink<ChatSlice, string>>();
        sink.Setup(x => x.ExecuteAsync(
                It.IsAny<IReadOnlyCollection<ChatSlice>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns<IReadOnlyCollection<ChatSlice>, IReadOnlyCollection<string>, CancellationToken>(
                (updates, removes, _) => {
                    actualUpdates = [.. updates];
                    actualRemoves = [.. removes];
                    return Task.CompletedTask;
                }
            );

        var maxTailSetSize = NewContent.Length / 2;
        var contentIndexer = new ChatContentIndexer(chatId, chats, docLoader.Object, docMapper.Object, contentArranger.Object, sink.Object) {
            MaxTailSetSize = maxTailSetSize,
        };

        var cursor = new ChatContentCursor(0, 0);
        await contentIndexer.InitAsync(cursor, CancellationToken.None);

        Assert.Equal(tailDocuments.Length, contentIndexer.TailDocs.Count);

        // Generate some new content
        var version = 100;
        var delayedCursors = new List<ChatContentCursor>();
        for (var i = 0; i < NewContent.Length; i++) {
            var localId = lastId + i + 1;
            var entryId = new ChatEntryId(chatId, ChatEntryKind.Text, localId, AssumeValid.Option);
            var entry = new ChatEntry(entryId, ++version) {
                Content = NewContent[i],
            };
            await contentIndexer.ApplyAsync(entry, CancellationToken.None);
            delayedCursors.Add(new ChatContentCursor(entry));
        }
        // New content doesn't affect next cursor until Flush
        Assert.Equal(cursor, contentIndexer.NextCursor);

        // Update one of the documents
        var updatedEntry = new ChatEntry(updatedEntryId, ++version) {
            Content = "Some fresh update.",
        };
        await contentIndexer.ApplyAsync(updatedEntry, CancellationToken.None);

        Assert.Same(updatedDoc, contentIndexer.TailDocs[updatedDoc.Id]);
        Assert.Same(updatedDoc, contentIndexer.OutUpdates[updatedDoc.Id]);

        var expectedNextCursor = new ChatContentCursor(updatedEntry);
        Assert.Equal(expectedNextCursor, contentIndexer.NextCursor);

        // Remove other doc
        var removedEntry = new ChatEntry(removedEntryId, ++version) {
            IsRemoved = true,
        };
        await contentIndexer.ApplyAsync(removedEntry, CancellationToken.None);

        expectedNextCursor = new ChatContentCursor(removedEntry);
        Assert.Equal(expectedNextCursor, contentIndexer.NextCursor);

        // Check our buffers are in expected state
        Assert.Equal(NewContent.Length, contentIndexer.Buffer.Count);
        Assert.Single(contentIndexer.OutUpdates);
        Assert.True(contentIndexer.OutUpdates.ContainsKey(updatedDoc.Id));
        Assert.Single(contentIndexer.OutRemoves);
        Assert.Contains(removedDoc.Id, contentIndexer.OutRemoves);

        var contentCursor = await contentIndexer.FlushAsync(CancellationToken.None);

        // Buffers must be empty after flush
        Assert.Empty(contentIndexer.Buffer);
        Assert.Empty(contentIndexer.OutRemoves);
        Assert.Empty(contentIndexer.OutUpdates);

        docLoader.Verify(x => x.LoadTailAsync(
            It.Is<ChatId>(id => id == chatId),
            It.IsAny<ChatContentCursor>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
        docLoader.Verify(x => x.LoadByEntryIdsAsync(
            It.IsAny<IEnumerable<ChatEntryId>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2 + NewContent.Length));

        docMapper.Verify(x => x.MapAsync(
            It.IsAny<SourceEntries>(),
            It.IsAny<CancellationToken>()), Times.Exactly(1 + (NewContent.Length / 2)));

        contentArranger
            .Verify(x => x.ArrangeAsync(
                It.IsAny<IReadOnlyCollection<ChatEntry>>(),
                It.IsAny<IReadOnlyCollection<ChatSlice>>(),
                It.IsAny<CancellationToken>()), Times.Once);

        sink.Verify(x => x.ExecuteAsync(
                It.IsAny<IReadOnlyCollection<ChatSlice>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(
            newDocIds.Append(updatedDoc.Id).OrderBy(x => x),
            actualUpdates.Select(doc => doc.Id).OrderBy(x => x));

        Assert.Equal(removedDoc.Id, actualRemoves.Single());

        Assert.Equal(delayedCursors.Append(expectedNextCursor).Max(), contentIndexer.NextCursor);
        Assert.Equal(contentIndexer.Cursor, contentIndexer.NextCursor);
        Assert.Equal(contentCursor, contentIndexer.NextCursor);

        var newTailDocIds = actualUpdates
            .OrderByDescending(x => x.Version)
            .Take(maxTailSetSize)
            .Select(x => x.Id);
        Assert.True(newTailDocIds.All(contentIndexer.TailDocs.ContainsKey));
        Assert.Equal(maxTailSetSize, contentIndexer.TailDocs.Count);
    }
}
