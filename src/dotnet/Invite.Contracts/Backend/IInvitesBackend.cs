namespace ActualChat.Invite.Backend;

public interface IInvitesBackend
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<IImmutableList<Invite>> GetAll(CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 10)]
    Task<Invite?> GetByCode(string inviteCode, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Invite> Generate(GenerateCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task UseInvite(UseInviteCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record GenerateCommand(
        [property: DataMember] Invite Invite
        ) : ICommand<Invite>, IBackendCommand;

    [DataContract]
    public sealed record UseInviteCommand(
        [property: DataMember] Invite Invite
        ) : ICommand<Unit>, IBackendCommand;
}
