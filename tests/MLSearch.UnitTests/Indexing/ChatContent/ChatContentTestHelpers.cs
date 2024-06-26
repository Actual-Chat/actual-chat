
using System.Text;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

internal static class ChatContentTestHelpers
{
    public static ChatSlice[] CreateDocuments()
    {
        var authorId = new PrincipalId(UserId.New(), AssumeValid.Option);
        var chatId = new ChatId(Generate.Option);
        var entryIds = Enumerable.Range(1, 4)
            .Select(id => new ChatEntryId(chatId, ChatEntryKind.Text, id, AssumeValid.Option))
            .ToArray();
        var textItems = new [] {
            "An accident happened to my brother Jim.",
            "Somebody threw a tomato at him.",
            "Tomatoes are juicy they can't hurt the skin.",
            "But this one was specially packed in a tin.",
        };
        return entryIds.Zip(textItems)
            .Select((args, i) => {
                var (id, text) = args;
                var metadata = new ChatSliceMetadata(
                    [authorId],
                    [new ChatSliceEntry(id, 1, 1)], null, null,
                    [], [], [], [],
                    false,
                    "en-US",
                    DateTime.Now.AddMinutes(-i)
                );
                return new ChatSlice(metadata, text);
            })
            .ToArray();
    }

    public static Mock<IChatContentMapper> MockDocMapper(Action<ChatSlice> onUpdatedDoc)
    {
        var authorId = new PrincipalId(UserId.New(), AssumeValid.Option);
        var docMapper = new Mock<IChatContentMapper>();
        docMapper
            .Setup(x => x.MapAsync(
                It.IsAny<SourceEntries>(),
                It.IsAny<CancellationToken>()))
            .Returns<SourceEntries, CancellationToken>((entries, _) => {
                var metadata = new ChatSliceMetadata(
                    [authorId],
                    [.. entries.Entries.Select(e => new ChatSliceEntry(e.Id, e.LocalId, e.Version))], entries.StartOffset, entries.EndOffset,
                    [], [], [], [],
                    false,
                    "en-US",
                    DateTime.Now
                );
                var content = entries.Entries.Select(e => e.Content)
                    .Aggregate(new StringBuilder(), (txt, ln) => txt.AppendLine(ln)).ToString();
                var updatedDoc = new ChatSlice(metadata, content);
                onUpdatedDoc(updatedDoc);
                return ValueTask.FromResult(updatedDoc);
            });
        return docMapper;
    }

}
