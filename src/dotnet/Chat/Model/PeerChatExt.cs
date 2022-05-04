namespace ActualChat.Chat;

public enum PeerChatShortIdKind { None, UserId }

public enum PeerChatIdKind { None, UserIds }

public static class PeerChatExt
{
    private const string PeerChatIdPrefix = "p:";

    public static bool IsPeerChatId(string chatId)
        => chatId.StartsWith(PeerChatIdPrefix, StringComparison.Ordinal);

    public static string CreatePeerChatLink(string targetPrincipalId)
        => Invariant($"{PeerChatIdPrefix}{targetPrincipalId}");

    public static string CreateUsersPeerChatId(string userId1, string userId2)
    {
        if (string.Compare(userId1, userId2, StringComparison.Ordinal) > 0)
            (userId1, userId2) = (userId2, userId1);
        return Invariant($"{PeerChatIdPrefix}{userId1}:{userId2}");
    }

    public static bool TryParseUsersPeerChatId(string chatId, out string userId1, out string userId2)
    {
        userId1 = userId2 = "";
        if (!IsPeerChatId(chatId))
            return false;
        var parts = chatId.Split(":");
        if (parts.Length != 3)
            return false;
        userId1 = parts[1];
        userId2 = parts[2];
        return !string.IsNullOrEmpty(userId1) && !string.IsNullOrEmpty(userId2)
            && !StringComparer.Ordinal.Equals(userId1, userId2);
    }

    public static PeerChatShortIdKind GetChatShortIdKind(string chatShortId)
    {
        if (!IsPeerChatId(chatShortId))
            return PeerChatShortIdKind.None;
        if (IsUserLink(chatShortId))
            return PeerChatShortIdKind.UserId;
        return PeerChatShortIdKind.None;
    }

    public static PeerChatIdKind GetChatIdKind(string chatId)
    {
        if (!IsPeerChatId(chatId))
            return PeerChatIdKind.None;
        if (IsUsersPeerChatId(chatId))
            return PeerChatIdKind.UserIds;
        return PeerChatIdKind.None;
    }

    public static string GetUserId(string chatShortId)
        => chatShortId.Substring(PeerChatIdPrefix.Length);

    private static bool IsUserLink(string chatId)
        => chatId.Count(c => c == ':') == 1;

    private static bool IsUsersPeerChatId(string chatId)
        => chatId.Count(c => c == ':') == 2;
}
