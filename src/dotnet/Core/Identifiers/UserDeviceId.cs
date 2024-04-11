using System.ComponentModel;
using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Identifiers.Internal;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<UserDeviceId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<UserDeviceId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<UserDeviceId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct UserDeviceId : ISymbolIdentifier<UserDeviceId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= DefaultLogFor<UserDeviceId>();
    private const char Delimiter = ':';

    public static UserDeviceId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UserId OwnerId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol DeviceId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public UserDeviceId(Symbol id)
        => this = Parse(id);
    public UserDeviceId(UserId ownerId, Symbol deviceId)
        => this = Parse(Format(ownerId, deviceId));
    public UserDeviceId(UserId ownerId, Symbol deviceId, ParseOrNone _)
        => this = ParseOrNone(Format(ownerId, deviceId));
    public UserDeviceId(string id)
        => this = Parse(id);
    public UserDeviceId(string id, ParseOrNone _)
        => this = ParseOrNone(id);

    public UserDeviceId(Symbol id, UserId ownerId, Symbol deviceId, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        OwnerId = ownerId;
        DeviceId = deviceId;
    }

    public UserDeviceId(UserId ownerId, Symbol deviceId, AssumeValid _)
    {
        if (ownerId.IsNone || deviceId.IsEmpty) {
            this = None;
            return;
        }
        Id = Format(ownerId, deviceId);
        OwnerId = ownerId;
        DeviceId = deviceId;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(UserDeviceId source) => source.Id;
    public static implicit operator string(UserDeviceId source) => source.Id.Value;

    // Equality

    public bool Equals(UserDeviceId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is UserDeviceId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(UserDeviceId left, UserDeviceId right) => left.Equals(right);
    public static bool operator !=(UserDeviceId left, UserDeviceId right) => !left.Equals(right);

    // Parsing

    private static string Format(UserId ownerId, Symbol deviceId)
        => ownerId.IsNone || deviceId.IsEmpty ? "" : $"{ownerId}{Delimiter}{deviceId}";

    public static UserDeviceId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<UserDeviceId>(s);
    public static UserDeviceId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<UserDeviceId>(s).LogWarning<UserDeviceId>(Log, None);

    public static bool TryParse(string? s, out UserDeviceId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        var parts = s.Split(Delimiter, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;

        if (!UserId.TryParse(parts[0], out var ownerId))
            return false;

        result = new UserDeviceId(s,
            ownerId,
            new Symbol(parts[1]),
            AssumeValid.Option);
        return true;
    }

    public static string Prefix(UserId ownerId)
        => $"{ownerId}{Delimiter}";
}
