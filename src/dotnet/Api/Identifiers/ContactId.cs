using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<ContactId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<ContactId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<ContactId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<ContactId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class ContactId : StringIdentifier, IStringIdentifier<ContactId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ContactId>();
    private static readonly ILruCache<string, ContactId> Cache = CreateCache<ContactId>(512);

    [IgnoreDataMember]
    public UserId OwnerId { get; }
    [IgnoreDataMember]
    public ChatId ChatId { get; }
    [IgnoreDataMember]
    public ContactKind Kind { get; }

    // Factories and constructors

    public static ContactId NewAny(UserId ownerId, ChatId chatId)
        => chatId is PeerChatId peerChatId
            ? NewUser(ownerId, peerChatId.AnotherUserId(ownerId)) // Ensures we create only valid ContactIds in this case
            : new ContactId(Format(ownerId, chatId), ownerId, chatId);

    public static ContactId NewUser(UserId ownerId, UserId otherUserId)
    {
        var chatId = PeerChatId.New(ownerId, otherUserId);
        return new ContactId(Format(ownerId, chatId), ownerId, chatId);
    }

    // There are two types of Chat contacts: Group chats & Place chats
    public static ContactId NewChat(UserId ownerId, GroupChatId chatId)
        => new(Format(ownerId, chatId), ownerId, chatId);
    public static ContactId NewChat(UserId ownerId, PlaceChatId chatId)
        => new(Format(ownerId, chatId), ownerId, chatId);

    // Place contact is a contact referencing place's root chat
    public static ContactId NewPlace(UserId ownerId, PlaceId placeId)
    {
        var chatId = placeId.RootChatId;
        return new(Format(ownerId, chatId), ownerId, chatId);
    }

    private ContactId(string value, UserId ownerId, ChatId chatId)
        : base(value)
    {
        OwnerId = ownerId;
        ChatId = chatId;
        var parent = chatId.IsThread(out var threadChatId) ? threadChatId.GetOutermostParent() : chatId;
        Kind = parent.Kind switch {
            ChatKind.Peer => ContactKind.User,
            ChatKind.Place => ((PlaceChatId)parent).IsRoot ? ContactKind.Place : ContactKind.Chat,
            _ => ContactKind.Chat,
        };
    }

    // Equality

    public bool Equals(ContactId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is ContactId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ContactId? left, ContactId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ContactId? left, ContactId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(UserId ownerId, ChatId chatId)
        => $"{ownerId.Value} {chatId.Value}";

    public static ContactId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ContactId>(s);

    public static ContactId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static ContactId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out ContactId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var ownerIdLength = s.OrdinalIndexOf(' ');
        if (ownerIdLength <= 0)
            return false;

        if (!UserId.TryParse(s[..ownerIdLength], out var ownerId))
            return false;
        if (!ChatId.TryParse(s[(ownerIdLength + 1)..], out var chatId))
            return false;
        if (chatId is PeerChatId peerChatId && peerChatId.UserId1 != ownerId && peerChatId.UserId2 != ownerId)
            return false;

        result = new ContactId(s, ownerId, chatId);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
