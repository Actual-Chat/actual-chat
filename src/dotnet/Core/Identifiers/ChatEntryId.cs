using System.ComponentModel;
using ActualChat.Internal;
using Stl.Fusion.Blazor;

namespace ActualChat;

[DataContract]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<ChatEntryId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<ChatEntryId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<ChatEntryId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ChatEntryId : ISymbolIdentifier<ChatEntryId>
{
    public static ChatEntryId None => default;

    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatEntryKind Kind { get; }
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
    public ChatEntryId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);

    public ChatEntryId(Symbol id, ChatId chatId, ChatEntryKind kind, long localId, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        ChatId = chatId;
        Kind = kind;
        LocalId = localId;
    }

    public ChatEntryId(ChatId chatId, ChatEntryKind kind, long localId, AssumeValid _)
    {
        if (chatId.IsNone) {
            this = None;
            return;
        }
        Id = Format(chatId, kind, localId);
        ChatId = chatId;
        Kind = kind;
        LocalId = localId;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(ChatEntryId source) => source.Id;
    public static implicit operator string(ChatEntryId source) => source.Id.Value;

    // Equality

    public bool Equals(ChatEntryId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is ChatEntryId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ChatEntryId left, ChatEntryId right) => left.Equals(right);
    public static bool operator !=(ChatEntryId left, ChatEntryId right) => !left.Equals(right);

    // Parsing

    private static string Format(ChatId chatId, ChatEntryKind kind, long localId)
        => chatId.IsNone ? "" : Invariant($"{chatId}:{kind:D}:{localId}");

    public static ChatEntryId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ChatEntryId>();
    public static ChatEntryId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : default;

    public static bool TryParse(string? s, out ChatEntryId result)
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
        if (kindLength < 0)
            return false;

        var sKind = s.AsSpan(kindStart, kindLength - kindStart);
        if (!int.TryParse(sKind, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kind))
            return false;
        if (kind is < 0 or > 2)
            return false;

        var sLocalId = s.AsSpan(kindLength + 1);
        if (!long.TryParse(sLocalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var localId))
            return false;
        if (localId < 0)
            return false;

        result = new ChatEntryId(s, chatId, (ChatEntryKind) kind, localId, AssumeValid.Option);
        return true;
    }
}
