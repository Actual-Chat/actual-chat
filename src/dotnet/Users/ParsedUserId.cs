namespace ActualChat.Users;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ParsedUserId : IEquatable<ParsedUserId>, IHasId<Symbol>
{
    [DataMember]
    public Symbol Id { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsValid { get; }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public ParsedUserId(Symbol id)
    {
        Id = id;
        IsValid = true;

        var idValue = Id.Value;
        foreach (var c in idValue) {
            if (!char.IsLetterOrDigit(c)) {
                IsValid = false;
                return;
            }
        }
    }

    public ParsedUserId RequireValid()
        => IsValid ? this : throw StandardError.Format("Invalid chat user Id format.");

    // Conversion

    public override string ToString() => Id;
    public static implicit operator ParsedUserId(Symbol source) => new(source);
    public static implicit operator ParsedUserId(string source) => new(source);
    public static implicit operator Symbol(ParsedUserId source) => source.Id;
    public static implicit operator string(ParsedUserId source) => source.Id;

    // Equality

    public bool Equals(ParsedUserId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is ParsedUserId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ParsedUserId left, ParsedUserId right) => left.Equals(right);
    public static bool operator !=(ParsedUserId left, ParsedUserId right) => !left.Equals(right);

    // Parsing

    public static bool TryParse(string value, out ParsedUserId result)
    {
        result = new ParsedUserId(value);
        return result.IsValid;
    }

    public static ParsedUserId Parse(string value)
        => new ParsedUserId(value).RequireValid();
}
