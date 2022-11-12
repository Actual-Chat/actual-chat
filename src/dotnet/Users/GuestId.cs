using Cysharp.Text;
using Stl.Generators;

namespace ActualChat.Users;

[DataContract]
public record struct GuestId(
    [property: DataMember(Order = 0)] Symbol Id)
{
    private static RandomStringGenerator IdGenerator { get; } = new (11, Alphabet.Alpha);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsValid {
        get {
            var value = Id.Value;
            return value.Length >= 8 && value[0] == '@';
        }
    }

    public static GuestId New()
        => ZString.Concat('@', IdGenerator.Next());

    public GuestId RequireValid()
        => IsValid ? this : throw StandardError.Format("Invalid guest Id format.");

    public override string ToString() => Id.Value;
    public static implicit operator GuestId(Symbol source) => new(source);
    public static implicit operator GuestId(string source) => new(source);
    public static implicit operator Symbol(GuestId source) => source.ToString();
    public static implicit operator string(GuestId source) => source.ToString();
}
