using System.ComponentModel;
using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Identifiers.Internal;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<TextEntryId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<TextEntryId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<TextEntryId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct TextEntryId : ISymbolIdentifier<TextEntryId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= DefaultLogFor<TextEntryId>();

    public static TextEntryId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ChatId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public long LocalId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public TextEntryId(Symbol id)
        => this = Parse(id);
    public TextEntryId(string? id)
        => this = Parse(id);
    public TextEntryId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);

    public TextEntryId(Symbol id, ChatId chatId, long localId, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        ChatId = chatId;
        LocalId = localId;
    }

    public TextEntryId(ChatId chatId, long localId, AssumeValid _)
    {
        if (chatId.IsNone || localId < 0) {
            this = None;
            return;
        }
        Id = Format(chatId, localId);
        ChatId = chatId;
        LocalId = localId;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(TextEntryId source) => source.Id;
    public static implicit operator string(TextEntryId source) => source.Id.Value;
    public static implicit operator ChatEntryId(TextEntryId source)
        => new(source.Id, source.ChatId, ChatEntryKind.Text, source.LocalId, AssumeValid.Option);

    // Equality

    public bool Equals(TextEntryId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is TextEntryId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(TextEntryId left, TextEntryId right) => left.Equals(right);
    public static bool operator !=(TextEntryId left, TextEntryId right) => !left.Equals(right);

    // Parsing

    private static string Format(ChatId chatId, long localId)
        => chatId.IsNone ? "" : $"{chatId}:0:{localId.Format()}";

    public static TextEntryId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<TextEntryId>(s);
    public static TextEntryId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<TextEntryId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out TextEntryId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        var chatIdLength = s.OrdinalIndexOf(":");
        if (chatIdLength < 0)
            return false;
        if (!ChatId.TryParse(s[..chatIdLength], out var chatId))
            return false;

        var kindStart = chatIdLength + 1;
        var kindLength = s.OrdinalIndexOf(":", kindStart);
        if (kindLength != kindStart + 1 || s[kindStart] != '0') // "0" = ChatEntryKind.Text.Format()
            return false;

        var sLocalId = s.AsSpan(kindLength + 1);
        if (!NumberExt.TryParsePositiveLong(sLocalId, out var localId))
            return false;

        result = new TextEntryId(s, chatId, localId, AssumeValid.Option);
        return true;
    }
}
