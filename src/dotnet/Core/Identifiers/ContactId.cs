using System.ComponentModel;
using ActualChat.Internal;
using Stl.Fusion.Blazor;

namespace ActualChat;

[DataContract]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<ContactId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<ContactId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<ContactId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly struct ContactId : ISymbolIdentifier<ContactId>
{
    public static ContactId None => default;

    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public UserId OwnerId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public ContactId(Symbol id)
        => this = Parse(id);
    public ContactId(UserId ownerId, ChatId chatId)
        => this = Parse(Format(ownerId, chatId));
    public ContactId(UserId ownerId, ChatId chatId, ParseOrNone _)
        => this = ParseOrNone(Format(ownerId, chatId));
    public ContactId(string id)
        => this = Parse(id);
    public ContactId(string id, ParseOrNone _)
        => this = ParseOrNone(id);

    public ContactId(Symbol id, UserId ownerId, ChatId chatId, AssumeValid _)
    {
        Id = id;
        OwnerId = ownerId;
        ChatId = chatId;
    }

    public ContactId(UserId ownerId, ChatId chatId, AssumeValid _)
    {
        Id = Format(ownerId, chatId);
        OwnerId = ownerId;
        ChatId = chatId;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(ContactId source) => source.Id;
    public static implicit operator string(ContactId source) => source.Id.Value;

    // Equality

    public bool Equals(ContactId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is ContactId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ContactId left, ContactId right) => left.Equals(right);
    public static bool operator !=(ContactId left, ContactId right) => !left.Equals(right);

    // Parsing

    private static string Format(UserId ownerId, ChatId chatId)
        => ownerId.IsNone ? "" : $"{ownerId} {chatId}";

    public static ContactId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ContactId>();
    public static ContactId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : default;

    public static bool TryParse(string? s, out ContactId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        var userIdLength = s.IndexOf(' ');
        if (userIdLength <= 0)
            return false;

        if (!UserId.TryParse(s[..userIdLength], out var ownerId))
            return false;
        if (!ChatId.TryParse(s[(userIdLength + 1)..], out var chatId))
            return false;
        if (chatId.IsPeerChatId(out var peerChatId) && peerChatId.UserId1 != ownerId && peerChatId.UserId2 != ownerId)
            return false;

        result = new ContactId(s, ownerId, chatId, AssumeValid.Option);
        return true;
    }
}
