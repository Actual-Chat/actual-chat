namespace ActualChat.Chat;

public interface IInviteCodesBackend
{
    [ComputeMethod(KeepAliveTime = 1)]
    Task<InviteCode?> GetByValue(string inviteCode, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<ImmutableArray<InviteCode>> Get(string chatId, string userId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 1)]
    Task<bool> CheckIfInviteCodeUsed(Session session, string chatId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<InviteCode> Generate(GenerateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task UseInviteCode(UseInviteCodeCommand command, CancellationToken cancellationToken);

    [DataContract]
    public record GenerateCommand(
        [property: DataMember] InviteCode InviteCode
        ) : ICommand<InviteCode>, IBackendCommand;

    [DataContract]
    public record UseInviteCodeCommand(
        [property: DataMember] Session Session,
        [property: DataMember] InviteCode InviteCode
        ) : ISessionCommand<Unit>, IBackendCommand;
}
