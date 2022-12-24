using System.ComponentModel;
using ActualChat.Internal;
using Stl.Fusion.Blazor;

namespace ActualChat;

[DataContract]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<RoleId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<RoleId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<RoleId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly struct RoleId : ISymbolIdentifier<RoleId>
{
    public static RoleId None => default;

    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Set on deserialization
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
    public RoleId(Symbol id)
        => this = Parse(id);
    public RoleId(string? id)
        => this = Parse(id);
    public RoleId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);

    public RoleId(Symbol id, ChatId chatId, long localId, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        ChatId = chatId;
        LocalId = localId;
    }

    public RoleId(ChatId chatId, long localId, AssumeValid _)
    {
        if (chatId.IsNone) {
            this = None;
            return;
        }
        Id = Format(chatId, localId);
        ChatId = chatId;
        LocalId = localId;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(RoleId source) => source.Id;
    public static implicit operator string(RoleId source) => source.Id.Value;

    // Equality

    public bool Equals(RoleId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is RoleId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(RoleId left, RoleId right) => left.Equals(right);
    public static bool operator !=(RoleId left, RoleId right) => !left.Equals(right);

    // Parsing

    private static string Format(ChatId chatId, long localId)
        => chatId.IsNone ? "" : $"{chatId.Value}:{localId.ToString(CultureInfo.InvariantCulture)}";

    public static RoleId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<RoleId>();
    public static RoleId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : default;

    public static bool TryParse(string? s, out RoleId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true;

        var chatIdLength = s.OrdinalIndexOf(":");
        if (chatIdLength == -1)
            return false;

        if (!ChatId.TryParse(s[..chatIdLength], out var chatId))
            return false;

        if (!long.TryParse(s.AsSpan(chatIdLength + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var localId))
            return false;
        if (localId < 0)
            return false;

        result = new RoleId(s, chatId, localId, AssumeValid.Option);
        return true;
    }
}
