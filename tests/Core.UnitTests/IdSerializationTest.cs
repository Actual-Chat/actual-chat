namespace ActualChat.Core.UnitTests;

public class IdSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void TwoWaySerializationOfIdsInOptionSetShouldWork()
    {
        var optionSet = new OptionSet();
        var userId = UserId.New();
        var peerChatId = new PeerChatId(userId, UserId.New());
        var chatId = new ChatId(Generate.Option);
        var placeId = new PlaceId(Generate.Option);
        var authorId = new AuthorId(chatId, 100, AssumeValid.Option);
        optionSet.SetId(userId);
        optionSet.SetId(peerChatId);
        optionSet.SetId(chatId);
        optionSet.SetId(placeId);
        optionSet.SetId(authorId);
        var optionSetSerialized = new NewtonsoftJsonSerialized<OptionSet> {
            Value = optionSet,
        };
        var optionSetString = optionSetSerialized.Data;
        var optionSetDeserialized = new NewtonsoftJsonSerialized<OptionSet> {
            Data = optionSetString,
        };
        var resultOptionSet = optionSetDeserialized.Value;

        var resultUserId = resultOptionSet.GetId<UserId>();
        var resultPeerChatId = resultOptionSet.GetId<PeerChatId>();
        var resultChatId = resultOptionSet.GetId<ChatId>();
        var resultPlaceId = resultOptionSet.GetId<PlaceId>();
        var resultAuthorId = resultOptionSet.GetId<AuthorId>();

        resultUserId.Should().Be(userId);
        resultPeerChatId.Should().Be(peerChatId);
        resultChatId.Should().Be(chatId);
        resultPlaceId.Should().Be(placeId);
        resultAuthorId.Should().Be(authorId);
    }
}
