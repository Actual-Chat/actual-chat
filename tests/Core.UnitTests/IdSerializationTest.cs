namespace ActualChat.Core.UnitTests;

public class IdSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void TwoWaySerializationOfIdsInOptionSetShouldWork()
    {
        var p0 = new MutablePropertyBag();
        var userId = UserId.New();
        var peerChatId = new PeerChatId(userId, UserId.New());
        var chatId = new ChatId(Generate.Option);
        var placeId = new PlaceId(Generate.Option);
        var authorId = new AuthorId(chatId, 100, AssumeValid.Option);
        p0.SetId(userId);
        p0.SetId(peerChatId);
        p0.SetId(chatId);
        p0.SetId(placeId);
        p0.SetId(authorId);
        var serialized = NewtonsoftJsonSerialized.New(p0);
        var serializedData = serialized.Data;

        var deserialized = new NewtonsoftJsonSerialized<MutablePropertyBag>() { Data = serializedData };
        var p1 = deserialized.Value;

        var resultUserId = p1.GetId<UserId>();
        var resultPeerChatId = p1.GetId<PeerChatId>();
        var resultChatId = p1.GetId<ChatId>();
        var resultPlaceId = p1.GetId<PlaceId>();
        var resultAuthorId = p1.GetId<AuthorId>();

        resultUserId.Should().Be(userId);
        resultPeerChatId.Should().Be(peerChatId);
        resultChatId.Should().Be(chatId);
        resultPlaceId.Should().Be(placeId);
        resultAuthorId.Should().Be(authorId);
    }
}
