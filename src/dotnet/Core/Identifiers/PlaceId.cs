using System.ComponentModel;
using ActualChat.Internal;
using MemoryPack;
using Stl.Fusion.Blazor;
using Stl.Generators;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<PlaceId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<PlaceId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<PlaceId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct PlaceId : ISymbolIdentifier<PlaceId>
{
    public static PlaceId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public PlaceId(Symbol id)
        => this = Parse(id);
    public PlaceId(string? id)
        => this = Parse(id);
    public PlaceId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);
    public PlaceId(Generate _)
        => this = new PlaceId(new ChatId(Generate.Option).Id);

    public PlaceId(Symbol id, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(PlaceId source) => source.Id;
    public static implicit operator string(PlaceId source) => source.Id.Value;

    // public static PlaceId From(ChatId chatId)
    //     => new (chatId.Id);

    // Equality

    public bool Equals(PlaceId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is PlaceId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(PlaceId left, PlaceId right) => left.Equals(right);
    public static bool operator !=(PlaceId left, PlaceId right) => !left.Equals(right);

    // Parsing

    public static PlaceId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<PlaceId>(s);
    public static PlaceId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<PlaceId>(s).LogWarning(DefaultLog, None);

    public static bool TryParse(string? s, out PlaceId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        if (s.Length < 10)
            return false;

        if (!(Alphabet.AlphaNumeric.IsMatch(s) || Constants.Chat.SystemChatIds.Contains(s)))
            return false;

        // Group chat ID
        result = new PlaceId(s, AssumeValid.Option);
        return true;
    }
}
