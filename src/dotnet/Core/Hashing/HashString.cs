using System.ComponentModel;
using ActualLab.Fusion.Blazor;
using ActualLab.Identifiers.Internal;
using MemoryPack;

namespace ActualChat.Hashing;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<HashString>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<HashString>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<HashString>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct HashString : ISymbolIdentifier<HashString>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= DefaultLogFor<HashString>();
    private const char Delimiter = ' ';

    public static HashString None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public HashAlgorithm Algorithm { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public HashEncoding Encoding { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol Hash { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public HashString(Symbol id)
        => this = Parse(id);
    public HashString(HashAlgorithm algorithm, HashEncoding encoding, Symbol hash)
        => this = Parse(Format(algorithm, encoding, hash));
    public HashString(HashAlgorithm algorithm, HashEncoding encoding, Symbol hash, ParseOrNone _)
        => this = ParseOrNone(Format(algorithm, encoding, hash));
    public HashString(string id)
        => this = Parse(id);
    public HashString(string id, ParseOrNone _)
        => this = ParseOrNone(id);

    public HashString(Symbol id, HashAlgorithm algorithm, HashEncoding encoding, Symbol hash, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        Algorithm = algorithm;
        Encoding = encoding;
        Hash = hash;
    }

    public HashString(HashAlgorithm algorithm, HashEncoding encoding, Symbol hash, AssumeValid _)
    {
        if (hash.IsEmpty) {
            this = None;
            return;
        }
        Id = Format(algorithm, encoding, hash);
        Algorithm = algorithm;
        Encoding = encoding;
        Hash = hash;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(HashString source) => source.Id;
    public static implicit operator string(HashString source) => source.Id.Value;

    // Equality

    public bool Equals(HashString other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is HashString other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(HashString left, HashString right) => left.Equals(right);
    public static bool operator !=(HashString left, HashString right) => !left.Equals(right);

    // Parsing

    private static string Format(HashAlgorithm algorithm, HashEncoding encoding, Symbol hash)
        => hash.IsEmpty ? "" : $"{algorithm:D}{Delimiter}{encoding:D}{Delimiter}{hash}";

    public static HashString Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<HashString>(s);
    public static HashString ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<HashString>(s).LogWarning<HashString>(Log, None);

    public static bool TryParse(string? s, out HashString result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        var algoEndsAt = s.OrdinalIndexOf(Delimiter);
        if (algoEndsAt < 0)
            return false;

        if (!Enum.TryParse<HashAlgorithm>(s[..algoEndsAt], out var algorithm))
            return false;

        var encodingStartsAt = algoEndsAt + 1;
        var encodingEndsAt = s.IndexOf(Delimiter, encodingStartsAt);
        if (!Enum.TryParse<HashEncoding>(s[encodingStartsAt..encodingEndsAt], out var encoding))
            return false;

        var hashStartsAt = encodingStartsAt + 1;
        result = new HashString(s,
            algorithm,
            encoding,
            new Symbol(s[hashStartsAt..]),
            AssumeValid.Option);
        return true;
    }
}
