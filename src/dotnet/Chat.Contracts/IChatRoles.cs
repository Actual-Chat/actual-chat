namespace ActualChat.Chat;

public interface IChatRoles : IComputeService
{
    [ComputeMethod]
    Task<ChatRole?> Get(Session session, string chatId, string roleId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<ChatRole>> List(Session session, string chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListAuthorIds(Session session, string chatId, string roleId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatRole?> Change(ChangeCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId,
        [property: DataMember] string RoleId,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<ChatRoleDiff> Change
    ) : ISessionCommand<ChatRole?>;
}
