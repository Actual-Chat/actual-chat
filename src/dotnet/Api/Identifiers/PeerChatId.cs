using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Unique identifier for a peer-to-peer chat between two users.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<PeerChatId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<PeerChatId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<PeerChatId>))]
[TypeConverter(typeof(StringLikeTypeConverter<PeerChatId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class PeerChatId : ChatId, IStringIdentifier<PeerChatId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<PeerChatId>();

    public static readonly string IdPrefix = "p-";

    [IgnoreDataMember]
    public UserId UserId1 { get; }
    [IgnoreDataMember]
    public UserId UserId2 { get; }
    [IgnoreDataMember]
    public (UserId UserId1, UserId UserId2) UserIds => (UserId1, UserId2);

    // Factories and constructors

    public static PeerChatId New(UserId userId1, UserId userId2)
    {
        if (userId1 == userId2)
            throw new ArgumentOutOfRangeException(nameof(userId2), "Both user IDs are the same.");

        (userId1, userId2) = (userId1, userId2).Sort(UserId.Comparer);
        return new(Format(userId1, userId2), userId1, userId2);
    }

    internal PeerChatId(string value, UserId userId1, UserId userId2)
        : base(value, ChatKind.Peer)
    {
        UserId1 = userId1;
        UserId2 = userId2;
    }

    // Helpers

    public int IndexOf(UserId userId)
    {
        if (UserId1 == userId)
            return 0;
        if (UserId2 == userId)
            return 1;

        return -1;
    }

    public bool HasUser(UserId userId)
        => IndexOf(userId) != -1;

    public PeerChatId FixOwnerId(UserId? ownerId)
    {
        if (ownerId.IsGuestOrNull())
            return this;
        if (!HasSingleNonGuestUserId(out var userId))
            return this;
        if (userId == ownerId)
            return null!;

        return New(ownerId, userId);
    }

    public bool HasSingleNonGuestUserId([NotNullWhen(true)] out UserId? userId)
    {
        var guestUserId = UserId1.IsGuest ? UserId1
            : UserId2.IsGuest ? UserId2
            : null;
        if (!guestUserId.IsGuestOrNull()) {
            userId = null;
            return false;
        }

        userId = UserIds.OtherThanOrDefault(guestUserId!);
        return !userId.IsGuestOrNull();
    }

    public UserId AnotherUserId(UserId userId)
        => UserIds.OtherThan(userId);

    public UserId? AnotherUserIdOrNull(UserId userId)
        => UserIds.OtherThanOrDefault(userId);

    public AuthorId AnotherAuthorId(UserId userId)
        => AuthorId.New(this, UserId1 == userId ? 2 : 1);

    // Equality

    public bool Equals(PeerChatId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is PeerChatId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(PeerChatId? left, PeerChatId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(PeerChatId? left, PeerChatId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(UserId userId1, UserId userId)
        => $"{IdPrefix}{userId1}-{userId}";

    public static new PeerChatId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<PeerChatId>(s);

    public static new PeerChatId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static new PeerChatId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out PeerChatId? result)
    {
        if (!ChatId.TryParse(s, out var chatEntryId)) {
            result = null;
            return false;
        }

        result = chatEntryId as PeerChatId;
        return result is not null;
    }
}

public static class PeerChatIdExt
{
    public static ContactId ToContactId(this PeerChatId chatId, UserId ownerId)
        => ContactId.NewUser(ownerId, chatId.AnotherUserId(ownerId));
}
