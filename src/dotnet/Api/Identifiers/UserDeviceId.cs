using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Unique identifier for a specific user device, combining user and device IDs.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<UserDeviceId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<UserDeviceId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<UserDeviceId>))]
[TypeConverter(typeof(StringLikeTypeConverter<UserDeviceId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class UserDeviceId : StringIdentifier, IStringIdentifier<UserDeviceId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<UserDeviceId>();
    private static readonly ILruCache<string, UserDeviceId> Cache = CreateCache<UserDeviceId>(16, 256);

    public const char Delimiter = ':';

    [IgnoreDataMember]
    public UserId OwnerId { get; }
    [IgnoreDataMember]
    public string DeviceId { get; }

    // Factories and constructors

    public static UserDeviceId New(UserId ownerId, string deviceId)
        => new(Format(ownerId, deviceId), ownerId, deviceId);

    private UserDeviceId(string value, UserId ownerId, string deviceId)
        : base(value)
    {
        OwnerId = ownerId;
        DeviceId = deviceId;
    }

    // Equality

    public bool Equals(UserDeviceId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is UserDeviceId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(UserDeviceId? left, UserDeviceId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(UserDeviceId? left, UserDeviceId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(UserId ownerId, string deviceId)
        => $"{ownerId}{Delimiter}{deviceId}";

    public static UserDeviceId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<UserDeviceId>(s);

    public static UserDeviceId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static UserDeviceId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out UserDeviceId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var parts = s.Split(Delimiter, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;

        if (!UserId.TryParse(parts[0], out var ownerId))
            return false;

        result = new UserDeviceId(s, ownerId, parts[1]);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
