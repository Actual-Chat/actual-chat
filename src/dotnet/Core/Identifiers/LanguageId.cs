using System.ComponentModel;
using ActualChat.Internal;
using Stl.Fusion.Blazor;

namespace ActualChat;

[DataContract]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<LanguageId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierJsonConverter<LanguageId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<LanguageId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly struct LanguageId : ISymbolIdentifier<LanguageId>
{
    public static LanguageId None { get; } = new("", "?", "Unknown", AssumeValid.Option);
    public static LanguageId English { get; } = new("en-US", "EN", "English", AssumeValid.Option);
    public static LanguageId French { get; } = new("fr-FR", "FR", "French", AssumeValid.Option);
    public static LanguageId German { get; } = new("de-DE", "DE", "German", AssumeValid.Option);
    public static LanguageId Russian { get; } = new("ru-RU", "RU", "Russian", AssumeValid.Option);
    public static LanguageId Spanish { get; } = new("es-ES", "ES", "Spanish", AssumeValid.Option);
    public static LanguageId Ukrainian { get; } = new("uk-UA", "UA", "Ukrainian", AssumeValid.Option);
    public static LanguageId Main { get; } = English;

    public static ImmutableArray<LanguageId> All { get; } = ImmutableArray.Create(
        English,
        French,
        German,
        Russian,
        Spanish,
        Ukrainian
    );

    private static readonly Dictionary<Symbol, LanguageId> ParsableIds =
        All.Select(id => (Key: id.Id, Id: id))
            .Concat(All.Select(id => (Key: (Symbol)id.Value.ToLowerInvariant(), Id: id)))
            .Concat(All.Select(id => (Key: id.Shortcut, Id: id)))
            .Concat(All.Select(id => (Key: (Symbol)id.Shortcut.Value.ToLowerInvariant(), Id: id)))
            .DistinctBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key, kv => kv.Id);

    private readonly LanguageInfo _info;

    [DataMember(Order = 0)]
    public Symbol Id => Info.Id;

    // Set on deserialization
    private LanguageInfo Info => _info ?? None.Info;

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsNone => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Symbol Shortcut => Info.Shortcut;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Title => Info.Title;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public LanguageId(Symbol id)
        => this = Parse(id);
    public LanguageId(string? id)
        => this = Parse(id);
    public LanguageId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);

    private LanguageId(Symbol id, Symbol shortcut, string title, AssumeValid _)
        => _info = new LanguageInfo(id, shortcut, title);

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(LanguageId source) => source.Id;
    public static implicit operator string(LanguageId source) => source.Value;

    // Equality

    public bool Equals(LanguageId other) => ReferenceEquals(Info, other.Info);
    public override bool Equals(object? obj) => obj is LanguageId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(LanguageId left, LanguageId right) => left.Equals(right);
    public static bool operator !=(LanguageId left, LanguageId right) => !left.Equals(right);

    // Parsing

    public static LanguageId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<LanguageId>();
    public static LanguageId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : default;

    public static bool TryParse(string? s, out LanguageId result)
    {
        var id = (Symbol)s;
        if (ParsableIds.TryGetValue(id, out result))
            return true;
        if (ParsableIds.TryGetValue(id.Value.ToLowerInvariant(), out result))
            return true;
        return false;
    }

    // Nested types

    [DataContract]
    private sealed record LanguageInfo(
        [property: DataMember] Symbol Id,
        [property: DataMember] Symbol Shortcut,
        [property: DataMember] string Title);
}
