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

    [DataContract]
    public sealed record UseInviteCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string InviteCode
        ) : ISessionCommand<InviteUsageResult>;

    [DataContract]
    public sealed record GenerateCommand(
        [property: DataMember] Session Session,
        [property: DataMember] Invite Invite
        ) : ISessionCommand<Invite>;
}
