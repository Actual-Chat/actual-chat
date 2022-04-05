namespace ActualChat.Chat;

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
        return PeerChatIdPrefix + originalChatId + ":" + chatAuthorLocalId1 + ":" + chatAuthorLocalId2;
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
}
