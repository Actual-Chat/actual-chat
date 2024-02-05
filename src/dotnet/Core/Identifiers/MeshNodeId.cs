using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;
using MemoryPack;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<MeshNodeId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<MeshNodeId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<MeshNodeId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct MeshNodeId : ISymbolIdentifier<MeshNodeId>
{
    private static RandomStringGenerator IdGenerator => Alphabet.AlphaNumeric.Generator8;

    public static MeshNodeId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public MeshNodeId(Symbol id)
        => this = Parse(id);
    public MeshNodeId(string? id)
        => this = Parse(id);
    public MeshNodeId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);
    public MeshNodeId(Generate _)
        => this = new MeshNodeId(IdGenerator.Next(), AssumeValid.Option);

    public MeshNodeId(Symbol id, AssumeValid _)
        => Id = id;

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(MeshNodeId source) => source.Id;
    public static implicit operator string(MeshNodeId source) => source.Id.Value;

    // Equality

    public bool Equals(MeshNodeId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is MeshNodeId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(MeshNodeId left, MeshNodeId right) => left.Equals(right);
    public static bool operator !=(MeshNodeId left, MeshNodeId right) => !left.Equals(right);

    // Parsing

    public static MeshNodeId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<MeshNodeId>(s);
    public static MeshNodeId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<MeshNodeId>(s).LogWarning(DefaultLog, None);

    public static bool TryParse(string? s, out MeshNodeId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        if (s.Length < 8 || !Alphabet.AlphaNumeric.IsMatch(s))
            return false;

        result = new MeshNodeId(s, AssumeValid.Option);
        return true;
    }
}
