using ActualLab.Versioning;

namespace ActualChat.Invite;

#pragma warning disable MA0049 // Allows ActualChat.Invite.LegacyInvite

/// <summary>
/// Wire-frozen v2.7 <see cref="Invite"/> shape kept for clients that still talk MemoryPack.
/// Carries a <see cref="LegacyInviteDetails"/> payload — the modern API replaces this with
/// concrete <c>ChatInvite</c> / <c>PlaceInvite</c> / <c>UserInvite</c> subclasses.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record LegacyInvite(
    [property: DataMember, MemoryPackOrder(0)] Symbol Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<Symbol>, IHasVersion<long>
{
    [DataMember, MemoryPackOrder(2)] public string CreatedBy { get; init; } = "";
    [DataMember, MemoryPackOrder(3)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(4)] public Moment ExpiresOn { get; init; }
    [DataMember, MemoryPackOrder(5)] public int Remaining { get; init; }
    [DataMember, MemoryPackOrder(6)] public LegacyInviteDetails Details { get; init; } = null!;

    public static LegacyInvite From(Invite invite)
        => new(invite.Id, invite.Version) {
            CreatedBy = invite.CreatedBy,
            CreatedAt = invite.CreatedAt,
            ExpiresOn = invite.ExpiresOn,
            Remaining = invite.Remaining,
            Details = LegacyInviteDetails.From(invite),
        };

    public Invite ToModern() => Details.Option switch {
        LegacyChatInviteOption chat => new ChatInvite(Id, Version) {
            ChatId = chat.ChatId,
            CreatedBy = CreatedBy,
            CreatedAt = CreatedAt,
            ExpiresOn = ExpiresOn,
            Remaining = Remaining,
        },
        LegacyPlaceInviteOption place => new PlaceInvite(Id, Version) {
            PlaceId = place.PlaceId,
            CreatedBy = CreatedBy,
            CreatedAt = CreatedAt,
            ExpiresOn = ExpiresOn,
            Remaining = Remaining,
        },
        LegacyUserInviteOption => new UserInvite(Id, Version) {
            CreatedBy = CreatedBy,
            CreatedAt = CreatedAt,
            ExpiresOn = ExpiresOn,
            Remaining = Remaining,
        },
        _ => throw StandardError.Format<LegacyInvite>($"Unknown legacy invite details: {Details.Option?.GetType().Name}"),
    };
}
