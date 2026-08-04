using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Hashing;

[DataContract]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<HashString>))]
[JsonConverter(typeof(StringLikeJsonConverter<HashString>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<HashString>))]
[TypeConverter(typeof(StringLikeTypeConverter<HashString>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct HashString : ISymbolIdentifier<HashString>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<HashString>();
    private const string Delimiter = " ";

    public static HashString None => default;

    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public HashAlgorithm Algorithm { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public HashEncoding Encoding { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string Hash { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
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

    public HashString(Symbol id, HashAlgorithm algorithm, HashEncoding encoding, string hash, AssumeValid _)
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

    public HashString(HashAlgorithm algorithm, HashEncoding encoding, string hash, AssumeValid _)
    {
        if (hash.IsNullOrEmpty()) {
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

        var algoEndsAt = s.IndexOf(Delimiter);
        if (algoEndsAt < 0)
            return false;

        if (!Enum.TryParse<HashAlgorithm>(s[..algoEndsAt], out var algorithm))
            return false;

        var encodingStartsAt = algoEndsAt + Delimiter.Length;
        var encodingEndsAt = s.IndexOf(Delimiter, encodingStartsAt);
        if (!Enum.TryParse<HashEncoding>(s[encodingStartsAt..encodingEndsAt], out var encoding))
            return false;

        var hashStartsAt = encodingEndsAt + Delimiter.Length;
        result = new HashString(s,
            algorithm,
            encoding,
            new Symbol(s[hashStartsAt..]),
            AssumeValid.Option);
        return true;
    }
}
