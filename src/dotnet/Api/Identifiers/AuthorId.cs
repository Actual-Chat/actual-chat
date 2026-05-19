using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Unique identifier for a chat author within a specific chat.
/// </summary>
[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringLikeJsonConverter<AuthorId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<AuthorId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<AuthorId>))]
[TypeConverter(typeof(StringLikeTypeConverter<AuthorId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class AuthorId : PrincipalId, IStringIdentifier<AuthorId>, IMentionTarget
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<AuthorId>();
    private static readonly ILruCache<string, AuthorId> Cache = CreateCache<AuthorId>(512);

    [IgnoreDataMember]
    public ChatId ChatId { get; }
    [IgnoreDataMember]
    public long LocalId { get; }
    [IgnoreDataMember]
    public override string ShardKey => ChatId.Value;
    [IgnoreDataMember]
    public MentionKind MentionKind => MentionKind.Author;

    // Factories and constructors

    public static AuthorId New(ChatId chatId, long localId)
        => new(Format(chatId, localId), chatId, localId);

    private AuthorId(string value, ChatId chatId, long localId) : base(value, PrincipalKind.Author)
    {
        ChatId = chatId;
        LocalId = localId;
    }

    // Equality

    public bool Equals(AuthorId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is AuthorId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(AuthorId? left, AuthorId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(AuthorId? left, AuthorId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(ChatId chatId, long localId)
        => $"{chatId.Value}:{localId.Format()}";

    public static new AuthorId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<AuthorId>(s);

    public static new AuthorId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static new AuthorId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out AuthorId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var chatIdLength = s.IndexOf(':');
        if (chatIdLength == -1)
            return false;

        if (!ChatId.TryParse(s[..chatIdLength], out var chatId))
            return false;

        var tail = s[(chatIdLength + 1)..];
        if (!NumberExt.TryParseLong(tail, out var localId))
            return false;

        result = new AuthorId(s, chatId, localId);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
