using System.ComponentModel;
using ActualChat.Internal;
using Stl.Fusion.Blazor;

namespace ActualChat;

[DataContract]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<Language>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<Language>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<Language>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly struct Language : ISymbolIdentifier<Language>
{
    public static Language None => default;

    private readonly LanguageHandle? _handle;

    [DataMember(Order = 0)]
    public Symbol Id => Handle.Id;

    // Set on deserialization
    private LanguageHandle Handle => _handle ?? LanguageHandle.None;

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsNone => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Symbol Shortcut => Handle.Shortcut;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Title => Handle.Title;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public Language(Symbol id)
        => this = Parse(id);
    public Language(string? id)
        => this = Parse(id);
    public Language(string? id, ParseOrNone _)
        => this = ParseOrNone(id);

    internal Language(Symbol id, Symbol shortcut, string title, AssumeValid _)
        => _handle = new LanguageHandle(id, shortcut, title);

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(Language source) => source.Id;
    public static implicit operator string(Language source) => source.Value;
    public static implicit operator Language(Symbol source) => new(source);
    public static implicit operator Language(string source) => new(source);

    // Equality

    public bool Equals(Language other) => ReferenceEquals(Handle, other.Handle);
    public override bool Equals(object? obj) => obj is Language other && Equals(other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(Handle);
    public static bool operator ==(Language left, Language right) => left.Equals(right);
    public static bool operator !=(Language left, Language right) => !left.Equals(right);

    // Parsing

    public static Language Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<Language>();
    public static Language ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : default;

    public static bool TryParse(string? s, out Language result)
    {
        var id = (Symbol)s;
        if (id.IsEmpty) {
            result = default;
            return true; // None
        }

        if (Languages.IdToLanguage.TryGetValue(id, out result))
            return true;
        if (Languages.IdToLanguage.TryGetValue(id.Value.ToLowerInvariant(), out result))
            return true;

        return false;
    }
}
