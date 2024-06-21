using System.Text;
using ActualChat.Chat;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

public class ChatContentIndexerTests(ITestOutputHelper @out) : TestBase(@out)
{
    private readonly PrincipalId _authorId = new (UserId.New(), AssumeValid.Option);

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
            bool[] bools = [false, true];
            var theoryData = new TheoryData<EntryToDocMap>();
            for (var docNumber = 1; docNumber < 4; docNumber++) {
                foreach (var isFirst in bools) {
                    foreach (var isLast in bools) {
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
        var chatEntry = new ChatEntry(chatEntryId, 0) {
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
                It.Is<bool>(x => x == true),
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
            .ReturnsAsync(existingDocs);
        ChatSlice? updatedDoc = null;
        var docMapper = new Mock<IChatContentMapper>();
        docMapper
            .Setup(x => x.MapAsync(
                It.IsAny<SourceEntries>(),
                It.IsAny<CancellationToken>()))
            .Returns<SourceEntries, CancellationToken>((entries, _) => {
                if (entries.Entries.Count == 0) {
                    return ValueTask.FromResult<IReadOnlyCollection<ChatSlice>>([]);
                }
                var metadata = new ChatSliceMetadata(
                    [_authorId],
                    [.. entries.Entries.Select(e => new ChatSliceEntry(e.Id, e.LocalId, e.Version))], entries.StartOffset, entries.EndOffset,
                    [], [], [], [],
                    false,
                    "en-US",
                    DateTime.Now
                );
                var content = entries.Entries.Select(e => e.Content)
                    .Aggregate(new StringBuilder(), (txt, ln) => txt.AppendLine(ln)).ToString();
                updatedDoc = new ChatSlice(metadata, content);
                return ValueTask.FromResult<IReadOnlyCollection<ChatSlice>>([updatedDoc]);
            });

        var sink = Mock.Of<ISink<ChatSlice, string>>();

        var contentIndexer = new ChatContentIndexer(chats.Object, docLoader.Object, docMapper.Object, sink);
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
                [_authorId],
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
