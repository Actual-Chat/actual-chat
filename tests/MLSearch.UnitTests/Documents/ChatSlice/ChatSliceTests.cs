using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.UnitTests.Documents.ChatSlice;

public class IndexedDocumentTests(ITestOutputHelper @out): TestBase(@out)
{
    [Fact]
    public void IdOfAnEmptyChatSliceIsEmptyString()
    {
        var emptyDocument = new MLSearch.Documents.ChatSlice(default, string.Empty);
        Assert.Equal(string.Empty, emptyDocument.Id);
    }

    [Fact]
    public void ChatSliceIdDependsOnFirstChatEntryAndStartOffset()
    {
        var chatId = GroupChatId.New();
        var chatEntryId1 = TextEntryId.New(chatId, 1);
        var chatEntryId2 = TextEntryId.New(chatId, 2);
        var metadata = CreateMetadata(chatEntryId1, chatEntryId2, 33, 111);
        var document = new MLSearch.Documents.ChatSlice(metadata, string.Empty);
        var id = document.Id;
        Assert.StartsWith(chatEntryId1.Value, id, StringComparison.Ordinal);
        Assert.EndsWith("33", id, StringComparison.Ordinal);

        static ChatSliceMetadata CreateMetadata(TextEntryId chatEntryId1, TextEntryId chatEntryId2, int startOffset, int endOffset) => new (
            [null!],
            [new (chatEntryId1, 1, 1), new (chatEntryId2, 2, 1)], startOffset, endOffset,
            [], [], [], [],
            "en-US",
            DateTime.Now
        );
    }
}
