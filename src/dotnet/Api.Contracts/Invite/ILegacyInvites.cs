using ActualLab.Rpc;

namespace ActualChat.Invite;

/// <summary>
/// v2.7 legacy IInvites facade. Old clients (version ≤ 2.7.9999) call wire-name
/// <c>"IInvites"</c> and the interface-level <see cref="LegacyNameAttribute"/> below
/// routes them here without per-method aliases — method names match
/// <see cref="IInvites"/>, only the return shapes (and the OnGenerate / OnUse /
/// OnRevoke command parameters) are pinned to the v2.7 wire format.
/// </summary>
[LegacyName(nameof(IInvites), "2.7.9999")]
public interface ILegacyInvites : IComputeService
{
    [ComputeMethod, Obsolete("2026.02: User invites feature is removed.")]
    Task<LegacyInvite[]> ListUserInvites(Session session, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<LegacyInvite[]> ListChatInvites(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<LegacyInvite[]> ListPlaceInvites(Session session, PlaceId placeId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<LegacyInvite?> GetOrGenerateChatInvite(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<LegacyInvite?> GetOrGeneratePlaceInvite(Session session, PlaceId placeId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<LegacyInvite> OnGenerate(LegacyInvites_Generate command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<LegacyInvite> OnUse(LegacyInvites_Use command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRevoke(LegacyInvites_Revoke command, CancellationToken cancellationToken);
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

/// <summary>
/// v2.7 wire-frozen counterpart of <see cref="Invites_Use"/>. Same field layout —
/// only the .NET type differs so Commander can register a distinct handler.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record LegacyInvites_Use(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] string InviteId
) : ISessionCommand<LegacyInvite>, IApiCommand
{
    public Invites_Use ToModern() => new(Session, InviteId);
}

/// <summary>
/// v2.7 wire-frozen counterpart of <see cref="Invites_Revoke"/>. Same field layout —
/// only the .NET type differs so Commander can register a distinct handler.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record LegacyInvites_Revoke(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] string InviteId
) : ISessionCommand<Unit>, IApiCommand
{
    public Invites_Revoke ToModern() => new(Session, InviteId);
}
