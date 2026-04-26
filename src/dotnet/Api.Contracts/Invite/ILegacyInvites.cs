using ActualLab.Rpc;

namespace ActualChat.Invite;

/// <summary>
/// v2.7 legacy IInvites facade. Old clients (version ≤ 2.7.9999) call wire-name
/// <c>"IInvites"</c> and the RPC layer routes them here via <see cref="LegacyNameAttribute"/>.
/// Methods convert the modern <see cref="Invite"/> union into the v2.7
/// wire-frozen <see cref="LegacyInvite"/> shape with its
/// <see cref="LegacyInviteDetails"/> wrapper.
/// </summary>
[LegacyName(nameof(IInvites), "2.7.9999")]
public interface ILegacyInvites : IComputeService
{
    [ComputeMethod, Obsolete("2025.02: User invites feature is removed.")]
    [LegacyName(nameof(IInvites.ListUserInvites), "2.7.9999")]
    Task<LegacyInvite[]> ListLegacyUserInvites(Session session, CancellationToken cancellationToken);

    [ComputeMethod]
    [LegacyName(nameof(IInvites.ListChatInvites), "2.7.9999")]
    Task<LegacyInvite[]> ListLegacyChatInvites(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    [LegacyName(nameof(IInvites.ListPlaceInvites), "2.7.9999")]
    Task<LegacyInvite[]> ListLegacyPlaceInvites(Session session, PlaceId placeId, CancellationToken cancellationToken);

    [ComputeMethod]
    [LegacyName(nameof(IInvites.GetOrGenerateChatInvite), "2.7.9999")]
    Task<LegacyInvite?> GetOrGenerateLegacyChatInvite(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    [LegacyName(nameof(IInvites.GetOrGeneratePlaceInvite), "2.7.9999")]
    Task<LegacyInvite?> GetOrGenerateLegacyPlaceInvite(Session session, PlaceId placeId, CancellationToken cancellationToken);

    [LegacyName(nameof(IInvites.OnGenerate), "2.7.9999")]
    Task<LegacyInvite> OnLegacyGenerate(LegacyInvites_Generate command, CancellationToken cancellationToken);

    [LegacyName(nameof(IInvites.OnUse), "2.7.9999")]
    Task<LegacyInvite> OnLegacyUse(Invites_Use command, CancellationToken cancellationToken);

    [LegacyName(nameof(IInvites.OnRevoke), "2.7.9999")]
    Task OnLegacyRevoke(Invites_Revoke command, CancellationToken cancellationToken);
}

/// <summary>
/// v2.7 wire-frozen counterpart of <see cref="Invites_Generate"/> that carries
/// a <see cref="LegacyInvite"/> instead of the new <see cref="Invite"/> union.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record LegacyInvites_Generate(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] LegacyInvite Invite
) : ISessionCommand<LegacyInvite>, IApiCommand;
