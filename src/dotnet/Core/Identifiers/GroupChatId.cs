using MemoryPack;
using ActualLab.Generators;

namespace ActualChat;

#pragma warning disable CA1036, MA0097 // Implement comparison operators: <, <=, etc.

public sealed class GroupChatId : IEquatable<GroupChatId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<GroupChatId>();
    internal static RandomStringGenerator IdGenerator { get; } = new(10, Alphabet.AlphaNumeric);

    public static GroupChatId None { get; }

    public Symbol Id { get; }

    static GroupChatId()
        => None = new (Symbol.Empty, AssumeValid.Option);

    public GroupChatId? ParentChatId { get; }
    public long ThreadId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsThread => ThreadId > 0;
    public ChatId GetThreadParentOrSelf() => IsThread ? ParentChatId! : this;

    internal static GroupChatId Group(string chatId)
        => new (chatId, AssumeValid.Option);

    public GroupChatId(Generate _)
        : this(IdGenerator.Next(), AssumeValid.Option)
    {
    }

    private GroupChatId(Symbol id, AssumeValid _)
    {
        Id = id;
        ThreadId = 0;
        ParentChatId = null;
    }

    private GroupChatId(Symbol id, GroupChatId parentChatId, long threadId, AssumeValid _)
    {
        if (id.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(id));
        if (parentChatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(parentChatId));
        if (threadId <= 0)
            throw new ArgumentOutOfRangeException(nameof(threadId));

        Id = id;
        ParentChatId = parentChatId;
        ThreadId = threadId;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(GroupChatId source) => source.Id;
    public static implicit operator string(GroupChatId source) => source.Id.Value;

    // Equality

    public bool Equals(GroupChatId? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => obj is GroupChatId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(GroupChatId left, GroupChatId right) => left.Equals(right);
    public static bool operator !=(GroupChatId left, GroupChatId right) => !left.Equals(right);

    // Parsing

    public static GroupChatId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<GroupChatId>(s);
    public static GroupChatId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<GroupChatId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out GroupChatId result)
    {
        result = None;
        if (s.IsNullOrEmpty())
            return true; // None

        if (s.Length < 6)
            return false;

        var span = s.AsSpan();
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
        if (!(Alphabet.AlphaNumeric.IsMatch(sRawChatId) || Constants.Chat.SystemChatIds.Contains(sRawChatId)))
            return false;

        // Group chat ID
        result = new GroupChatId(sRawChatId, AssumeValid.Option);
        if (threadIds is null)
            return true;

        foreach (var threadId in threadIds)
            result = result.CreateThreadId(threadId);
        return true;
    }

    private GroupChatId CreateThreadId(long threadId)
    {
        var s = Value + ActualChat.ChatId.ThreadIdSeparator + threadId.ToInvariantString();
        return new GroupChatId(s, this, threadId, AssumeValid.Option);
    }
}
