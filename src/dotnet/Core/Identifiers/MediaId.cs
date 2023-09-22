using System.ComponentModel;
using ActualChat.Internal;
using MemoryPack;
using Stl.Fusion.Blazor;
using Stl.Generators;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<MediaId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<MediaId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<MediaId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct MediaId : ISymbolIdentifier<MediaId>
{
    private const char Separator = ':';

    private static RandomStringGenerator IdGenerator { get; } = new(10, Alphabet.AlphaNumeric);

    public static MediaId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Scope { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string LocalId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public MediaId(Symbol id)
        => this = Parse(id);
    public MediaId(string? id)
        => this = Parse(id);
    public MediaId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);
    public MediaId(string scope, Generate _)
        => this = new MediaId($"{scope}{Separator}{IdGenerator.Next()}");

    public MediaId(Symbol id, string scope, string localId, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }

        Id = id;
        Scope = scope;
        LocalId = localId;
    }

    // Conversion
    public override string ToString() => Value;
    public static implicit operator Symbol(MediaId source) => source.Id;
    public static implicit operator string(MediaId source) => source.Id.Value;

    // Equality
    public bool Equals(MediaId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is MediaId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(MediaId left, MediaId right) => left.Equals(right);
    public static bool operator !=(MediaId left, MediaId right) => !left.Equals(right);

    // Parsing
    public static MediaId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<MediaId>(s);
    public static MediaId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<MediaId>(s).LogWarning(DefaultLog, None);

    public static bool TryParse(string? s, out MediaId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        var parts = s.Split(Separator);
        if (parts.Length != 2)
            return false;

        var scope = parts[0];
        if (!Alphabet.AlphaNumericDash.IsMatch(scope))
            return false;

        var localId = parts[1];
        if (!Alphabet.AlphaNumeric.IsMatch(localId))
            return false;

        result = new MediaId(s, scope, localId, AssumeValid.Option);
        return true;
    }
}
