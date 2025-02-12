using System.ComponentModel;
using ActualLab.Fusion.Blazor;
using ActualLab.Identifiers.Internal;
using MemoryPack;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<ConversationId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<ConversationId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<ConversationId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct ConversationId : ISymbolIdentifier<ConversationId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ConversationId>();

    public static ConversationId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ChatId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public long StartEntryLid { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public ConversationId(Symbol id)
        => this = Parse(id);
    public ConversationId(string? id)
        => this = Parse(id);
    public ConversationId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);

    public ConversationId(Symbol id, ChatId chatId, long startEntryLid, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        ChatId = chatId;
        StartEntryLid = startEntryLid;
    }

    public ConversationId(ChatId chatId, long startEntryLid, AssumeValid _)
    {
        if (chatId.IsNone || startEntryLid < 0) {
            this = None;
            return;
        }
        Id = Format(chatId, startEntryLid);
        ChatId = chatId;
        StartEntryLid = startEntryLid;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(ConversationId source) => source.Id;
    public static implicit operator string(ConversationId source) => source.Id.Value;

    // Equality

    public bool Equals(ConversationId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is ConversationId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ConversationId left, ConversationId right) => left.Equals(right);
    public static bool operator !=(ConversationId left, ConversationId right) => !left.Equals(right);

    // Parsing

    public static string Format(ChatId chatId, long startEntryLid)
        => chatId.IsNone ? "" : $"{chatId}:{startEntryLid.Format()}";

    public static ConversationId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ConversationId>(s);
    public static ConversationId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<ConversationId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out ConversationId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        var chatIdLength = s.OrdinalIndexOf(":");
        if (chatIdLength < 0)
            return false;

        if (!ChatId.TryParse(s[..chatIdLength], out var chatId))
            return false;

        var sStartEntryLid = s.AsSpan(chatIdLength + 1);
        if (!NumberExt.TryParsePositiveLong(sStartEntryLid, out var startEntryLid))
            return false;

        result = new ConversationId(s, chatId, startEntryLid, AssumeValid.Option);
        return true;
    }
}
