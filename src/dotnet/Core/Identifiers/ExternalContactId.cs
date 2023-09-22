using System.ComponentModel;
using ActualChat.Internal;
using MemoryPack;
using Stl.Fusion.Blazor;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<ExternalContactId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<ExternalContactId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<ExternalContactId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct ExternalContactId : ISymbolIdentifier<ExternalContactId>
{
    private const char Delimiter = ':';
    public static ExternalContactId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UserId OwnerId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol DeviceId { get; }
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
    public ExternalContactId(UserId ownerId, Symbol deviceId, Symbol deviceContactId)
        => this = Parse(Format(ownerId, deviceId, deviceContactId));
    public ExternalContactId(UserId ownerId, Symbol deviceId, Symbol deviceContactId, ParseOrNone _)
        => this = ParseOrNone(Format(ownerId, deviceId, deviceContactId));
    public ExternalContactId(string id)
        => this = Parse(id);
    public ExternalContactId(string id, ParseOrNone _)
        => this = ParseOrNone(id);

    public ExternalContactId(Symbol id, UserId ownerId, Symbol deviceId, Symbol deviceContactId, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        OwnerId = ownerId;
        DeviceId = deviceId;
        DeviceContactId = deviceContactId;
    }

    public ExternalContactId(UserId ownerId, Symbol deviceId, Symbol deviceContactId, AssumeValid _)
    {
        if (ownerId.IsNone || deviceId.IsEmpty || deviceContactId.IsEmpty) {
            this = None;
            return;
        }
        Id = Format(ownerId, deviceId, deviceContactId);
        OwnerId = ownerId;
        DeviceId = deviceId;
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

    private static string Format(UserId ownerId, Symbol deviceId, Symbol deviceContactId)
        => ownerId.IsNone || deviceId.IsEmpty || deviceContactId.IsEmpty ? "" : $"{ownerId}{Delimiter}{deviceId}{Delimiter}{deviceContactId}";

    public static ExternalContactId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ExternalContactId>(s);
    public static ExternalContactId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<ExternalContactId>(s).LogWarning(DefaultLog, None);

    public static bool TryParse(string? s, out ExternalContactId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        var parts = s.Split(Delimiter, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return false;

        if (!UserId.TryParse(parts[0], out var ownerId))
            return false;

        result = new ExternalContactId(s,
            ownerId,
            new Symbol(parts[1]),
            new Symbol(parts[2]),
            AssumeValid.Option);
        return true;
    }

    public static string Prefix(UserId ownerId)
        => $"{ownerId}{Delimiter}";
    public static string Prefix(UserId ownerId, Symbol deviceId)
        => $"{ownerId}{Delimiter}{deviceId}{Delimiter}";
}
