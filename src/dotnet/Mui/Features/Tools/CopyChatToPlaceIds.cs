namespace ActualChat.Mui;

public static class CopyChatToPlaceIds
{
    public static PlaceChatId GetCopiedChatId(ChatId sourceChatId, PlaceId placeId)
    {
        var localChatId = sourceChatId is PlaceChatId placeChatId
            ? placeChatId.LocalChatId
            : sourceChatId.Value;
        return PlaceChatId.Parse(PlaceChatId.Format(placeId, localChatId));
    }
}
