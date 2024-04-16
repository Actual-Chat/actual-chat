using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.UnitTests.Documents.ChatSlice;

public class ChatSliceMetadataTests(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void EmptyStructurePropertiesHaveExpectedDefaults()
    {
        var emptyMetadata = new ChatSliceMetadata();
        Assert.Equal(PrincipalId.None, emptyMetadata.AuthorId);
        Assert.True(emptyMetadata.ChatEntries.IsDefault);
        Assert.Null(emptyMetadata.StartOffset);
        Assert.Null(emptyMetadata.EndOffset);
        Assert.True(emptyMetadata.ReplyToEntries.IsDefault);
        Assert.True(emptyMetadata.Mentions.IsDefault);
        Assert.True(emptyMetadata.Reactions.IsDefault);
        Assert.True(emptyMetadata.ConversationParticipants.IsDefault);
        Assert.True(emptyMetadata.Attachments.IsDefault);
        Assert.False(emptyMetadata.IsPublic);
        Assert.Null(emptyMetadata.Language);
        Assert.Equal(default, emptyMetadata.Timestamp);

        Assert.Equal(ChatId.None, emptyMetadata.ChatId);
        Assert.Equal(PlaceId.None, emptyMetadata.PlaceId);
    }

    [Fact]
    public void ValuesCanBeReadAfterInitialization()
    {
        var authorId = new PrincipalId(UserId.New(), AssumeValid.Option);
        var chatId = new ChatId(Generate.Option);
        var chatEntryId1 = new ChatEntryId(chatId, ChatEntryKind.Text, 1, AssumeValid.Option);
        var chatEntryId2 = new ChatEntryId(chatId, ChatEntryKind.Text, 2, AssumeValid.Option);
        var chatEntries = ImmutableArray.Create<ChatSliceEntry>(new (chatEntryId1, 1, 1), new (chatEntryId2, 2, 1));
        var (startOffset, endOffset) = (0, 100);
        var replyToEntries = ImmutableArray.Create(new ChatEntryId(chatId, ChatEntryKind.Text, 100, AssumeValid.Option));
        var activeUser = new PrincipalId(UserId.New(), AssumeValid.Option);
        var mentions = ImmutableArray.Create(activeUser);
        var reactions = ImmutableArray.Create(activeUser);
        var participants = ImmutableArray.Create(authorId, activeUser);
        var attachments = ImmutableArray.Create(
            new ChatSliceAttachment(new MediaId("chat", Generate.Option), "summary1"),
            new ChatSliceAttachment(new MediaId("chat", Generate.Option), "summary2")
        );
        const string lang = "en-US";
        var timestamp = DateTime.Now;

        var metadata = new ChatSliceMetadata(
            authorId, chatEntries, startOffset, endOffset,
            replyToEntries, mentions, reactions, participants, attachments,
            true, lang, timestamp
        );

        Assert.Equal(authorId, metadata.AuthorId);
        Assert.Equal(chatEntries, metadata.ChatEntries);
        Assert.Equal(startOffset, metadata.StartOffset);
        Assert.Equal(endOffset, metadata.EndOffset);
        Assert.Equal(replyToEntries, metadata.ReplyToEntries);
        Assert.Equal(mentions, metadata.Mentions);
        Assert.Equal(reactions, metadata.Reactions);
        Assert.Equal(participants, metadata.ConversationParticipants);
        Assert.Equal(attachments, metadata.Attachments);
        Assert.True(metadata.IsPublic);
        Assert.Equal(lang, metadata.Language);
        Assert.Equal(timestamp, metadata.Timestamp);
    }

    [Fact]
    public void ChatIdAndPlaceIdCanBeReadProperly()
    {
        var chatId = new ChatId(Generate.Option);
        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 1, AssumeValid.Option);
        var metadata = CreateMetadata(chatEntryId);
        Assert.Equal(chatId, metadata.ChatId);
        Assert.Equal(PlaceId.None, metadata.PlaceId);

        var placeId = new PlaceId(Generate.Option);
        var placeRootChatId = PlaceChatId.Root(placeId);
        var chatId1 = ChatId.Place(placeRootChatId);
        var placeRootChatEntryId = new ChatEntryId(chatId1, ChatEntryKind.Text, 1, AssumeValid.Option);
        var rootPlaceChatMetadata = CreateMetadata(placeRootChatEntryId);
        Assert.Equal(placeRootChatId.Id, chatId1);
        Assert.Equal(chatId1, rootPlaceChatMetadata.ChatId);
        Assert.Equal(placeId, rootPlaceChatMetadata.PlaceId);

        var placeChatId = new PlaceChatId(placeId, Generate.Option);
        var chatId2 = ChatId.Place(placeChatId);
        var placeChatEntryId = new ChatEntryId(chatId2, ChatEntryKind.Text, 1, AssumeValid.Option);
        var placeChatMetadata = CreateMetadata(placeChatEntryId);
        Assert.Equal(placeChatId.Id, chatId2);
        Assert.Equal(chatId2, placeChatMetadata.ChatId);
        Assert.Equal(placeId, placeChatMetadata.PlaceId);

        static ChatSliceMetadata CreateMetadata(ChatEntryId chatEntryId) => new (
            PrincipalId.None,
            [new (chatEntryId, 1, 1)], null, null,
            [], [], [], [], [],
            false,
            "en-US",
            DateTime.Now
        );
    }
}
