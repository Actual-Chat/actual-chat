using System.ComponentModel;
using ActualChat.Internal;

namespace ActualChat;

[DataContract]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<AuthorId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierJsonConverter<AuthorId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<AuthorId>))]
[StructLayout(LayoutKind.Auto)]
public readonly struct AuthorId : ISymbolIdentifier<AuthorId>
{
    public static AuthorId None => default;

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
    public AuthorId(Symbol id)
        => this = Parse(id);
    public AuthorId(string? id)
        => this = Parse(id);
    public AuthorId(string? id, ParseOrNoneOption _)
        => this = ParseOrNone(id);

    public AuthorId(Symbol id, ChatId chatId, long localId, SkipParseOption _)
    {
        Id = id;
        ChatId = chatId;
        LocalId = localId;
    }

    public AuthorId(ChatId chatId, long localId, SkipParseOption _)
    {
        Id = Format(chatId, localId);
        ChatId = chatId;
        LocalId = localId;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(AuthorId source) => source.Id;
    public static implicit operator string(AuthorId source) => source.Value;

    // Equality

    public bool Equals(AuthorId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is AuthorId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(AuthorId left, AuthorId right) => left.Equals(right);
    public static bool operator !=(AuthorId left, AuthorId right) => !left.Equals(right);

    // Parsing

    private static string Format(ChatId chatId, long localId)
        => $"{chatId.Value}:{localId.ToString(CultureInfo.InvariantCulture)}";

    public static AuthorId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<AuthorId>();
    public static AuthorId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : default;

    public static bool TryParse(string? s, out AuthorId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return false;

        var chatIdLength = s.OrdinalIndexOf(":");
        if (chatIdLength == -1)
            return false;

        if (!ChatId.TryParse(s[..chatIdLength], out var chatId))
            return false;

        var tail = s[(chatIdLength + 1)..];
        if (!long.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var localId))
            return false;

        result = new AuthorId(s, chatId, localId, ParseOptions.Skip);
        return true;
    }
}
