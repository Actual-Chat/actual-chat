namespace ActualChat.Mui.UnitTests;

public class CopyChatToPlaceIdsTest
{
    [Fact]
    public void GroupChatKeepsItsIdInsideThePlace()
    {
        // arrange
        var chatId = ChatId.Parse("abcdefghij");
        var placeId = PlaceId.Parse("klmnopqrst");

        // act
        var copiedChatId = CopyChatToPlaceIds.GetCopiedChatId(chatId, placeId);

        // assert
        copiedChatId.PlaceId.Should().Be(placeId);
        copiedChatId.LocalChatId.Should().Be("abcdefghij");
    }

    [Fact]
    public void PlaceChatKeepsItsLocalIdInsideTheNewPlace()
    {
        // arrange
        var placeId = PlaceId.Parse("klmnopqrst");
        var otherPlaceId = PlaceId.Parse("uvwxyzabcd");
        var chatId = PlaceChatId.Parse(PlaceChatId.Format(otherPlaceId, "abcdefghij"));

        // act
        var copiedChatId = CopyChatToPlaceIds.GetCopiedChatId(chatId, placeId);

        // assert
        copiedChatId.PlaceId.Should().Be(placeId);
        copiedChatId.LocalChatId.Should().Be("abcdefghij");
    }
}
