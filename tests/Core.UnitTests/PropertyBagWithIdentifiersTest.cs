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
        p = p.Set("X");
        p = p.Set(userId);
        p = p.Set(peerChatId);
        p = p.Set(chatId);
        p = p.Set(placeId);
        p = p.Set(authorId);

        var p1 = p.PassThroughAllSerializers();
        var x = p.GetOrDefault<string>("");
        var userId1 = p1.GetOrDefault<UserId>();
        var peerChatId1 = p1.GetOrDefault<PeerChatId>();
        var chatId1 = p1.GetOrDefault<ChatId>();
        var placeId1 = p1.GetOrDefault<PlaceId>();
        var authorId1 = p1.GetOrDefault<AuthorId>();
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
        p.Set("X");
        p.Set(userId);
        p.Set(peerChatId);
        p.Set(chatId);
        p.Set(placeId);
        p.Set(authorId);

        var p1 = p.PassThroughAllSerializers();
        var x = p.GetOrDefault<string>("");
        var userId1 = p1.GetOrDefault<UserId>();
        var peerChatId1 = p1.GetOrDefault<PeerChatId>();
        var chatId1 = p1.GetOrDefault<ChatId>();
        var placeId1 = p1.GetOrDefault<PlaceId>();
        var authorId1 = p1.GetOrDefault<AuthorId>();
        x.Should().Be("X");
        userId1.Should().Be(userId);
        peerChatId1.Should().Be(peerChatId);
        chatId1.Should().Be(chatId);
        placeId1.Should().Be(placeId);
        authorId1.Should().Be(authorId);
    }
}
