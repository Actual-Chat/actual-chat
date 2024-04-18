using MemoryPack;

namespace ActualChat.Invite;

public interface IInvites : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<Invite>> ListUserInvites(Session session, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<Invite>> ListChatInvites(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<Invite>> ListPlaceInvites(Session session, PlaceId placeId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Invite?> GetOrGenerateChatInvite(Session session, ChatId chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Invite> OnGenerate(Invites_Generate command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Invite> OnUse(Invites_Use command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRevoke(Invites_Revoke command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Invites_Generate(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] Invite Invite
) : ISessionCommand<Invite>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Invites_Use(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] string InviteId
) : ISessionCommand<Invite>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Invites_Revoke(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] string InviteId
) : ISessionCommand<Unit>, IApiCommand;
