namespace ActualChat.Invite;

public interface IInvites
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<IImmutableList<Invite>> GetUserInvites(Session session, CancellationToken cancellationToken);

    [ComputeMethod(KeepAliveTime = 10)]
    Task<IImmutableList<Invite>> GetChatInvites(Session session, string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Invite> Generate(GenerateCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<InviteUsageResult> UseInvite(UseInviteCommand command, CancellationToken cancellationToken);

    public record UseInviteCommand(Session Session, string InviteCode) : ISessionCommand<InviteUsageResult>;

    public record GenerateCommand(Session Session, Invite Invite) : ISessionCommand<Invite>;
}
