using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Overrides Equals(object) but not GetHashCode()

/// <summary>
/// Identifies one call: the chat it is placed in plus a local id no other call to that chat shares.
/// The local id is opaque - only the server that issues it knows how it is made.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<CallId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<CallId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<CallId>))]
[TypeConverter(typeof(StringLikeTypeConverter<CallId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class CallId : StringIdentifier, IStringIdentifier<CallId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<CallId>();
    private static readonly ILruCache<string, CallId> Cache = CreateCache<CallId>(64, 256);

    public const char Delimiter = ':';

    [IgnoreDataMember]
    public ChatId ChatId { get; }
    [IgnoreDataMember]
    public string LocalId { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override ShardKey ShardKey => ChatId.ShardKey;

    // Factories and constructors

    public static CallId New(ChatId chatId, string localId)
    {
        ArgumentException.ThrowIfNullOrEmpty(localId);

        return new(Format(chatId, localId), chatId, localId);
    }

    private CallId(string value, ChatId chatId, string localId) : base(value)
    {
        ChatId = chatId;
        LocalId = localId;
    }

    // Equality

    public bool Equals(CallId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is CallId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(CallId? left, CallId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(CallId? left, CallId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(ChatId chatId, string localId)
        => $"{chatId.Value}{Delimiter}{localId}";

    public static CallId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<CallId>(s);

    public static CallId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static CallId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out CallId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var chatIdLength = s.IndexOf(Delimiter);
        if (chatIdLength < 0 || chatIdLength == s.Length - 1)
            return false;
        if (!ChatId.TryParse(s[..chatIdLength], out var chatId))
            return false;

        result = new CallId(s, chatId, s[(chatIdLength + 1)..]);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
