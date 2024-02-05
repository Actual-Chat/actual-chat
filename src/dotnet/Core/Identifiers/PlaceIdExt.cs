namespace ActualChat;

public static class PlaceIdExt
{
    public static ChatId ToRootChatId(this PlaceId placeId)
        => placeId.IsNone ? ChatId.None : PlaceChatId.Root(placeId);
}
