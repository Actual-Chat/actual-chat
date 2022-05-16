namespace ActualChat.Chat;

public enum ChatIdKind { Group, PeerShort, PeerFull }

public static class ChatIdExt
{
    private const string PeerChatIdPrefix = "p-";

    public static bool IsPeerChatId(string chatId)
        => chatId.StartsWith(PeerChatIdPrefix, StringComparison.Ordinal);

    public static ChatIdKind GetChatIdKind(string chatId)
    {
        if (chatId.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(chatId));

        if (!IsPeerChatId(chatId))
            return ChatIdKind.Group;
        return chatId.Count(c => c == '-') switch {
            1 => ChatIdKind.PeerShort,
            2 => ChatIdKind.PeerFull,
            _ => throw new ArgumentOutOfRangeException(nameof(chatId)),
        };
    }

    public static string FormatShortPeerChatId(string peerUserId)
        => $"{PeerChatIdPrefix}{peerUserId}";

    public static string FormatFullPeerChatId(string userId1, string userId2)
    {
        if (string.Compare(userId1, userId2, StringComparison.Ordinal) > 0)
            (userId1, userId2) = (userId2, userId1);
        return $"{PeerChatIdPrefix}{userId1}-{userId2}";
    }

    public static string ParseShortPeerChatId(string chatId)
    {
        if (!TryParseShortPeerChatId(chatId, out var userId))
            throw new InvalidOperationException("Invalid short peer chat ID.");
        return userId;
    }

    public static (string UserId1, string UserId2) ParseFullPeerChatId(string chatId)
    {
        if (!TryParseFullPeerChatId(chatId, out var userId1, out var userId2))
            throw new InvalidOperationException("Invalid full peer chat ID.");
        return (userId1, userId2);
    }

    public static bool TryParseShortPeerChatId(string chatId, out string userId)
    {
        userId = "";
        if (!IsPeerChatId(chatId))
            return false;
        userId = chatId.Substring(PeerChatIdPrefix.Length);
        return true;
    }

    public static bool TryParseFullPeerChatId(string chatId, out string userId1, out string userId2)
    {
        userId1 = userId2 = "";
        if (!IsPeerChatId(chatId))
            return false;
        var parts = chatId.Split("-");
        if (parts.Length != 3)
            return false;
        userId1 = parts[1];
        userId2 = parts[2];
        return !userId1.IsNullOrEmpty() && !userId2.IsNullOrEmpty()
            && !StringComparer.Ordinal.Equals(userId1, userId2);
    }
}
