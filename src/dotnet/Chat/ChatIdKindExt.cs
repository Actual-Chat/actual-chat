namespace ActualChat.Chat;

public static class ChatIdKindExt
{
    public static bool IsPeerAny(this ChatIdKind chatIdKind)
        => chatIdKind is ChatIdKind.PeerFull or ChatIdKind.PeerShort;

    public static ChatType ToChatType(this ChatIdKind chatIdKind)
        => chatIdKind switch {
            ChatIdKind.Group => ChatType.Group,
            ChatIdKind.PeerShort => ChatType.Peer,
            ChatIdKind.PeerFull => ChatType.Peer,
            _ => throw new ArgumentOutOfRangeException(nameof(chatIdKind), chatIdKind, null),
        };
}
