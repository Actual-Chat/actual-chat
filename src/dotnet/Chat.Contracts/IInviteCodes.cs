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

    [DataContract]
    public record GenerateCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId
        ) : ISessionCommand<InviteCode>;

    [DataContract]
    public record UseInviteCodeCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string InviteCode
        ) : ISessionCommand<InviteCodeUseResult>;
}
