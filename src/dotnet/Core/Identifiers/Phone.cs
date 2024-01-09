using System.ComponentModel;
using ActualChat.Internal;
using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<Phone>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<Phone>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<Phone>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct Phone : ISymbolIdentifier<Phone>
{
    private const char Delimiter = '-';
    public static Phone None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Code { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Number { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsValid
        => !IsNone && IsNormalized(Code) && IsNormalized(Number) && TryParse(Id, out _);

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public Phone(Symbol id)
        => this = Parse(id);
    public Phone(string? id)
        => this = Parse(id);
    public Phone(string? id, ParseOrNone _)
        => this = ParseOrNone(id);

    public Phone(Symbol id, string code, string number, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        Code = code;
        Number = number;
    }

    public Phone(string code, string number, AssumeValid _)
    {
        if (code.IsNullOrEmpty() || number.IsNullOrEmpty()) {
            this = None;
            return;
        }
        Id = Format(code, number);
        Code = code;
        Number = number;
    }

    public Phone(string code, string number, bool normalize = true)
    {
        if (code.IsNullOrEmpty() || number.IsNullOrEmpty()) {
            this = None;
            return;
        }

        if (normalize) {
            code = Normalize(code);
            number = Normalize(number);
            if (code.IsNullOrEmpty() || number.IsNullOrEmpty())
                throw StandardError.Format<Phone>(Format(code, number));
        }
        else {
            if (!IsNormalized(code) || !IsNormalized(number))
                throw StandardError.Format<Phone>(Format(code, number));
        }

        Id = Format(code, number);
        Code = code;
        Number = number;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(Phone source) => source.Id;
    public static implicit operator string(Phone source) => source.Id.Value;

    public string ToInternational()
        => $"+{Code}{Number}";

    // Equality

    public bool Equals(Phone other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is Phone other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(Phone left, Phone right) => left.Equals(right);
    public static bool operator !=(Phone left, Phone right) => !left.Equals(right);

    // Parsing
    private static string Format(string code, string number)
        => $"{code}{Delimiter}{number}";
    public static Phone Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<Phone>(s);
    public static Phone ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<Phone>(s).LogWarning(DefaultLog, None);

    public static bool TryParse(string? s, out Phone result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        var idx = s.LastIndexOf(Delimiter);
        if (idx < 0)
            return false;

        var code = s[..idx];
        var number = s[(idx + 1)..];
        if (!code.All(char.IsDigit) || !number.All(char.IsDigit))
            return false;

        var id = Format(code, number);

        result = new Phone(id, code, number, AssumeValid.Option);
        return true;
    }

    public static string Normalize(string phonePart)
        => new (phonePart.Where(char.IsDigit).ToArray());

    public static bool IsNormalized(string phonePart)
        => phonePart.All(char.IsDigit);
}
