namespace ActualChat.Invite;

public interface IInvites : IComputeService
{
    [ComputeMethod]
    Task<IImmutableList<Invite>> GetUserInvites(Session session, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<IImmutableList<Invite>> GetChatInvites(Session session, string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Invite> Generate(GenerateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Invite> Use(UseCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record GenerateCommand(
        [property: DataMember] Session Session,
        [property: DataMember] Invite Invite
    ) : ISessionCommand<Invite>;

    [DataContract]
    public sealed record UseCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string InviteId
        ) : ISessionCommand<Invite>;
}
