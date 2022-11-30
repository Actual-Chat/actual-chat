using System.ComponentModel;
using ActualChat.Internal;

namespace ActualChat;

[DataContract]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<ChatEntryId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierJsonConverter<ChatEntryId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<ChatEntryId>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ChatEntryId : ISymbolIdentifier<ChatEntryId>
{
    public static ChatEntryId None => default;

    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatEntryKind EntryKind { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public long LocalId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public ChatEntryId(Symbol id)
        => this = Parse(id);
    public ChatEntryId(string? id)
        => this = Parse(id);
    public ChatEntryId(string? id, ParseOrNoneOption _)
        => this = ParseOrNone(id);

    public ChatEntryId(Symbol id, ChatId chatId, ChatEntryKind entryKind, long localId, SkipParseOption _)
    {
        Id = id;
        ChatId = chatId;
        EntryKind = entryKind;
        LocalId = localId;
    }

    public ChatEntryId(ChatId chatId, ChatEntryKind entryKind, long localId, SkipParseOption _)
    {
        Id = Invariant($"{chatId}:{entryKind:D}:{localId}");
        ChatId = chatId;
        LocalId = localId;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(ChatEntryId source) => source.Id;
    public static implicit operator string(ChatEntryId source) => source.Value;

    // Equality

    public bool Equals(ChatEntryId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is ChatEntryId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ChatEntryId left, ChatEntryId right) => left.Equals(right);
    public static bool operator !=(ChatEntryId left, ChatEntryId right) => !left.Equals(right);

    // Parsing

    public static ChatEntryId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ChatEntryId>();
    public static ChatEntryId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : default;

    public static bool TryParse(string? s, out ChatEntryId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return false;

        var chatIdLength = s.OrdinalIndexOf(":");
        if (chatIdLength == -1)
            return false;

        if (!ChatId.TryParse(s[..chatIdLength], out var chatId))
            return false;

        var entryKindLength = s.OrdinalIndexOf(":", chatIdLength + 1);
        if (entryKindLength == -1)
            return false;

        var sEntryKind = s[(chatIdLength + 1)..entryKindLength];
        if (!Enum.TryParse<ChatEntryKind>(sEntryKind, out var entryKind))
            return false;

        var sLocalId = s[(entryKindLength + 1)..];
        if (!long.TryParse(sLocalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var localId))
            return false;

        result = new ChatEntryId(s, chatId, entryKind, localId, ParseOptions.Skip);
        return true;
    }
}
