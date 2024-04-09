using System.ComponentModel;
using ActualChat.Internal;
using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<ExternalContactId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<ExternalContactId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<ExternalContactId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct ExternalContactId : ISymbolIdentifier<ExternalContactId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= DefaultLogFor<ExternalContactId>();
    private const char Delimiter = ':';

    public static ExternalContactId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UserDeviceId UserDeviceId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol DeviceContactId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public ExternalContactId(Symbol id)
        => this = Parse(id);
    public ExternalContactId(UserDeviceId userDeviceId, Symbol deviceContactId)
        => this = Parse(Format(userDeviceId, deviceContactId));
    public ExternalContactId(UserDeviceId userDeviceId, Symbol deviceContactId, ParseOrNone _)
        => this = ParseOrNone(Format(userDeviceId, deviceContactId));
    public ExternalContactId(string id)
        => this = Parse(id);
    public ExternalContactId(string id, ParseOrNone _)
        => this = ParseOrNone(id);

    public ExternalContactId(Symbol id, UserDeviceId userDeviceId, Symbol deviceContactId, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        UserDeviceId = userDeviceId;
        DeviceContactId = deviceContactId;
    }

    public ExternalContactId(UserDeviceId userDeviceId, Symbol deviceContactId, AssumeValid _)
    {
        if (userDeviceId.IsNone || deviceContactId.IsEmpty) {
            this = None;
            return;
        }
        Id = Format(userDeviceId, deviceContactId);
        UserDeviceId = userDeviceId;
        DeviceContactId = deviceContactId;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(ExternalContactId source) => source.Id;
    public static implicit operator string(ExternalContactId source) => source.Id.Value;

    // Equality

    public bool Equals(ExternalContactId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is ExternalContactId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ExternalContactId left, ExternalContactId right) => left.Equals(right);
    public static bool operator !=(ExternalContactId left, ExternalContactId right) => !left.Equals(right);

    // Parsing

    private static string Format(UserDeviceId userDeviceId, Symbol deviceContactId)
        => userDeviceId.IsNone || deviceContactId.IsEmpty ? "" : $"{userDeviceId}{Delimiter}{deviceContactId}";

    public static ExternalContactId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ExternalContactId>(s);
    public static ExternalContactId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<ExternalContactId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out ExternalContactId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        var delimIndex = s.LastIndexOf(Delimiter);
        if (delimIndex < 0 || delimIndex >= s.Length - 1)
            return false;

        if (!UserDeviceId.TryParse(s[..delimIndex], out var userDeviceId))
            return false;
        var deviceContactId = new Symbol(s[(delimIndex + 1)..]);

        result = new ExternalContactId(s,
            userDeviceId,
            new Symbol(deviceContactId),
            AssumeValid.Option);
        return true;
    }

    public static string Prefix(UserId ownerId)
        => UserDeviceId.Prefix(ownerId);
    public static string Prefix(UserDeviceId userDeviceId)
        => $"{userDeviceId}{Delimiter}";
}
