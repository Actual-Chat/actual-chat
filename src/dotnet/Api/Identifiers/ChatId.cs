using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<ChatId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<ChatId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<ChatId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<ChatId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public partial class ChatId : StringIdentifier, IStringIdentifier<ChatId>, IHasShardKey<string>
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
    public virtual string ShardKey => Value;

    public ChatId GetRootChatId()
        => this is PlaceChatId placeChatId
            ? placeChatId.RootChatId
            : this;

    // Factories and constructors

    protected ChatId(string value, ChatKind kind) : base(value)
        => Kind = kind;

    // Equality

    public bool Equals(ChatId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
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

        if (s.OrdinalStartsWith(PeerChatId.IdPrefix)) {
            result = TryParsePeerChatId(s);
            return result != null;
        }
        if (s.OrdinalStartsWith(PlaceChatId.IdPrefix)) {
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

    public bool IsThread()
        => this is ThreadChatId;

    public ThreadChatId CreateThreadId(long threadId)
    {
        if (Kind is not (ChatKind.Group or ChatKind.Place))
            throw StandardError.NotSupported($"{Kind} chats do not support threads");

        return new ThreadChatId(this, threadId);
    }

    public void EnsureNonThread()
    {
        if (IsThread())
            throw StandardError.Constraint("ChatId should not belong to Thread");
    }

    // Private methods

    private static PeerChatId? TryParsePeerChatId(string s)
    {
        var tail = s.AsSpan(2);
        var userId1Length = tail.IndexOf('-');
        if (userId1Length < 0)
            return null;

        if (!UserId.TryParse(tail[..userId1Length].ToString(), out var userId1))
            return null;
        if (!UserId.TryParse(tail[(userId1Length + 1)..].ToString(), out var userId2))
            return null;
        if (string.CompareOrdinal(userId1.Value, userId2.Value) >= 0)
            return null; // Wrong sort order or they are the same

        return new PeerChatId(s, userId1, userId2);
    }

    private static ChatId? TryParsePlaceChatId(string s)
    {
        var tail = s.AsSpan(2);
        var placeIdLength = tail.IndexOf('-');
        if (placeIdLength < 0)
            return null;

        if (!PlaceId.TryParse(tail[..placeIdLength].ToString(), out var placeId))
            return null;
        if (!LocalChatId.TryParse(tail[(placeIdLength + 1)..], null, out var localChatId))
            return null;

        var threadIds = new List<long>();
        while (localChatId.Parent is not null && localChatId.IsTread) {
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
        if (!LocalChatId.TryParse(s, SpecialChatId, out var localChatId))
            return null;

        var threadIds = new List<long>();
        while (localChatId.Parent is not null && localChatId.IsTread) {
            threadIds.Insert(0, localChatId.ThreadId);
            localChatId = localChatId.Parent;
        }

        ChatId result = new GroupChatId(localChatId.Id, localChatId);
        foreach (var threadId in threadIds)
            result = new ThreadChatId(result, threadId);
        return result;

        static bool SpecialChatId(string s1)
        {
            return OrdinalEquals(s1, "the-actual-one") || OrdinalEquals(s1, "feedback-template");
        }
    }
}
