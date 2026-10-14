using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Unique identifier for a user notification.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<NotificationId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<NotificationId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<NotificationId>))]
[TypeConverter(typeof(StringLikeTypeConverter<NotificationId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class NotificationId : StringIdentifier, IStringIdentifier<NotificationId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<NotificationId>();
    private static readonly ILruCache<string, NotificationId> Cache = CreateCache<NotificationId>(64, 256);

    private readonly string _short;

    [IgnoreDataMember]
    public UserId UserId { get; }
    [IgnoreDataMember]
    public NotificationKind Kind { get; }
    [IgnoreDataMember]
    public string SimilarityKey { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override ShardKey ShardKey => UserId.ShardKey;

    // Factories and constructors

    public static NotificationId New(UserId userId, NotificationKind kind, string similarityKey)
        => new(Format(userId, kind, similarityKey), userId, kind, similarityKey);

    private NotificationId(string value, UserId userId, NotificationKind kind, string similarityKey) : base(value)
    {
        UserId = userId;
        Kind = kind;
        SimilarityKey = similarityKey;
        _short = value[(value.IndexOf(' ') + 1)..];
    }

    public string ToShort()
        // The id without the user id: "<kind>:<similarityKey>"
        => _short;

    // Equality

    public bool Equals(NotificationId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is NotificationId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(NotificationId? left, NotificationId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(NotificationId? left, NotificationId? right)
        => !(left?.Equals(right) ?? right is null);

    // Parsing

    private static string Format(UserId userId, NotificationKind kind, Symbol similarityKey)
        => $"{userId} {kind.Format()}:{similarityKey.Value}";

    public static NotificationId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<NotificationId>(s);

    public static NotificationId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static NotificationId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out NotificationId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var userIdLength = s.IndexOf(' ');
        if (userIdLength < 0)
            return false;
        if (!UserId.TryParse(s[..userIdLength], out var userId))
            return false;

        if (!TryParseShort(s[(userIdLength + 1)..], out var kind, out var similarityKey))
            return false;

        result = new NotificationId(s, userId, kind, similarityKey);
        result = Cache.AddOrGet(s, result);
        return true;
    }

    public static NotificationId ParseShort(UserId userId, string? s)
        => TryParseShort(userId, s, out var result) ? result : throw StandardError.Format<NotificationId>(s);

    public static NotificationId? TryParseShort(UserId userId, string? s)
        => TryParseShort(userId, s, out var result) ? result : null;

    public static bool TryParseShort(UserId userId, string? s, [NotNullWhen(true)] out NotificationId? result)
    {
        result = null;
        return !s.IsNullOrEmpty() && TryParse($"{userId} {s}", out result);
    }

    private static bool TryParseShort(string? s, out NotificationKind kind, out string similarityKey)
    {
        kind = default;
        similarityKey = "";
        if (s.IsNullOrEmpty())
            return false;

        var kindLength = s.IndexOf(':');
        if (kindLength < 0)
            return false;

        if (!NumberExt.TryParsePositiveInt(s.AsSpan(0, kindLength), out var iKind))
            return false;

        if (iKind is < 1 or >= (int)NotificationKind.Invalid)
            return false;

        kind = (NotificationKind)iKind;
        similarityKey = s[(kindLength + 1)..];
        return true;
    }
}
