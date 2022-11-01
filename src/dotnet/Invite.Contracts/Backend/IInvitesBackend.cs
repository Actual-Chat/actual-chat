namespace ActualChat.Invite.Backend;

public interface IInvitesBackend : IComputeService
{
    [ComputeMethod]
    Task<Invite?> Get(string id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Invite>> GetAll(string searchKey, int minRemaining, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Invite> Generate(GenerateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Invite> Use(UseCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record GenerateCommand(
        [property: DataMember] Invite Invite
        ) : ICommand<Invite>, IBackendCommand;

    [DataContract]
    public sealed record UseCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string InviteId
        ) : ISessionCommand<Invite>;
}
