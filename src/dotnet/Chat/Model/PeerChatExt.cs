namespace ActualChat.Chat;

public enum PeerChatLinkKind { None, AuthorId, UserId }

public static class PeerChatExt
{
    private const string PeerChatIdPrefix = "p:";

    public static bool IsPeerChatId(string chatId)
        => chatId.StartsWith(PeerChatIdPrefix, StringComparison.Ordinal);

    public static bool IsAuthorsPeerChatId(string chatId)
        => IsPeerChatId(chatId) && chatId.Count(c => c == ':') == 3;

    public static string CreateAuthorsPeerChatId(
        string originalChatId,
        long chatAuthorLocalId1, long chatAuthorLocalId2)
    {
        if (chatAuthorLocalId1 > chatAuthorLocalId2)
            (chatAuthorLocalId1, chatAuthorLocalId2) = (chatAuthorLocalId2, chatAuthorLocalId1);
        return Invariant($"{PeerChatIdPrefix}{originalChatId}:{chatAuthorLocalId1}:{chatAuthorLocalId2}");
    }

    public static string CreatePeerChatLink(string targetPrincipalId)
        => Invariant($"{PeerChatIdPrefix}{targetPrincipalId}");

    public static string CreateUsersPeerChatId(string userId1, string userId2)
    {
        if (string.Compare(userId1, userId2, StringComparison.Ordinal) > 0)
            (userId1, userId2) = (userId2, userId1);
        return Invariant($"{PeerChatIdPrefix}{userId1}:{userId2}");
    }

    public static string GerOriginalChatId(string chatId)
    {
        if (!IsAuthorsPeerChatId(chatId))
            throw new InvalidOperationException();
        var startIndex = PeerChatIdPrefix.Length;
        var index = chatId.IndexOf(":", startIndex, StringComparison.Ordinal);
        var originalChatId = chatId.Substring(startIndex, index - startIndex);
        return originalChatId;
    }

    public static bool TryParseAuthorsPeerChatId(string chatId, out string originalChatId,
        out long chatAuthorLocalId1, out long chatAuthorLocalId2)
    {
        originalChatId = "";
        chatAuthorLocalId1 = 0;
        chatAuthorLocalId2 = 0;
        if (!IsPeerChatId(chatId))
            return false;
        var parts = chatId.Split(":");
        if (parts.Length != 4)
            return false;
        originalChatId = parts[1];
        if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out chatAuthorLocalId1))
            return false;
        if (!long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out chatAuthorLocalId2))
            return false;
        return true;
    }

    public static PeerChatLinkKind GetChatLinkKind(string chatIdentifier)
    {
        if (!IsPeerChatId(chatIdentifier))
            return PeerChatLinkKind.None;
        if (IsAuthorLink(chatIdentifier))
            return PeerChatLinkKind.AuthorId;
        if (IsUserLink(chatIdentifier))
            return PeerChatLinkKind.UserId;
        return PeerChatLinkKind.None;
    }

    private static bool IsAuthorLink(string chatId)
        => IsPeerChatId(chatId) && chatId.Count(c => c == ':') == 2;

    private static bool IsUserLink(string chatId)
        => IsPeerChatId(chatId) && chatId.Count(c => c == ':') == 1;

    public static string GetChatAuthorId(string chatIdentifier)
        => chatIdentifier.Substring(PeerChatIdPrefix.Length);

    public static string GetUserId(string chatIdentifier)
        => chatIdentifier.Substring(PeerChatIdPrefix.Length);
}
