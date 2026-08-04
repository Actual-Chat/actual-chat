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

    [IgnoreDataMember]
    public UserId UserId { get; }
    [IgnoreDataMember]
    public NotificationKind Kind { get; }
    [IgnoreDataMember]
    public string SimilarityKey { get; }

    // Factories and constructors

    public static NotificationId New(UserId userId, NotificationKind kind, string similarityKey)
        => new(Format(userId, kind, similarityKey), userId, kind, similarityKey);

    private NotificationId(string value, UserId userId, NotificationKind kind, string similarityKey) : base(value)
    {
        UserId = userId;
        Kind = kind;
        SimilarityKey = similarityKey;
    }

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

        var kindStart = userIdLength + 1;
        var kindLength = s.IndexOf(':', kindStart);
        if (kindLength < 0)
            return false;

        var sKind = s.AsSpan(kindStart, kindLength - kindStart);
        if (!NumberExt.TryParsePositiveInt(sKind, out var kind))
            return false;

        if (kind is < 1 or >= (int)NotificationKind.Invalid)
            return false;

        var similarityKey = (Symbol)s[(kindLength + 1)..];
        result = new NotificationId(s, userId, (NotificationKind)kind, similarityKey);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
