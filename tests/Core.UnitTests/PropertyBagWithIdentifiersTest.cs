namespace ActualChat.Core.UnitTests;

public class PropertyBagWithIdentifiersTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void PropertyBagTest()
    {
        var p = new PropertyBag();
        var userId = UserId.New();
        var groupChatId = GroupChatId.New();
        var peerChatId = PeerChatId.New(userId, UserId.New());
        var placeId = PlaceId.New();
        var authorId = AuthorId.New(groupChatId, 100);
        p = p.KeylessSet("X");
        p = p.KeylessSet(userId);
        p = p.KeylessSet(peerChatId);
        p = p.KeylessSet(groupChatId);
        p = p.KeylessSet((ChatId)groupChatId);
        p = p.KeylessSet(placeId);
        p = p.KeylessSet(authorId);

        var p1 = p.PassThroughSerializers();
        var x = p.KeylessGet<string>("");
        var userId1 = p1.KeylessGet<UserId>();
        var peerChatId1 = p1.KeylessGet<PeerChatId>();
        var groupChatId1 = p1.KeylessGet<GroupChatId>();
        var chatId1 = p1.KeylessGet<ChatId>();
        var placeId1 = p1.KeylessGet<PlaceId>();
        var authorId1 = p1.KeylessGet<AuthorId>();
        x.Should().Be("X");
        userId1.Should().Be(userId);
        peerChatId1.Should().Be(peerChatId);
        groupChatId1.Should().Be(groupChatId);
        chatId1.Should().Be(groupChatId);
        placeId1.Should().Be(placeId);
        authorId1.Should().Be(authorId);
    }

    [Fact]
    public void MutablePropertyBagTest()
    {
        var p = new MutablePropertyBag();
        var userId = UserId.New();
        var groupChatId = GroupChatId.New();
        var peerChatId = PeerChatId.New(userId, UserId.New());
        var placeId = PlaceId.New();
        var authorId = AuthorId.New(groupChatId, 100);
        p.KeylessSet("X");
        p.KeylessSet(userId);
        p.KeylessSet(peerChatId);
        p.KeylessSet(groupChatId);
        p.KeylessSet((ChatId)groupChatId);
        p.KeylessSet(placeId);
        p.KeylessSet(authorId);

        var p1 = p.PassThroughSerializers();
        var x = p.KeylessGet<string>("");
        var userId1 = p1.KeylessGet<UserId>();
        var peerChatId1 = p1.KeylessGet<PeerChatId>();
        var groupChatId1 = p1.KeylessGet<GroupChatId>();
        var chatId1 = p1.KeylessGet<ChatId>();
        var placeId1 = p1.KeylessGet<PlaceId>();
        var authorId1 = p1.KeylessGet<AuthorId>();
        x.Should().Be("X");
        userId1.Should().Be(userId);
        peerChatId1.Should().Be(peerChatId);
        groupChatId1.Should().Be(groupChatId);
        chatId1.Should().Be(groupChatId);
        placeId1.Should().Be(placeId);
        authorId1.Should().Be(authorId);
    }
}
