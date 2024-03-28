using System.ComponentModel;
using ActualChat.Internal;
using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<ContactId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<ContactId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<ContactId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct ContactId : ISymbolIdentifier<ContactId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= DefaultLogFor<ContactId>();

    public static ContactId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UserId OwnerId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ChatId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
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
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        OwnerId = ownerId;
        ChatId = chatId;
    }

    public ContactId(UserId ownerId, ChatId chatId, AssumeValid _)
    {
        if (ownerId.IsNone || chatId.IsNone) {
            this = None;
            return;
        }
        Id = Format(ownerId, chatId);
        OwnerId = ownerId;
        ChatId = chatId;
    }

    public static ContactId Peer(UserId ownerId, UserId otherUserId)
        => new (ownerId, new PeerChatId(ownerId, otherUserId));

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

    public static string Format(UserId ownerId, ChatId chatId)
        => ownerId.IsNone || chatId.IsNone ? "" : $"{ownerId} {chatId}";

    public static ContactId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ContactId>(s);
    public static ContactId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<ContactId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out ContactId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        var ownerIdLength = s.OrdinalIndexOf(' ');
        if (ownerIdLength <= 0)
            return false;

        if (!UserId.TryParse(s[..ownerIdLength], out var ownerId))
            return false;
        if (!ChatId.TryParse(s[(ownerIdLength + 1)..], out var chatId))
            return false;
        if (chatId.IsPeerChat(out var peerChatId) && peerChatId.UserId1 != ownerId && peerChatId.UserId2 != ownerId)
            return false;

        result = new ContactId(s, ownerId, chatId, AssumeValid.Option);
        return true;
    }
}
