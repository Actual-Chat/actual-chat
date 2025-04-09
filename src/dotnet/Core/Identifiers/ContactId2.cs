using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract]
[JsonConverter(typeof(StringIdentifierJsonConverter<ContactId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<ContactId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<ContactId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class ContactId2(string value, UserId2 ownerId, ChatId2 chatId, AssumeValid _)
    : StringIdentifier(value), IStringIdentifier<ContactId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ContactId2>();
    private static readonly ILruCache<string, ContactId2> Cache = CreateCache<ContactId2>(256);

    [IgnoreDataMember]
    public UserId2 OwnerId { get; } = ownerId;
    [IgnoreDataMember]
    public ChatId2 ChatId { get; } = chatId;

    // Factories

    public static ContactId2 New(UserId2 ownerId, ChatId2 chatId)
        => new(Format(ownerId, chatId), ownerId, chatId, AssumeValid.Option);

    public static ContactId2 NewPeer(UserId2 ownerId, UserId2 otherUserId)
        => New(ownerId, PeerChatId2.New(ownerId, otherUserId).AsChatId);

    // Equality

    public bool Equals(ContactId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is ContactId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ContactId2? left, ContactId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ContactId2? left, ContactId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(UserId2 ownerId, ChatId2 chatId)
        => $"{ownerId.Value} {chatId.Value}";

    public static ContactId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ContactId2>(s);

    public static bool TryParse(string? s, [NotNullWhen(true)] out ContactId2? result)
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

        if (!UserId2.TryParse(s[..ownerIdLength], out var ownerId))
            return false;
        if (!ChatId2.TryParse(s[(ownerIdLength + 1)..], out var chatId))
            return false;
        if (chatId.PeerChatId is { } peerChatId && peerChatId.UserId1 != ownerId && peerChatId.UserId2 != ownerId)
            return false;

        result = new ContactId2(s, ownerId, chatId, AssumeValid.Option);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}

public static class ContactId2Ext
{
    public static UserId2? GetOtherUserId(this ContactId2 id)
        => id.ChatId.PeerChatId is { } peerChatId
            ? peerChatId.AnotherUserIdOrNull(id.OwnerId)
            : null;
}
