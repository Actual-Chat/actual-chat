namespace ActualChat.Chat;

public enum PeerChatIdKind { None, Short, Full }

public static class PeerChatExt
{
    private const string PeerChatIdPrefix = "p-";

    public static bool IsPeerChatId(string chatId)
        => chatId.StartsWith(PeerChatIdPrefix, StringComparison.Ordinal);

    public static PeerChatIdKind GetChatIdKind(string chatId)
    {
        if (!IsPeerChatId(chatId))
            return PeerChatIdKind.None;
        return chatId.Count(c => c == '-') switch {
            1 => PeerChatIdKind.Short,
            2 => PeerChatIdKind.Full,
            _ => PeerChatIdKind.None,
        };
    }

    public static string CreateShortPeerChatId(string targetPrincipalId)
        => $"{PeerChatIdPrefix}{targetPrincipalId}";

    public static string CreateFullPeerChatId(string userId1, string userId2)
    {
        if (string.Compare(userId1, userId2, StringComparison.Ordinal) > 0)
            (userId1, userId2) = (userId2, userId1);
        return $"{PeerChatIdPrefix}{userId1}-{userId2}";
    }

    public static string ParseShortPeerChatId(string shortChatId)
    {
        if (!TryParseShortPeerChatId(shortChatId, out var userId))
            throw new InvalidOperationException("Invalid short peer chat ID.");
        return userId;
    }

    public static (string UserId1, string UserId2) ParseFullPeerChatId(string fullChatId)
    {
        if (!TryParseFullPeerChatId(fullChatId, out var userId1, out var userId2))
            throw new InvalidOperationException("Invalid full peer chat ID.");
        return (userId1, userId2);
    }

    public static bool TryParseShortPeerChatId(string shortChatId, out string userId)
    {
        userId = "";
        if (!IsPeerChatId(shortChatId))
            return false;
        userId = shortChatId.Substring(PeerChatIdPrefix.Length);
        return true;
    }

    public static bool TryParseFullPeerChatId(string fullChatId, out string userId1, out string userId2)
    {
        userId1 = userId2 = "";
        if (!IsPeerChatId(fullChatId))
            return false;
        var parts = fullChatId.Split("-");
        if (parts.Length != 3)
            return false;
        userId1 = parts[1];
        userId2 = parts[2];
        return !userId1.IsNullOrEmpty() && !userId2.IsNullOrEmpty()
            && !StringComparer.Ordinal.Equals(userId1, userId2);
    }
}
