namespace ActualChat;

internal class LocalChatId
{
    public string Id { get; }
    public LocalChatId? Parent { get; }
    public long ThreadId { get; }
    public bool IsTread => ThreadId > 0;

    public static LocalChatId New(string id)
        => new (id);

    private LocalChatId(string id)
    {
        Id = id;
        Parent = null;
        ThreadId = 0;
    }

    private LocalChatId(string id, LocalChatId parentChatId, long threadId)
    {
        if (id.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(id));
        if (threadId <= 0)
            throw new ArgumentOutOfRangeException(nameof(threadId));

        Id = id;
        Parent = parentChatId;
        ThreadId = threadId;
    }

    public static bool TryParse(ReadOnlySpan<char> s, Func<string, bool>? allowNonStandardChatIdFunc, [NotNullWhen(true)] out LocalChatId? result)
    {
        result = null;
        if (s.Length < 6)
            return false;

        var span = s;
        List<long>? threadIds = null;
        while (true) {
            var threadIdIndex = span.LastIndexOf(ActualChat.ChatId.ThreadIdSeparator);
            if (threadIdIndex < 0)
                break;

            var sThreadId = span.Slice(threadIdIndex + 1);
            if (!long.TryParse(sThreadId, CultureInfo.InvariantCulture, out var threadId))
                break;

            threadIds ??= new List<long>();
            threadIds.Insert(0, threadId);
            span = span.Slice(0, threadIdIndex);
        }
        if (span.Length < 6)
            return false;

        var sRawChatId = span.ToString();
        if (!(Alphabet.AlphaNumeric.IsMatch(sRawChatId)
            || (allowNonStandardChatIdFunc is not null && allowNonStandardChatIdFunc(sRawChatId))))
            return false;

        // Local chat ID
        result = new LocalChatId(sRawChatId);
        if (threadIds is null)
            return true;

        foreach (var threadId in threadIds)
            result = result.CreateThreadId(threadId);
        return true;
    }

    private LocalChatId CreateThreadId(long threadId)
    {
        var s = Id + ChatId.ThreadIdSeparator + threadId.ToInvariantString();
        return new LocalChatId(s, this, threadId);
    }
}
