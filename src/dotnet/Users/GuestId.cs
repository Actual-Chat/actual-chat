using Cysharp.Text;
using Stl.Generators;

namespace ActualChat.Users;

[DataContract]
public record struct GuestId(
    [property: DataMember(Order = 0)] Symbol Id)
{
    private static readonly RandomStringGenerator IdGenerator = new(8, Alphabet.AlphaNumeric);
    public static readonly char PrefixChar = '~';

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsValid {
        get {
            var value = Id.Value;
            return value.Length > IdGenerator.Length && value[0] == PrefixChar;
        }
    }

    public static GuestId New()
        => ZString.Concat(PrefixChar, IdGenerator.Next());

    public GuestId RequireValid()
        => IsValid ? this : throw StandardError.Format("Invalid guest Id format.");

    public override string ToString() => Id.Value;
    public static implicit operator GuestId(Symbol source) => new(source);
    public static implicit operator GuestId(string source) => new(source);
    public static implicit operator Symbol(GuestId source) => source.ToString();
    public static implicit operator string(GuestId source) => source.ToString();
}
