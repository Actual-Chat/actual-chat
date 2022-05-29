namespace ActualChat.Chat;

public static class ChatIdTypeExt
{
    public static ChatType ToChatType(this ChatIdType chatIdType)
        => chatIdType switch {
            ChatIdType.Group => ChatType.Group,
            ChatIdType.PeerShort => ChatType.Peer,
            ChatIdType.PeerFull => ChatType.Peer,
            _ => throw new ArgumentOutOfRangeException(nameof(chatIdType), chatIdType, null),
        };
}
