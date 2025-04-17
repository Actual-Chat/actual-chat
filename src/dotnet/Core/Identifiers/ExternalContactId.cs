using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<ExternalContactId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<ExternalContactId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<ExternalContactId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<ExternalContactId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class ExternalContactId : StringIdentifier, IStringIdentifier<ExternalContactId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ExternalContactId>();
    private static readonly ILruCache<string, ExternalContactId> Cache = CreateCache<ExternalContactId>(256);

    public const char Delimiter = UserDeviceId.Delimiter;

    [IgnoreDataMember]
    public UserDeviceId UserDeviceId { get; }
    [IgnoreDataMember]
    public Symbol DeviceContactId { get; }

    // Factories and constructors

    public static ExternalContactId New(UserDeviceId userDeviceId, Symbol deviceContactId)
        => new(Format(userDeviceId, deviceContactId), userDeviceId, deviceContactId);

    private ExternalContactId(string value, UserDeviceId userDeviceId, Symbol deviceContactId) : base(value)
    {
        UserDeviceId = userDeviceId;
        DeviceContactId = deviceContactId;
    }

    // Equality

    public bool Equals(ExternalContactId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is ExternalContactId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ExternalContactId? left, ExternalContactId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ExternalContactId? left, ExternalContactId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    private static string Format(UserDeviceId userDeviceId, Symbol deviceContactId)
        => $"{userDeviceId.Value}{Delimiter}{deviceContactId}";

    public static ExternalContactId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ExternalContactId>(s);

    public static ExternalContactId? ParseOrNull(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static ExternalContactId? TryParse(string? s)
        => TryParse(s, out var result) ? result : null;

    public static bool TryParse(string? s, [NotNullWhen(true)] out ExternalContactId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var delimIndex = s.LastIndexOf(Delimiter);
        if (delimIndex < 0 || delimIndex >= s.Length - 1)
            return false;

        if (!UserDeviceId.TryParse(s[..delimIndex], out var userDeviceId))
            return false;
        var deviceContactId = new Symbol(s[(delimIndex + 1)..]);

        result = new ExternalContactId(s, userDeviceId, deviceContactId);
        result = Cache.AddOrGet(s, result);
        return true;
    }

    // Helpers

    public static string GetFormatPrefix(UserId2 ownerId)
        => $"{ownerId.Value}{UserDeviceId.Delimiter}";
    public static string GetFormatPrefix(UserDeviceId userDeviceId)
        => $"{userDeviceId.Value}{Delimiter}";
}
