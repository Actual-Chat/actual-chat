using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract]
[JsonConverter(typeof(StringIdentifierJsonConverter<UserDeviceId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<UserDeviceId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<UserDeviceId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class UserDeviceId2(string value, UserId2 ownerId, string deviceId, AssumeValid _)
    : StringIdentifier(value), IStringIdentifier<UserDeviceId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<UserDeviceId2>();
    private static readonly ILruCache<string, UserDeviceId2> Cache = CreateCache<UserDeviceId2>(256);
    private const char Delimiter = ':';

    [IgnoreDataMember]
    public UserId2 OwnerId { get; } = ownerId;
    [IgnoreDataMember]
    public string DeviceId { get; } = deviceId;

    // Factories

    public UserDeviceId2 New(UserId2 ownerId, string deviceId)
        => new(Format(ownerId, deviceId), ownerId, deviceId, AssumeValid.Option);

    // Equality

    public bool Equals(UserDeviceId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is UserDeviceId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(UserDeviceId2? left, UserDeviceId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(UserDeviceId2? left, UserDeviceId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(UserId2 ownerId, string deviceId)
        => $"{ownerId}{Delimiter}{deviceId}";

    public static UserDeviceId2 Parse(string s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<UserDeviceId2>(s);

    public static bool TryParse(string? s, [NotNullWhen(true)] out UserDeviceId2? result)
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

        if (!UserId2.TryParse(parts[0], out var ownerId))
            return false;

        result = new UserDeviceId2(s, ownerId, parts[1], AssumeValid.Option);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
