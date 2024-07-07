using System.Text;
using ActualChat.Chat;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

public class ChatContentIndexerApplyTests(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task ApplyingCreateEventPutsEventIntoInternalBuffer()
    {
        var chats = Mock.Of<IChatsBackend>();
        // Doc loader returns empty list, so event considered as new chat entry
        var docLoader = new Mock<IChatContentDocumentLoader>();
        docLoader
            .Setup(x => x.LoadByEntryIdsAsync(It.IsAny<IEnumerable<ChatEntryId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var docMapper = Mock.Of<IChatContentMapper>();
        var sink = Mock.Of<ISink<ChatSlice, string>>();
        var contentArranger = Mock.Of<IChatContentArranger>();

        var chatId = new ChatId(Generate.Option);
        var contentIndexer = new ChatContentIndexer(chatId, chats, docLoader.Object, docMapper, contentArranger, sink);

        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 2, AssumeValid.Option);
        var chatEntry = new ChatEntry(chatEntryId) {
            Content = "Some fresh message.",
        };
        await contentIndexer.ApplyAsync(chatEntry, CancellationToken.None);

        Assert.True(contentIndexer.Buffer.Count > 0);
        Assert.Equal(chatEntryId, contentIndexer.Buffer.Last().Id);
    }

    [Fact]
    public async Task ApplyingUpdateEventRightAfterCreateEventUpdatesEntryInBuffer()
    {
        var chats = Mock.Of<IChatsBackend>();
        // Doc loader returns empty list, so event considered as new chat entry
        var docLoader = new Mock<IChatContentDocumentLoader>();
        docLoader
            .Setup(x => x.LoadByEntryIdsAsync(It.IsAny<IEnumerable<ChatEntryId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var docMapper = Mock.Of<IChatContentMapper>();
        var sink = Mock.Of<ISink<ChatSlice, string>>();
        var contentArranger = Mock.Of<IChatContentArranger>();

        var chatId = new ChatId(Generate.Option);
        var contentIndexer = new ChatContentIndexer(chatId, chats, docLoader.Object, docMapper, contentArranger, sink);

        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 2, AssumeValid.Option);
        var chatEntry = new ChatEntry(chatEntryId) {
            Content = "Some fresh message.",
        };
        await contentIndexer.ApplyAsync(chatEntry, CancellationToken.None);
        var bufferLength = contentIndexer.Buffer.Count;
        var modifiedEntry = new ChatEntry(chatEntryId, 1) {
            Content = "Modified message."
        };
        await contentIndexer.ApplyAsync(modifiedEntry, CancellationToken.None);

        Assert.Equal(bufferLength, contentIndexer.Buffer.Count);
        Assert.Equal(modifiedEntry, contentIndexer.Buffer.First(entry => entry.Id==chatEntryId));
    }

    [Fact]
    public async Task ApplyingRemoveEventRightAfterCreateEventRemovesEntryFromBuffer()
    {
        var chats = Mock.Of<IChatsBackend>();
        // Doc loader returns empty list, so event considered as new chat entry
        var docLoader = new Mock<IChatContentDocumentLoader>();
        docLoader
            .Setup(x => x.LoadByEntryIdsAsync(It.IsAny<IEnumerable<ChatEntryId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var docMapper = Mock.Of<IChatContentMapper>();
        var sink = Mock.Of<ISink<ChatSlice, string>>();
        var contentArranger = Mock.Of<IChatContentArranger>();

        var chatId = new ChatId(Generate.Option);
        var contentIndexer = new ChatContentIndexer(chatId, chats, docLoader.Object, docMapper, contentArranger, sink);

        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 2, AssumeValid.Option);
        var chatEntry = new ChatEntry(chatEntryId) {
            Content = "Some fresh message.",
        };
        await contentIndexer.ApplyAsync(chatEntry, CancellationToken.None);
        var removedEntry = new ChatEntry(chatEntryId, 1) {
            IsRemoved = true
        };
        await contentIndexer.ApplyAsync(removedEntry, CancellationToken.None);

        Assert.Empty(contentIndexer.Buffer);
    }

    [Fact]
    public async Task ApplyingOrphanedRemoveEventDoesNotChangeBuffer()
    {
        var chats = Mock.Of<IChatsBackend>();
        // Doc loader returns empty list, so event doesn't have index footprint yet
        var docLoader = new Mock<IChatContentDocumentLoader>();
        docLoader
            .Setup(x => x.LoadByEntryIdsAsync(It.IsAny<IEnumerable<ChatEntryId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var docMapper = Mock.Of<IChatContentMapper>();
        var sink = Mock.Of<ISink<ChatSlice, string>>();
        var contentArranger = Mock.Of<IChatContentArranger>();

        var chatId = new ChatId(Generate.Option);
        var contentIndexer = new ChatContentIndexer(chatId, chats, docLoader.Object, docMapper, contentArranger, sink);

        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 2, AssumeValid.Option);
        var removedEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 202, AssumeValid.Option);
        var chatEntry = new ChatEntry(chatEntryId) {
            Content = "Some fresh message.",
        };
        await contentIndexer.ApplyAsync(chatEntry, CancellationToken.None);
        var removedEntry = new ChatEntry(removedEntryId, 1) {
            IsRemoved = true
        };
        var buffer = contentIndexer.Buffer.ToArray();
        await contentIndexer.ApplyAsync(removedEntry, CancellationToken.None);

        Assert.Equal(buffer, contentIndexer.Buffer);
    }

    [Fact]
    public async Task UpdatingDocumentUpdatesTailAndOutputBuffer()
    {
        var tailDocuments = ChatContentTestHelpers.CreateDocuments();
        var updatedDoc = tailDocuments[tailDocuments.Length / 2];
        var updatedEntryId = updatedDoc.Metadata.ChatEntries.Single().Id;
        var chats = new Mock<IChatsBackend>();
        // There must be no calls to GetTile, as updatedDoc contains single entry,
        // so there is no need of loading anything
        chats.Setup(x => x.GetTile(
            It.IsAny<ChatId>(),
            It.IsAny<ChatEntryKind>(),
            It.IsAny<ActualLab.Mathematics.Range<long>>(),
            It.Is<bool>(include => include == true),
            It.IsAny<CancellationToken>()));

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
            .ReturnsAsync([updatedDoc]);

        var docMapper = ChatContentTestHelpers.MockDocMapper(doc => updatedDoc = doc);

        var sink = Mock.Of<ISink<ChatSlice, string>>();
        var contentArranger = Mock.Of<IChatContentArranger>();

        var contentIndexer = new ChatContentIndexer(updatedEntryId.ChatId, chats.Object, docLoader.Object, docMapper.Object, contentArranger, sink);

        var cursor = new ChatContentCursor(0, 0);
        await contentIndexer.InitAsync(cursor, CancellationToken.None);

        Assert.Equal(tailDocuments.Length, contentIndexer.TailDocs.Count);

        var updatedContents = new[] {
            "I don't know why I'm doing this.",
            "I hope it won't break."
        };
        var version = 1;
        foreach (var content in updatedContents) {
            var updatedEntry = new ChatEntry(updatedEntryId, ++version) {
                Content = content,
            };
            await contentIndexer.ApplyAsync(updatedEntry, CancellationToken.None);

            Assert.Same(updatedDoc, contentIndexer.TailDocs[updatedDoc!.Id]);
            Assert.Same(updatedDoc, contentIndexer.OutUpdates[updatedDoc!.Id]);

            var expectedNextCursor = new ChatContentCursor(updatedEntry);
            Assert.Equal(expectedNextCursor, contentIndexer.NextCursor);
        }

        chats.Verify(x => x.GetTile(
            It.IsAny<ChatId>(),
            It.IsAny<ChatEntryKind>(),
            It.IsAny<ActualLab.Mathematics.Range<long>>(),
            It.Is<bool>(include => include == true),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemovingDocumentCleansTailAndOutputBuffer()
    {
        var tailDocuments = ChatContentTestHelpers.CreateDocuments();
        var updatedDoc = tailDocuments[tailDocuments.Length / 2];
        var updatedEntryId = updatedDoc.Metadata.ChatEntries.Single().Id;
        var chats = new Mock<IChatsBackend>();
        // There must be no calls to GetTile, as updatedDoc contains single entry,
        // so there is no need of loading anything
        chats.Setup(x => x.GetTile(
            It.IsAny<ChatId>(),
            It.IsAny<ChatEntryKind>(),
            It.IsAny<ActualLab.Mathematics.Range<long>>(),
            It.Is<bool>(include => include == true),
            It.IsAny<CancellationToken>()));

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
            .ReturnsAsync([updatedDoc]);

        var docMapper = ChatContentTestHelpers.MockDocMapper(doc => updatedDoc = doc);

        var sink = Mock.Of<ISink<ChatSlice, string>>();
        var contentArranger = Mock.Of<IChatContentArranger>();

        var contentIndexer = new ChatContentIndexer(updatedEntryId.ChatId, chats.Object, docLoader.Object, docMapper.Object, contentArranger, sink);

        var cursor = new ChatContentCursor(0, 0);
        await contentIndexer.InitAsync(cursor, CancellationToken.None);

        Assert.Equal(tailDocuments.Length, contentIndexer.TailDocs.Count);

        HashSet<string> updatedDocIds = [updatedDoc.Id];

        var version = 1;
        var updatedEntry = new ChatEntry(updatedEntryId, ++version) {
            Content = "Some fresh update.",
        };
        await contentIndexer.ApplyAsync(updatedEntry, CancellationToken.None);

        updatedDocIds.Add(updatedDoc.Id);

        Assert.Same(updatedDoc, contentIndexer.TailDocs[updatedDoc.Id]);
        Assert.Same(updatedDoc, contentIndexer.OutUpdates[updatedDoc.Id]);

        var expectedNextCursor = new ChatContentCursor(updatedEntry);
        Assert.Equal(expectedNextCursor, contentIndexer.NextCursor);

        var removedEntry = new ChatEntry(updatedEntryId, ++version) {
            IsRemoved = true,
        };
        await contentIndexer.ApplyAsync(removedEntry, CancellationToken.None);

        expectedNextCursor = new ChatContentCursor(removedEntry);
        Assert.Equal(expectedNextCursor, contentIndexer.NextCursor);

        Assert.DoesNotContain(contentIndexer.TailDocs.Keys, updatedDocIds.Contains);
        Assert.DoesNotContain(contentIndexer.OutUpdates.Keys, updatedDocIds.Contains);
        Assert.Contains(updatedDoc.Id, contentIndexer.OutRemoves);

        chats.Verify(x => x.GetTile(
            It.IsAny<ChatId>(),
            It.IsAny<ChatEntryKind>(),
            It.IsAny<ActualLab.Mathematics.Range<long>>(),
            It.Is<bool>(include => include == true),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    public class EntryToDocMap(int numDocs, bool isFirst, bool isLast) : IXunitSerializable
    {
        public int NumDocs => numDocs;
        public bool IsFirst => isFirst;
        public bool IsLast => isLast;

        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public EntryToDocMap() : this(default, default, default)
        { }

        public void Deserialize(IXunitSerializationInfo info)
        {
            numDocs = info.GetValue<int>(nameof(numDocs));
            isFirst = info.GetValue<bool>(nameof(isFirst));
            isLast = info.GetValue<bool>(nameof(isLast));
        }

        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(numDocs), numDocs);
            info.AddValue(nameof(isFirst), isFirst);
            info.AddValue(nameof(isLast), isLast);
        }
    }

    public static TheoryData<EntryToDocMap> EntryMappings
    {
        get {
            bool[] booleans = [false, true];
            var theoryData = new TheoryData<EntryToDocMap>();
            for (var docNumber = 1; docNumber < 4; docNumber++) {
                foreach (var isFirst in booleans) {
                    foreach (var isLast in booleans) {
                        theoryData.Add(new EntryToDocMap(docNumber, isFirst, isLast));
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
        var chatId = new ChatId(Generate.Option);
        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 2, AssumeValid.Option);
        var chatEntry = new ChatEntry(chatEntryId) {
            IsRemoved = true,
        };
        var existingDocs = GenerateExistingDocsForEntry(chatEntryId, entryToDocMap);
        var firstDoc = existingDocs.First();
        var lastDoc = existingDocs.Last();
        var firstEntryId = firstDoc.Metadata.ChatEntries.First().Id;
        var lastEntryId = lastDoc.Metadata.ChatEntries.Last().Id;

        var allEntries = new List<ChatEntry>();
        if (firstEntryId != chatEntryId) {
            allEntries.Add(new ChatEntry(firstEntryId) { Content = FirstEntryContent, IsRemoved = false, });
        }
        allEntries.Add(new ChatEntry(chatEntryId) { Content = EntryContent, IsRemoved = true, });
        if (lastEntryId != chatEntryId) {
            allEntries.Add(new ChatEntry(lastEntryId) { Content = LastEntryContent, IsRemoved = false, });
        }

        // Behavior must not depend on document order
        existingDocs.Shuffle();
        // Behavior must not depend on entries order
        allEntries.Shuffle();

        var chats = new Mock<IChatsBackend>();
        _ = chats
            .Setup(x => x.GetTile(
                It.IsAny<ChatId>(),
                It.IsAny<ChatEntryKind>(),
                It.IsAny<ActualLab.Mathematics.Range<long>>(),
                It.Is<bool>(include => include == true),
                It.IsAny<CancellationToken>()))
            .Returns<ChatId, ChatEntryKind, ActualLab.Mathematics.Range<long>, bool, CancellationToken>(
                (_, _, range, includeRemoved, _) => {
                    var tile = new ChatTile(range, includeRemoved, allEntries.ToApiArray());
                    return Task.FromResult(tile);
                });
        var docLoader = new Mock<IChatContentDocumentLoader>();
        docLoader
            .Setup(x => x.LoadByEntryIdsAsync(
                It.IsAny<IEnumerable<ChatEntryId>>(),
                It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<ChatEntryId>, CancellationToken>(
                (entryIds, _) => {
                    var idSet = new HashSet<ChatEntryId>(entryIds);
                    var result = existingDocs.Where(x => x.Metadata.ChatEntries.Any(e => idSet.Contains(e.Id))).ToList();
                    return Task.FromResult<IReadOnlyCollection<ChatSlice>>(result);
                });

        ChatSlice? updatedDoc = null;
        var docMapper = ChatContentTestHelpers.MockDocMapper(doc => updatedDoc = doc);

        var sink = Mock.Of<ISink<ChatSlice, string>>();
        var contentArranger = Mock.Of<IChatContentArranger>();

        var contentIndexer = new ChatContentIndexer(chatId, chats.Object, docLoader.Object, docMapper.Object, contentArranger, sink);
        await contentIndexer.ApplyAsync(chatEntry, CancellationToken.None);

        var docsToRemove = existingDocs
            .Where(x => x.Metadata.ChatEntries.First().Id == chatEntryId)
            .ToArray();
        var docsToUpdate = !entryToDocMap.IsFirst || !entryToDocMap.IsLast ? [updatedDoc!] : Array.Empty<ChatSlice>();

        Assert.Equal(docsToRemove.Select(x => x.Id).OrderBy(x => x), contentIndexer.OutRemoves.OrderBy(x => x));
        Assert.Equal(docsToUpdate.Select(x => x.Id).OrderBy(x => x), contentIndexer.OutUpdates.Keys.OrderBy(x => x));

        if (updatedDoc is null) {
            return;
        }
        if (entryToDocMap.IsFirst) {
            Assert.Null(updatedDoc.Metadata.StartOffset);
        }
        else {
            Assert.Equal(UniqueStartOffset, updatedDoc.Metadata.StartOffset!.Value);
        }
        if (entryToDocMap.IsLast) {
            Assert.Null(updatedDoc.Metadata.EndOffset);
        }
        else {
            Assert.Equal(UniqueEndOffset, updatedDoc.Metadata.EndOffset!.Value);
        }
    }

    private const string FirstEntryContent =
    $$$"""
        Oh, no!
    """;

    private const string EntryContent =
    $$$"""
        Life's puzzle pieces strewn,
        Connections elusive, like a hidden moon.
        Yet with each trial, we learn to fight.
    """;

    private const string LastEntryContent =
    $$$"""
        Find the purpose, and get it right!
    """;

    private const int UniqueStartOffset = 757;
    private const int UniqueEndOffset = 979;

    private ChatSlice[] GenerateExistingDocsForEntry(ChatEntryId chatEntryId, EntryToDocMap entryToDocMap)
    {
        var authorId = new PrincipalId(UserId.New(), AssumeValid.Option);
        var chatId = chatEntryId.ChatId;
        var numDocs = entryToDocMap.NumDocs;

        // Slice Entry content
        var contentSlices = new string[numDocs];
        var reader = new StringReader(EntryContent);
        var docId = 0;
        while (docId < numDocs - 1) {
            contentSlices[docId] = reader.ReadLine()!;
            docId++;
        }
        contentSlices[docId] = reader.ReadToEnd();

        // Create documents
        var documents = new List<ChatSlice>();
        var sliceStart = 0;
        for (var doc = 0; doc < numDocs; doc++) {
            var isFirstDoc = doc == 0;
            var isLastDoc = doc == numDocs - 1;
            var entries = new List<ChatSliceEntry>();
            var sbContent = new StringBuilder();
            if (isFirstDoc && !entryToDocMap.IsFirst) {
                var firstEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 1, AssumeValid.Option);
                entries.Add(new ChatSliceEntry(firstEntryId, 1, 1));
                sbContent.AppendLine(FirstEntryContent);
            }
            entries.Add(new ChatSliceEntry(chatEntryId, 1, 1));
            sbContent.Append(contentSlices[doc]);
            var sliceEnd = sliceStart + contentSlices[doc].Length;
            if (isLastDoc && !entryToDocMap.IsLast) {
                var lastEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 3, AssumeValid.Option);
                entries.Add(new ChatSliceEntry(lastEntryId, 1, 1));
                sbContent.AppendLine(LastEntryContent);
            }

            var startOffset = sliceStart > 0
                ? sliceStart
                : entryToDocMap.IsFirst ? default(int?) : UniqueStartOffset;
            var endOffset = isLastDoc
                ? entryToDocMap.IsLast ? default(int?) : UniqueEndOffset
                : sliceEnd;

            var metadata = new ChatSliceMetadata(
                [authorId],
                [.. entries], startOffset, endOffset,
                [], [], [], [],
                false,
                "en-US",
                DateTime.Now.AddMinutes(doc)
            );
            documents.Add(new ChatSlice(metadata, sbContent.ToString()));
            sliceStart = sliceEnd;
        }

        return [.. documents];
    }
}
