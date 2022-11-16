using Cysharp.Text;
using Stl.Generators;

namespace ActualChat;

#pragma warning disable MA0011

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct UserId : IEquatable<UserId>, IComparable<UserId>, IRequirementTarget, ICanBeEmpty
{
    private static readonly RandomStringGenerator IdGenerator = new(6, Alphabet.AlphaNumeric);
    private static readonly RandomStringGenerator GuestIdGenerator = new(8, Alphabet.AlphaNumeric);
    public static readonly char GuestIdPrefixChar = '~';

    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsEmpty => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsGuestId => !IsEmpty && Value[0] == GuestIdPrefixChar;

    public static UserId New()
        => new(IdGenerator.Next(), ActualChat.Parse.None);
    public static UserId NewGuest()
        => new(ZString.Concat(GuestIdPrefixChar, GuestIdGenerator.Next()), ActualChat.Parse.None);

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public UserId(Symbol id) => this = Parse(id);
    public UserId(string id) => this = Parse(id);
    public UserId(Symbol id, SkipParseTag _) => Id = id;
    public UserId(string id, ParseOrDefaultTag _) => ParseOrDefault(id);

    // Conversion

    public override string ToString() => Id;
    public static implicit operator Symbol(UserId source) => source.Id;
    public static implicit operator string(UserId source) => source.Value;

    // Equality

    public int CompareTo(UserId other) => Id.CompareTo(other.Id);
    public bool Equals(UserId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is UserId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(UserId left, UserId right) => left.Equals(right);
    public static bool operator !=(UserId left, UserId right) => !left.Equals(right);

    // Parsing

    public static UserId Parse(string s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<UserId>();
    public static UserId ParseOrDefault(string s)
        => TryParse(s, out var result) ? result : default;

    public static bool TryParse(string? s, out UserId result)
    {
        result = default;
        if (s == null || s.Length < 3) // Tests may use some accounts with short Ids + there is "admin"
            return false;

        for (var i = 0; i < s.Length; i++) {
            var c = s[i];
            if (!char.IsLetterOrDigit(c)) {
                if (c == GuestIdPrefixChar && i == 0)
                    continue; // GuestId
                return false;
            }
        }

        result = new UserId(s, ActualChat.Parse.None);
        return true;
    }
}
