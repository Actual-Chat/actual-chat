using System.ComponentModel;
using ActualChat.Internal;
using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<AuthorId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<AuthorId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<AuthorId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct AuthorId : ISymbolIdentifier<AuthorId>
{
    public static AuthorId None => default;

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
    public AuthorId(Symbol id)
        => this = Parse(id);
    public AuthorId(string? id)
        => this = Parse(id);
    public AuthorId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);

    public AuthorId(Symbol id, ChatId chatId, long localId, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        ChatId = chatId;
        LocalId = localId;
    }

    public AuthorId(ChatId chatId, long localId, AssumeValid _)
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
    public static implicit operator Symbol(AuthorId source) => source.Id;
    public static implicit operator string(AuthorId source) => source.Id.Value;

    // Equality

    public bool Equals(AuthorId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is AuthorId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(AuthorId left, AuthorId right) => left.Equals(right);
    public static bool operator !=(AuthorId left, AuthorId right) => !left.Equals(right);

    // Parsing

    public static string Format(ChatId chatId, long localId)
        => chatId.IsNone ? "" : $"{chatId.Value}:{localId.Format()}";

    public static AuthorId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<AuthorId>(s);
    public static AuthorId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<AuthorId>(s).LogWarning(DefaultLog, None);

    public static bool TryParse(string? s, out AuthorId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        var chatIdLength = s.OrdinalIndexOf(":");
        if (chatIdLength == -1)
            return false;

        if (!ChatId.TryParse(s[..chatIdLength], out var chatId))
            return false;

        var tail = s[(chatIdLength + 1)..];
        if (!NumberExt.TryParseLong(tail, out var localId))
            return false;

        result = new AuthorId(s, chatId, localId, AssumeValid.Option);
        return true;
    }
}
