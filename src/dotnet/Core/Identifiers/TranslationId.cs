using System.ComponentModel;
using ActualLab.Fusion.Blazor;
using ActualLab.Identifiers.Internal;
using MemoryPack;

namespace ActualChat;

#pragma warning disable CA1036, MA0097 // Implement comparison operators: <, <=, etc.

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<TranslationId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<TranslationId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<TranslationId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct TranslationId : ISymbolIdentifier<TranslationId>
{
    private const string Delimiter = ":";

    [field: AllowNull, MaybeNull]
    private static ILogger Log => field ??= StaticLog.For<ChatId>();

    public static TranslationId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatEntryId ChatEntryId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Language Language { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public TranslationId(Symbol id)
        => this = Parse(id);
    public TranslationId(string? id)
        => this = Parse(id);
    public TranslationId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);

    private TranslationId(Symbol id, ChatEntryId chatEntryId, Language language, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        ChatEntryId = chatEntryId;
        Language = language;
    }

    public TranslationId(ChatEntryId chatEntryId, Language language, AssumeValid _)
    {
        if (chatEntryId.IsNone || language.IsNone) {
            this = None;
            return;
        }
        Id = Format(chatEntryId, language);
        ChatEntryId = chatEntryId;
        Language = language;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(TranslationId source) => source.Id;
    public static implicit operator string(TranslationId source) => source.Id.Value;
    public static explicit operator TranslationId(string source) => new(source);

    // Equality

    public bool Equals(TranslationId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is TranslationId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(TranslationId left, TranslationId right) => left.Equals(right);
    public static bool operator !=(TranslationId left, TranslationId right) => !left.Equals(right);

    // Parsing

    private static string Format(ChatEntryId chatEntryId, Language language)
        => chatEntryId.IsNone ? "" : $"{chatEntryId}{Delimiter}{language.Id}";

    public static TranslationId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<TranslationId>(s);
    public static TranslationId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<TranslationId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out TranslationId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        if (s.Length < 6)
            return false;

        var entryIdLength = s.OrdinalLastIndexOf(Delimiter);
        if (entryIdLength < 0)
            return false;

        if (!ChatEntryId.TryParse(s[..entryIdLength], out var entryId))
            return false;

        var languageStart = entryIdLength + 1;
        if (!Language.TryParse(s[languageStart..], out var language))
            return false;

        result = new TranslationId(s, entryId, language, AssumeValid.Option);
        return true;
    }

}
