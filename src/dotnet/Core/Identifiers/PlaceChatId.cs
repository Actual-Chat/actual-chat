using System.ComponentModel;
using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Identifiers.Internal;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<PlaceChatId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<PlaceChatId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<PlaceChatId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct PlaceChatId : ISymbolIdentifier<PlaceChatId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= DefaultLogFor<PlaceChatId>();

    public static readonly string IdPrefix = "s-";
    public static PlaceChatId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Parsed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public PlaceId PlaceId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol LocalChatId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsRoot => !IsNone && PlaceId.Id == LocalChatId;

    public static PlaceChatId Root(PlaceId placeId)
        => new(Format(placeId, placeId.Id), placeId, placeId.Id, AssumeValid.Option);

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public PlaceChatId(Symbol id) => this = Parse(id);
    public PlaceChatId(string? id) => this = Parse(id);
    public PlaceChatId(string? id, ParseOrNone _) => ParseOrNone(id);
    public PlaceChatId(PlaceId placeId, Generate _)
    {
        if (placeId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(placeId));
        var localChatId = new ChatId(Generate.Option).Id;
        this = new PlaceChatId(Format(placeId, localChatId), placeId, localChatId, AssumeValid.Option);
    }

    private PlaceChatId(Symbol id, PlaceId placeId, Symbol localChatId, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        PlaceId = placeId;
        LocalChatId = localChatId;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(PlaceChatId source) => source.Id;
    public static implicit operator string(PlaceChatId source) => source.Id.Value;

    // Equality

    public bool Equals(PlaceChatId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is PlaceChatId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(PlaceChatId left, PlaceChatId right) => left.Equals(right);
    public static bool operator !=(PlaceChatId left, PlaceChatId right) => !left.Equals(right);

    // Parsing

    public static string Format(PlaceId placeId, Symbol localChatId)
        => placeId.IsNone ? "" : $"{IdPrefix}{placeId}-{localChatId}";

    public static PlaceChatId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<PlaceChatId>(s);
    public static PlaceChatId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<PlaceChatId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out PlaceChatId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        if (!s.OrdinalStartsWith(IdPrefix))
            return false;

        var tail = s.AsSpan(2);
        var placeIdLength = tail.IndexOf('-');
        if (placeIdLength < 0)
            return false;

        if (!PlaceId.TryParse(tail[..placeIdLength].ToString(), out var placeId))
            return false;
        if (!ChatId.TryParse(tail[(placeIdLength + 1)..].ToString(), out var localChatId))
            return false;
        if (placeId.IsNone || localChatId.IsNone || localChatId.Kind != ChatKind.Group)
            return false; // Both PlaceId and local ChatId must be there

        result = new PlaceChatId((Symbol)s, placeId, localChatId, AssumeValid.Option);
        return true;
    }
}
