using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<PeerChatId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<PeerChatId2>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<PeerChatId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<PeerChatId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class PeerChatId2 : ChatId2, IStringIdentifier<PeerChatId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<PeerChatId2>();

    public static readonly string IdPrefix = "p-";

    [IgnoreDataMember]
    public UserId2 UserId1 { get; }
    [IgnoreDataMember]
    public UserId2 UserId2 { get; }
    [IgnoreDataMember]
    public (UserId2 UserId1, UserId2 UserId2) UserIds => (UserId1, UserId2);

    // Factories and constructors

    public static PeerChatId2 New(UserId2 userId1, UserId2 userId2)
    {
        if (userId1 == userId2)
            throw new ArgumentOutOfRangeException(nameof(userId2), "Both user IDs are the same.");

        (userId1, userId2) = (userId1, userId2).Sort(UserId2.Comparer);
        return new(Format(userId1, userId2), userId1, userId2);
    }

    internal PeerChatId2(string value, UserId2 userId1, UserId2 userId2)
        : base(value, ChatKind.Peer)
    {
        UserId1 = userId1;
        UserId2 = userId2;
    }

    // Helpers

    public int IndexOf(UserId2 userId)
    {
        if (UserId1 == userId)
            return 0;
        if (UserId2 == userId)
            return 1;

        return -1;
    }

    public bool HasUser(UserId2 userId)
        => IndexOf(userId) != -1;

    public PeerChatId2 FixOwnerId(UserId2? ownerId)
    {
        if (ownerId.IsGuestOrNull())
            return this;
        if (!HasSingleNonGuestUserId(out var userId))
            return this;
        if (userId == ownerId)
            return null!;

        return New(ownerId, userId);
    }

    public bool HasSingleNonGuestUserId([NotNullWhen(true)] out UserId2? userId)
    {
        var guestUserId = UserId1.IsGuest
            ? UserId1
            : UserId2.IsGuest
                ? UserId2
                : null;
        if (!guestUserId.IsGuestOrNull()) {
            userId = null;
            return false;
        }

        userId = UserIds.OtherThanOrDefault(guestUserId!);
        return !userId.IsGuestOrNull();
    }

    public UserId2 AnotherUserId(UserId2 userId)
        => UserIds.OtherThan(userId);

    public UserId2? AnotherUserIdOrNull(UserId2 userId)
        => UserIds.OtherThanOrDefault(userId);

    public AuthorId2 AnotherAuthorId(UserId2 userId)
        => AuthorId2.New(this, UserId1 == userId ? 2 : 1);

    // Equality

    public bool Equals(PeerChatId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is PeerChatId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(PeerChatId2? left, PeerChatId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(PeerChatId2? left, PeerChatId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(UserId2 userId1, UserId2 userId2)
        => $"{IdPrefix}{userId1}-{userId2}";

    public static new PeerChatId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<PeerChatId2>(s);

    public static new PeerChatId2? ParseOrNull(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static new PeerChatId2? TryParse(string? s)
        => TryParse(s, out var result) ? result : null;

    public static bool TryParse(string? s, [NotNullWhen(true)] out PeerChatId2? result)
    {
        if (!ChatId2.TryParse(s, out var chatEntryId)) {
            result = null;
            return false;
        }

        result = chatEntryId as PeerChatId2;
        return result is not null;
    }
}
