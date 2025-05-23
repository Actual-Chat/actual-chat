using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.UnitTests.Documents.ChatSlice;

public class ChatSliceMetadataTests(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void EmptyStructurePropertiesHaveExpectedDefaults()
    {
        var emptyMetadata = new ChatSliceMetadata();
        Assert.True(emptyMetadata.Authors.IsDefault);
        Assert.True(emptyMetadata.ChatEntries.IsDefault);
        Assert.Null(emptyMetadata.StartOffset);
        Assert.Null(emptyMetadata.EndOffset);
        Assert.True(emptyMetadata.ReplyToEntries.IsDefault);
        Assert.True(emptyMetadata.Mentions.IsDefault);
        Assert.True(emptyMetadata.Reactions.IsDefault);
        Assert.True(emptyMetadata.Attachments.IsDefault);
        Assert.Null(emptyMetadata.Language);
        Assert.Equal(default, emptyMetadata.ContentTimestamp);

        Assert.Null(emptyMetadata.ChatId);
        Assert.Null(emptyMetadata.PlaceId);
    }

    [Fact]
    public void ValuesCanBeReadAfterInitialization()
    {
        var authors = ImmutableArray.Create<PrincipalId>(UserId.New());
        var chatId = GroupChatId.New();
        var chatEntryId1 = TextEntryId.New(chatId, 1);
        var chatEntryId2 = TextEntryId.New(chatId, 2);
        var chatEntries = ImmutableArray.Create<ChatSliceEntry>(new (chatEntryId1, 1, 1), new (chatEntryId2, 2, 1));
        var (startOffset, endOffset) = (0, 100);
        var replyToEntries = ImmutableArray.Create(TextEntryId.New(chatId, 100));
        var activeUser = (PrincipalId)UserId.New();
        var mentions = ImmutableArray.Create(activeUser);
        var reactions = ImmutableArray.Create(activeUser);
        var attachments = ImmutableArray.Create(
            new ChatSliceAttachment(MediaId.New("chat"), "summary1"),
            new ChatSliceAttachment(MediaId.New("chat"), "summary2")
        );
        const string lang = "en-US";
        var timestamp = DateTime.Now;

        var metadata = new ChatSliceMetadata(
            authors, chatEntries, startOffset, endOffset,
            replyToEntries, mentions, reactions, attachments,
            lang, timestamp
        );

        Assert.Equal(authors, metadata.Authors);
        Assert.Equal(chatEntries, metadata.ChatEntries);
        Assert.Equal(startOffset, metadata.StartOffset);
        Assert.Equal(endOffset, metadata.EndOffset);
        Assert.Equal(replyToEntries, metadata.ReplyToEntries);
        Assert.Equal(mentions, metadata.Mentions);
        Assert.Equal(reactions, metadata.Reactions);
        Assert.Equal(attachments, metadata.Attachments);
        Assert.Equal(lang, metadata.Language);
        Assert.Equal(timestamp, metadata.ContentTimestamp);
    }

    [Fact]
    public void ChatIdAndPlaceIdCanBeReadProperly()
    {
        var chatId = GroupChatId.New();
        var chatEntryId = TextEntryId.New(chatId, 1);
        var metadata = CreateMetadata(chatEntryId);
        Assert.Equal(chatId, metadata.ChatId);
        Assert.Null(metadata.PlaceId);

        var placeId = PlaceId.New();
        var rootChatId = placeId.RootChatId;
        var rootChatEntryId = TextEntryId.New(rootChatId, 1);
        var rootChatMetadata = CreateMetadata(rootChatEntryId);
        Assert.Equal(rootChatId, rootChatMetadata.ChatId);
        Assert.Equal(placeId, rootChatMetadata.PlaceId);

        var placeChatId = PlaceChatId.New(placeId);
        var placeChatEntryId = TextEntryId.New(placeChatId, 1);
        var placeChatMetadata = CreateMetadata(placeChatEntryId);
        Assert.Equal(placeChatId, placeChatMetadata.ChatId);
        Assert.Equal(placeId, placeChatMetadata.PlaceId);

        static ChatSliceMetadata CreateMetadata(TextEntryId chatEntryId) => new (
            [AuthorId.New(chatEntryId.ChatId, 1)],
            [new (chatEntryId, 1, 1)], null, null,
            [], [], [], [],
            "en-US",
            DateTime.Now
        );
    }
}
