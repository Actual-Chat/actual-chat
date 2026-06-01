namespace ActualChat.Core.UnitTests.Identifiers;

public class IdentifierSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ChatId_Basic()
    {
        ChatId id = ChatId.Parse("the-actual-one");
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void GroupChatId_Basic()
    {
        var id = GroupChatId.Parse("1234abcd");
        ChatId chatId = id;
        chatId.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void PeerChatId_Basic()
    {
        var id = PeerChatId.New(UserId.Parse("admin1"), UserId.Parse("admin2"));
        ChatId chatId = id;
        chatId.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void PlaceChatId_Basic()
    {
        var placeId = PlaceId.New();
        var id = PlaceChatId.New(placeId);
        ChatId chatId = id;
        chatId.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void UserId_Basic()
    {
        var id = UserId.Parse("admin1");
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void UserId_Guest()
    {
        var id = UserId.NewGuest();
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void AuthorId_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var id = AuthorId.New(chatId, 5);
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void PlaceId_Basic()
    {
        var id = PlaceId.New();
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void ChatEntryId_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var id = ChatEntryId.New(chatId, 1);
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void TextEntryId_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var id = ChatEntryId.New(chatId, 1);
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void ConversationId_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var id = ConversationId.New(chatId, 0);
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void ContactId_Basic()
    {
        var userId = UserId.Parse("admin1");
        var chatId = ChatId.Parse("the-actual-one");
        var id = ContactId.NewAny(userId, chatId);
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void StreamId_Basic()
    {
        var id = StreamId.New(NodeRef.Parse("1234abcd"), "local1");
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void MediaId_Basic()
    {
        var id = MediaId.New("scope1", "local1");
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void RoleId_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var id = RoleId.New(chatId, 1);
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Language_Basic()
    {
        var id = Languages.English;
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void NotificationId_Basic()
    {
        var id = NotificationId.New(UserId.New(), NotificationKind.Message, "123");
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void ExternalContactId_Basic()
    {
        var userId = UserId.New();
        var deviceId = UserDeviceId.New(userId, "device1");
        var id = ExternalContactId.New(deviceId, "1");
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void UserDeviceId_Basic()
    {
        var userId = UserId.New();
        var id = UserDeviceId.New(userId, "device1");
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void TranslationId_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var sourceId = TranslationSourceId.New(ChatEntryId.New(chatId, 1));
        var id = TranslationId.New(sourceId, Languages.Russian);
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void TranslationSourceId_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var id = TranslationSourceId.New(ChatEntryId.New(chatId, 1));
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void AliasId_Basic()
    {
        var id = AliasId.Parse("test-alias");
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void MentionId_Basic()
    {
        var id = MentionRef.Parse("u:user123456");
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void Emoji_Basic()
    {
        var id = Emojis.ThumbsUp;
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void NodeRef_Basic()
    {
        var id = NodeRef.Parse("1234abcd");
        id.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void ExplicitNotificationId_Basic()
    {
        var id = ExplicitNotificationId.New(
            UserId.New(),
            ExplicitNotificationKind.NotifyMentionedMembers,
            "key-123");
        id.AssertPassesThroughAllSerializers();
    }
}
