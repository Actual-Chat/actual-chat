using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract]
[JsonConverter(typeof(StringIdentifierJsonConverter<PeerChatId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<PeerChatId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<PeerChatId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class PeerChatId2(string value, UserId2 userId1, UserId2 userId2, AssumeValid _)
    : StringIdentifier(value), IStringIdentifier<PeerChatId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<PeerChatId2>();
    private static readonly ILruCache<string, PeerChatId2> Cache = CreateCache<PeerChatId2>(256);

    public static readonly string IdPrefix = "p-";

    [IgnoreDataMember]
    public UserId2 UserId1 { get; } = userId1;
    [IgnoreDataMember]
    public UserId2 UserId2 { get; } = userId2;
    [IgnoreDataMember]
    public (UserId2 UserId1, UserId2 UserId2) UserIds => (UserId1, UserId2);

    [IgnoreDataMember] [field: AllowNull, MaybeNull]
    public ChatId2 AsChatId => field ??= ChatId2.Get(this);

    public static PeerChatId2 New(UserId2 userId1, UserId2 userId2)
    {
        if (userId1 == userId2)
            throw new ArgumentOutOfRangeException(nameof(userId2), "Both user IDs are the same.");

        (userId1, userId2) = (userId1, userId2).Sort(UserId2.Comparer);
        return new(Format(userId1, userId2), userId1, userId2, AssumeValid.Option);
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
        => AuthorId2.New(AsChatId, UserId1 == userId ? 2 : 1);

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

    private static string Format(UserId2 userId1, UserId2 userId2)
        => $"{IdPrefix}{userId1}-{userId2}";

    public static PeerChatId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<PeerChatId2>(s);

    public static bool TryParse(string? s, [NotNullWhen(true)] out PeerChatId2? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (!s.OrdinalStartsWith(IdPrefix))
            return false;

        var tail = s.AsSpan(2);
        var userId1Length = tail.IndexOf('-');
        if (userId1Length < 0)
            return false;

        if (!UserId2.TryParse(tail[..userId1Length].ToString(), out var userId1))
            return false;
        if (!UserId2.TryParse(tail[(userId1Length + 1)..].ToString(), out var userId2))
            return false;
        if (string.CompareOrdinal(userId1.Value, userId2.Value) >= 0)
            return false; // Wrong sort order or they are the same

        result = new PeerChatId2(s, userId1, userId2, AssumeValid.Option);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
