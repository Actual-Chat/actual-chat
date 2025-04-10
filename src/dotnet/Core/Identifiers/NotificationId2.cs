using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract]
[JsonConverter(typeof(StringIdentifierJsonConverter<NotificationId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<NotificationId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<NotificationId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class NotificationId2 : StringIdentifier, IStringIdentifier<NotificationId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<NotificationId2>();
    private static readonly ILruCache<string, NotificationId2> Cache = CreateCache<NotificationId2>(64, 256);

    [IgnoreDataMember]
    public UserId UserId { get; }
    [IgnoreDataMember]
    public NotificationKind Kind { get; }
    [IgnoreDataMember]
    public Symbol SimilarityKey { get; }

    // Factories and constructors

    public static NotificationId2 New(UserId userId, NotificationKind kind, Symbol similarityKey)
        => new(Format(userId, kind, similarityKey), userId, kind, similarityKey);

    private NotificationId2(string value, UserId userId, NotificationKind kind, Symbol similarityKey) : base(value)
    {
        UserId = userId;
        Kind = kind;
        SimilarityKey = similarityKey;
    }

    // Equality

    public bool Equals(NotificationId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is NotificationId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(NotificationId2? left, NotificationId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(NotificationId2? left, NotificationId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Parsing

    private static string Format(UserId userId, NotificationKind kind, Symbol similarityKey)
        => userId.IsNone ? "" : $"{userId} {kind.Format()}:{similarityKey.Value}";

    public static NotificationId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<NotificationId2>(s);

    public static bool TryParse(string? s, [NotNullWhen(true)] out NotificationId2? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var userIdLength = s.OrdinalIndexOf(" ");
        if (userIdLength < 0)
            return false;
        if (!UserId.TryParse(s[..userIdLength], out var userId))
            return false;

        var kindStart = userIdLength + 1;
        var kindLength = s.OrdinalIndexOf(":", kindStart);
        if (kindLength < 0)
            return false;

        var sKind = s.AsSpan(kindStart, kindLength - kindStart);
        if (!NumberExt.TryParsePositiveInt(sKind, out var kind))
            return false;

        if (kind is < 1 or >= (int)NotificationKind.Invalid)
            return false;

        var similarityKey = (Symbol)s[(kindLength + 1)..];
        result = new NotificationId2(s, userId, (NotificationKind)kind, similarityKey);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
