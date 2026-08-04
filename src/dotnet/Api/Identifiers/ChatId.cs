using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Unique identifier for a chat (group, peer, place, or thread).
/// </summary>
[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringLikeJsonConverter<ChatId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<ChatId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<ChatId>))]
[TypeConverter(typeof(StringLikeTypeConverter<ChatId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public partial class ChatId : StringIdentifier, IStringIdentifier<ChatId>, IHasShardKey<string>, IMentionTarget
{
    public const char ThreadIdSeparator = '-';

    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ChatId>();
    private static readonly ILruCache<string, ChatId> Cache = CreateCache<ChatId>(512);

    public static readonly RandomStringGenerator IdGenerator = new(10, Alphabet.AlphaNumeric);

    [IgnoreDataMember]
    public ChatKind Kind { get; }
    [IgnoreDataMember]
    public bool IsSystem => Constants.Chat.SystemChatIds.Contains(this);
    [IgnoreDataMember]
    public ChatId RootChatId
        => this is PlaceChatId placeChatId ? placeChatId.RootChatId : this;
    [IgnoreDataMember]
    public MentionKind MentionKind => MentionKind.Chat;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember]
    public virtual string ShardKey => Value;

    // Factories and constructors

    protected ChatId(string value, ChatKind kind) : base(value)
        => Kind = kind;

    // Equality

    public bool Equals(ChatId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is ChatId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ChatId? left, ChatId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ChatId? left, ChatId? right)
        => !(left?.Equals(right) ?? right is null);

    // Parsing

    public static ChatId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ChatId>(s);

    public static ChatId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static ChatId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out ChatId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (s.Length < 6)
            return false;

        if (s.StartsWith(PeerChatId.IdPrefix)) {
            result = TryParsePeerChatId(s);
            return result != null;
        }
        if (s.StartsWith(PlaceChatId.IdPrefix)) {
            result = TryParsePlaceChatId(s);
            return result != null;
        }
        result = TryParseGroupChatId(s);
        if (result == null)
            return false;

        result = Cache.AddOrGet(s, result);
        return true;
    }

    // Threads

    public bool IsThread([NotNullWhen(true)]out ThreadChatId? threadChatId)
    {
        threadChatId = this as ThreadChatId;
        return threadChatId != null;
    }

    public ChatId GetThreadOutermostParentOrSelf()
        => this is ThreadChatId threadChatId ? threadChatId.GetOutermostParent() : this;

    public bool IsThread()
        => this is ThreadChatId;

    public ThreadChatId CreateThreadId(long threadId)
        => new (this, threadId);

    public void EnsureNonThread()
    {
        if (IsThread())
            throw StandardError.Constraint("ChatId should not belong to Thread");
    }

    // Private methods

    private static ChatId? TryParsePeerChatId(string s)
    {
        // Peer chat IDs may carry trailing "-<threadId>" segments (peer-chat threads).
        // Peer user IDs never end with "-<number>", so any such suffix is a thread ID:
        // try the plain "p-u1-u2" base first, then peel one numeric segment at a time.
        if (TryParsePeerChatIdBase(s) is { } peerChatId)
            return peerChatId;

        var span = s.AsSpan();
        List<long>? threadIds = null;
        while (true) {
            var threadIdIndex = span.LastIndexOf(ThreadIdSeparator);
            if (threadIdIndex < 0 || !long.TryParse(span[(threadIdIndex + 1)..], out var threadId) || threadId <= 0)
                return null;

            threadIds ??= new List<long>();
            threadIds.Insert(0, threadId);
            span = span[..threadIdIndex];
            if (span.Length < 6 || TryParsePeerChatIdBase(span.ToString()) is not { } baseChatId)
                continue;

            ChatId result = baseChatId;
            foreach (var id in threadIds)
                result = new ThreadChatId(result, id);

            return result;
        }
    }

    private static PeerChatId? TryParsePeerChatIdBase(string s)
    {
        var tail = s.AsSpan(2);
        var userId1Length = tail.IndexOf('-');
        if (userId1Length < 0)
            return null;

        var sUserId1 = tail[..userId1Length].ToString();
        var sUserId2 = tail[(userId1Length + 1)..].ToString();
        if (!UserId.TryParse(sUserId1, out var userId1)
            || !UserId.TryParse(sUserId2, out var userId2))
            return TryParsePeerChatIdWithUserIdException(s);
        if (string.CompareOrdinal(userId1.Value, userId2.Value) >= 0)
            return null; // Wrong sort order or they are the same

        return new PeerChatId(s, userId1, userId2);
    }

    private static PeerChatId? TryParsePeerChatIdWithUserIdException(string s)
    {
        var tail = s.AsSpan(2);
        foreach (var sUserId1 in UserId.UserIdExceptions) {
            if (tail.Length <= sUserId1.Length
                || tail[sUserId1.Length] != '-'
                || !tail.StartsWith(sUserId1))
                continue;

            var sUserId2 = tail[(sUserId1.Length + 1)..].ToString();
            if (!UserId.TryParse(sUserId1, out var userId1))
                continue;
            if (!UserId.TryParse(sUserId2, out var userId2))
                continue;
            if (string.CompareOrdinal(userId1.Value, userId2.Value) >= 0)
                return null; // Wrong sort order or they are the same

            return new PeerChatId(s, userId1, userId2);
        }

        return null;
    }

    private static ChatId? TryParsePlaceChatId(string s)
    {
        var tail = s.AsSpan(2);
        var placeIdLength = tail.IndexOf('-');
        if (placeIdLength < 0)
            return null;

        if (!PlaceId.TryParse(tail[..placeIdLength].ToString(), out var placeId))
            return null;
        if (!ParsedLocalChatId.TryParse(tail[(placeIdLength + 1)..], null, out var localChatId))
            return null;

        if (!localChatId.IsThread)
            return new PlaceChatId(PlaceChatId.Format(placeId, localChatId.Id), placeId, localChatId);

        var threadIds = new List<long>();
        while (localChatId.Parent is not null && localChatId.IsThread) {
            threadIds.Insert(0, localChatId.ThreadId);
            localChatId = localChatId.Parent;
        }
        ChatId result = new PlaceChatId(PlaceChatId.Format(placeId, localChatId.Id), placeId, localChatId);
        foreach (var threadId in threadIds)
            result = new ThreadChatId(result, threadId);
        return result;
    }

    private static ChatId? TryParseGroupChatId(string s)
    {
        if (!ParsedLocalChatId.TryParse(s, IsSpecialChatId, out var localChatId))
            return null;

        if (!localChatId.IsThread)
            return new GroupChatId(localChatId.Id);

        var threadIds = new List<long>();
        while (localChatId.Parent is not null && localChatId.IsThread) {
            threadIds.Insert(0, localChatId.ThreadId);
            localChatId = localChatId.Parent;
        }

        var result = (ChatId)new GroupChatId(localChatId.Id);
        foreach (var threadId in threadIds)
            result = new ThreadChatId(result, threadId);
        return result;

        static bool IsSpecialChatId(string s1)
            => s1 is "the-actual-one" or "feedback-template";
    }
}
