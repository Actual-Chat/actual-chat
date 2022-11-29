namespace ActualChat;

public static class PeerChatId
{
    public static ChatId New(UserId userId1, UserId userId2)
    {
        if (userId1.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(userId1));
        if (userId2.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(userId2));
        if (userId1 == userId2)
            throw new ArgumentOutOfRangeException(nameof(userId2), "Both user IDs are the same.");

        (userId1, userId2) = (userId1, userId2).Sort();
        return new($"p-{userId1}-{userId2}", ParseOptions.Skip);
    }

    public static (UserId UserId1, UserId UserId2) Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ChatId>();
    public static (UserId UserId1, UserId UserId2) ParseOrDefault(string? s)
        => TryParse(s, out var result) ? result : default;

    public static bool TryParse(string? s, out UserId userId1, out UserId userId2)
    {
        var result = TryParse(s, out var userIds);
        (userId1, userId2) = userIds;
        return result;
    }

    public static bool TryParse(string? s, out (UserId UserId1, UserId UserId2) result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return false;

        if (!s.OrdinalStartsWith("p-"))
            return false;

        var tail = s.AsSpan(2);
        var dashIndex = tail.IndexOf('-');
        if (dashIndex < 0)
            return false;

        if (!UserId.TryParse(tail[..dashIndex].ToString(), out var userId1))
            return false;
        if (!UserId.TryParse(tail[(dashIndex + 1)..].ToString(), out var userId2))
            return false;

        result = (userId1, userId2);
        return true;
    }
}
