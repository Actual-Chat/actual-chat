using MemoryPack;

namespace ActualChat.Invite;

public interface IInvitesBackend : IComputeService
{
    [ComputeMethod]
    Task<Invite?> Get(string id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<Invite>> GetAll(string searchKey, int minRemaining, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> IsValid(string activationKey, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Invite> OnGenerate(InvitesBackend_Generate command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Invite> OnUse(InvitesBackend_Use command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRevoke(InvitesBackend_Revoke command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record InvitesBackend_Revoke(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] string InviteId
) : ISessionCommand<Unit>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record InvitesBackend_Use(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] string InviteId
) : ISessionCommand<Invite>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record InvitesBackend_Generate(
    [property: DataMember, MemoryPackOrder(0)] Invite Invite
) : ICommand<Invite>, IBackendCommand;
