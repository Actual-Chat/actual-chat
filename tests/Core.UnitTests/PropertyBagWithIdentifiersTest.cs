namespace ActualChat.Core.UnitTests;

public class PropertyBagWithIdentifiersTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void PropertyBagTest()
    {
        var p = new PropertyBag();
        var userId = UserId.New();
        var peerChatId = new PeerChatId(userId, UserId.New());
        var chatId = new ChatId(Generate.Option);
        var placeId = new PlaceId(Generate.Option);
        var authorId = new AuthorId(chatId, 100, AssumeValid.Option);
        p = p.KeylessSet("X");
        p = p.KeylessSet(userId);
        p = p.KeylessSet(peerChatId);
        p = p.KeylessSet(chatId);
        p = p.KeylessSet(placeId);
        p = p.KeylessSet(authorId);

        var p1 = p.PassThroughAllSerializers();
        var x = p.KeylessGet<string>("");
        var userId1 = p1.KeylessGet<UserId>();
        var peerChatId1 = p1.KeylessGet<PeerChatId>();
        var chatId1 = p1.KeylessGet<ChatId>();
        var placeId1 = p1.KeylessGet<PlaceId>();
        var authorId1 = p1.KeylessGet<AuthorId>();
        x.Should().Be("X");
        userId1.Should().Be(userId);
        peerChatId1.Should().Be(peerChatId);
        chatId1.Should().Be(chatId);
        placeId1.Should().Be(placeId);
        authorId1.Should().Be(authorId);
    }

    [Fact]
    public void MutablePropertyBagTest()
    {
        var p = new MutablePropertyBag();
        var userId = UserId.New();
        var peerChatId = new PeerChatId(userId, UserId.New());
        var chatId = new ChatId(Generate.Option);
        var placeId = new PlaceId(Generate.Option);
        var authorId = new AuthorId(chatId, 100, AssumeValid.Option);
        p.KeylessSet("X");
        p.KeylessSet(userId);
        p.KeylessSet(peerChatId);
        p.KeylessSet(chatId);
        p.KeylessSet(placeId);
        p.KeylessSet(authorId);

        var p1 = p.PassThroughAllSerializers();
        var x = p.KeylessGet<string>("");
        var userId1 = p1.KeylessGet<UserId>();
        var peerChatId1 = p1.KeylessGet<PeerChatId>();
        var chatId1 = p1.KeylessGet<ChatId>();
        var placeId1 = p1.KeylessGet<PlaceId>();
        var authorId1 = p1.KeylessGet<AuthorId>();
        x.Should().Be("X");
        userId1.Should().Be(userId);
        peerChatId1.Should().Be(peerChatId);
        chatId1.Should().Be(chatId);
        placeId1.Should().Be(placeId);
        authorId1.Should().Be(authorId);
    }
}
