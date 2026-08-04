using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Unique identifier for a permission role within a chat.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<RoleId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<RoleId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<RoleId>))]
[TypeConverter(typeof(StringLikeTypeConverter<RoleId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class RoleId : StringIdentifier, IStringIdentifier<RoleId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<RoleId>();
    private static readonly ILruCache<string, RoleId> Cache = CreateCache<RoleId>(128);

    public const char Delimiter = ':';

    [IgnoreDataMember]
    public ChatId ChatId { get; }
    [IgnoreDataMember]
    public long LocalId { get; }

    // Factories and constructors

    public static RoleId New(ChatId chatId, long localId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(localId);
        return new(Format(chatId, localId), chatId, localId);
    }

    private RoleId(string value, ChatId chatId, long localId) : base(value)
    {
        ChatId = chatId;
        LocalId = localId;
    }

    // Equality

    public bool Equals(RoleId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is RoleId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(RoleId? left, RoleId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(RoleId? left, RoleId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(ChatId chatId, long localId)
        => $"{chatId.Value}{Delimiter}{localId.Format()}";

    public static RoleId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<RoleId>(s);

    public static RoleId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static RoleId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out RoleId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var chatIdLength = s.IndexOf(Delimiter);
        if (chatIdLength < 0)
            return false;

        if (!ChatId.TryParse(s[..chatIdLength], out var chatId))
            return false;
        if (!NumberExt.TryParsePositiveLong(s.AsSpan(chatIdLength + 1), out var localId))
            return false;

        result = new RoleId(s, chatId, localId);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
