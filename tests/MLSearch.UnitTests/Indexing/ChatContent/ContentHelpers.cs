
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

internal static class ContentHelpers
{
    public static ChatSlice[] CreateDocuments()
    {
        var authorId = new PrincipalId(UserId.New(), AssumeValid.Option);
        var chatId = new ChatId(Generate.Option);
        var entryIds = Enumerable.Range(1, 4)
            .Select(id => new ChatEntryId(chatId, ChatEntryKind.Text, id, AssumeValid.Option))
            .ToArray();
        var textItems = new [] {
            "An accident happend to my brother Jim.",
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
}
