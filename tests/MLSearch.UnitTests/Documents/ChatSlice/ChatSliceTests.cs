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
        var chatId = new ChatId(Generate.Option);
        var chatEntryId1 = new ChatEntryId(chatId, ChatEntryKind.Text, 1, AssumeValid.Option);
        var chatEntryId2 = new ChatEntryId(chatId, ChatEntryKind.Text, 2, AssumeValid.Option);
        var metadata = CreateMetadata(chatEntryId1, chatEntryId2, 33, 111);
        var document = new MLSearch.Documents.ChatSlice(metadata, string.Empty);
        var id = document.Id;
        Assert.StartsWith(chatEntryId1, id, StringComparison.Ordinal);
        Assert.EndsWith("33", id, StringComparison.Ordinal);

        static ChatSliceMetadata CreateMetadata(ChatEntryId chatEntryId1, ChatEntryId chatEntryId2, int startOffset, int endOffset) => new (
            PrincipalId.None,
            [new (chatEntryId1, 1), new (chatEntryId2, 1)], startOffset, endOffset,
            [], [], [], [], [],
            false,
            "en-US",
            DateTime.Now
        );
    }
}
