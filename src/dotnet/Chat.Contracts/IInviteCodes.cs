namespace ActualChat.Chat;

public interface IInviteCodes
{
    [ComputeMethod(KeepAliveTime = 1)]
    Task<ImmutableArray<InviteCode>> Get(Session session, string chatId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<InviteCode> Generate(GenerateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<InviteCodeUseResult> UseInviteCode(UseInviteCodeCommand command, CancellationToken cancellationToken);

    public record GenerateCommand(Session Session, string ChatId) : ISessionCommand<InviteCode>;
    public record UseInviteCodeCommand(Session Session, string InviteCode) : ISessionCommand<InviteCodeUseResult>;
}
