namespace ActualChat.Internal;

/// <summary>
/// Represents the local portion of a chat identifier for parsing purposes.
/// </summary>
internal sealed class ParsedLocalChatId
{
    public string Id { get; }
    public ParsedLocalChatId? Parent { get; }
    public long ThreadId { get; }
    public bool IsThread => ThreadId > 0;

    public static ParsedLocalChatId New(string id)
        => new (id);

    private ParsedLocalChatId(string id)
    {
        Id = id;
        Parent = null;
        ThreadId = 0;
    }

    private ParsedLocalChatId(string id, ParsedLocalChatId parentChatId, long threadId)
    {
        if (id.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(id));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threadId);

        Id = id;
        Parent = parentChatId;
        ThreadId = threadId;
    }

    public static bool TryParse(
        ReadOnlySpan<char> s,
        Func<string, bool>? isSpecialChatIdPredicate,
        [NotNullWhen(true)] out ParsedLocalChatId? result)
    {
        result = null;
        if (s.Length < 6)
            return false;

        var span = s;
        List<long>? threadIds = null;
        while (true) {
            var threadIdIndex = span.LastIndexOf(ChatId.ThreadIdSeparator);
            if (threadIdIndex < 0)
                break;

            var sThreadId = span.Slice(threadIdIndex + 1);
            if (!long.TryParse(sThreadId, out var threadId))
                break;

            threadIds ??= new List<long>();
            threadIds.Insert(0, threadId);
            span = span.Slice(0, threadIdIndex);
        }
        if (span.Length < 6)
            return false;

        var sRawChatId = span.ToString();
        if (!(Alphabet.AlphaNumeric.IsMatch(sRawChatId) || isSpecialChatIdPredicate?.Invoke(sRawChatId) == true))
            return false;

        // Local chat ID
        result = new ParsedLocalChatId(sRawChatId);
        if (threadIds is null)
            return true;

        foreach (var threadId in threadIds)
            result = result.CreateThreadId(threadId);
        return true;
    }

    // Private methods

    private ParsedLocalChatId CreateThreadId(long threadId)
    {
        var s = $"{Id}{ChatId.ThreadIdSeparator}{threadId}";
        return new ParsedLocalChatId(s, this, threadId);
    }
}
