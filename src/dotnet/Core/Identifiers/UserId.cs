using System.ComponentModel;
using System.Numerics;
using Cysharp.Text;
using MemoryPack;
using ActualLab.Generators;
using ActualLab.Fusion.Blazor;
using ActualLab.Identifiers.Internal;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<UserId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<UserId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<UserId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct UserId : ISymbolIdentifier<UserId>,
    IComparable<UserId>,
    IComparisonOperators<UserId, UserId, bool>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= DefaultLogFor<UserId>();
    private static readonly RandomStringGenerator IdGenerator = new(6, Alphabet.AlphaNumeric);
    private static readonly RandomStringGenerator GuestIdGenerator = new(8, Alphabet.AlphaNumeric);

    public static readonly char GuestIdPrefixChar = '~';
    public static UserId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsGuest => Value is { } v && v.Length != 0 && v[0] == GuestIdPrefixChar;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsGuestOrNone => IsNone || Value[0] == GuestIdPrefixChar;

    public static UserId New()
        => new(IdGenerator.Next(), AssumeValid.Option);
    public static UserId NewGuest()
        => new(ZString.Concat(GuestIdPrefixChar, GuestIdGenerator.Next()), AssumeValid.Option);

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public UserId(Symbol id)
        => this = Parse(id);
    public UserId(string? id)
        => this = Parse(id);
    public UserId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);

    public UserId(Symbol id, AssumeValid _)
        => Id = id;

    // Conversion

    public override string ToString() => Id;
    public static implicit operator Symbol(UserId source) => source.Id;
    public static implicit operator string(UserId source) => source.Id.Value;
    public static explicit operator UserId(string source) => new (source);

    // Comparison

    public int CompareTo(UserId other) => Id.CompareTo(other.Id);
    public static bool operator >(UserId left, UserId right) => left.CompareTo(right) > 0;
    public static bool operator >=(UserId left, UserId right) => left.CompareTo(right) >= 0;
    public static bool operator <(UserId left, UserId right) => left.CompareTo(right) < 0;
    public static bool operator <=(UserId left, UserId right) => left.CompareTo(right) <= 0;

    // Equality

    public bool Equals(UserId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is UserId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(UserId left, UserId right) => left.Equals(right);
    public static bool operator !=(UserId left, UserId right) => !left.Equals(right);

    // Parsing

    public static UserId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<UserId>(s);
    public static UserId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<UserId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out UserId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        if (s.Length < 3) // Tests may use some accounts with short Ids + there is "admin"
            return false;

        var alphabet = Alphabet.AlphaNumericDash;
        for (var i = 0; i < s.Length; i++) {
            var c = s[i];
            if (!alphabet.IsMatch(c)) {
                if (c == GuestIdPrefixChar && i == 0)
                    continue; // GuestId
                return false;
            }
        }

        result = new UserId(s, AssumeValid.Option);
        return true;
    }
}
