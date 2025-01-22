namespace ActualChat;

public static class ContactIdExt
{
    public static UserId GetOtherUserId(this ContactId id)
        => !id.ChatId.IsPeerChat(out var peerChatId) ? UserId.None : peerChatId.AnotherUserIdOrDefault(id.OwnerId);
}
