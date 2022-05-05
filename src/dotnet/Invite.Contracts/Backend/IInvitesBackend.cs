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

    public record GenerateCommand(Invite Invite) : ICommand<Invite>, IBackendCommand;

    public record UseInviteCommand(Invite Invite) : ICommand<Unit>, IBackendCommand;
}
