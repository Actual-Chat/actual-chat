using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Unique identifier for an explicit notification sent to a user.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<ExplicitNotificationId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<ExplicitNotificationId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<ExplicitNotificationId>))]
[TypeConverter(typeof(StringLikeTypeConverter<ExplicitNotificationId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class ExplicitNotificationId : StringIdentifier, IStringIdentifier<ExplicitNotificationId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ExplicitNotificationId>();

    public const char Delimiter = ':';

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId UserId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ExplicitNotificationKind Kind { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string SimilarityKey { get; }

    public static ExplicitNotificationId New(UserId userId, ExplicitNotificationKind kind, string similarityKey)
        => new(Format(userId, kind, similarityKey), userId, kind, similarityKey);

    private ExplicitNotificationId(string id, UserId userId, ExplicitNotificationKind kind, string similarityKey)
        : base(id)
    {
        UserId = userId;
        Kind = kind;
        SimilarityKey = similarityKey;
    }

    // Equality

    public bool Equals(ExplicitNotificationId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is ExplicitNotificationId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ExplicitNotificationId? left, ExplicitNotificationId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ExplicitNotificationId? left, ExplicitNotificationId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    private static string Format(UserId userId, ExplicitNotificationKind kind, Symbol similarityKey)
        => $"{userId} {kind.Format()}{Delimiter}{similarityKey.Value}";

    public static ExplicitNotificationId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ExplicitNotificationId>(s);

    public static ExplicitNotificationId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static ExplicitNotificationId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s,  [NotNullWhen(true)] out ExplicitNotificationId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        var userIdLength = s.IndexOf(' ');
        if (userIdLength < 0)
            return false;
        if (!UserId.TryParse(s[..userIdLength], out var userId))
            return false;

        var kindStart = userIdLength + 1;
        var kindLength = s.IndexOf(Delimiter, kindStart);
        if (kindLength < 0)
            return false;

        var sKind = s.AsSpan(kindStart, kindLength - kindStart);
        if (!NumberExt.TryParsePositiveInt(sKind, out var kind))
            return false;
        if (kind is < 1 or >= (int)NotificationKind.Invalid)
            return false;

        var similarityKey = (Symbol)s[(kindLength + 1)..];
        result = new ExplicitNotificationId(s, userId, (ExplicitNotificationKind)kind, similarityKey);
        return true;
    }
}
