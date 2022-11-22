namespace ActualChat.Chat;

public interface IRoles : IComputeService
{
    [ComputeMethod]
    Task<Role?> Get(Session session, ChatId chatId, RoleId roleId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<Role>> List(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<AuthorId>> ListAuthorIds(Session session, ChatId chatId, RoleId roleId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Role> Change(ChangeCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] Session Session,
        [property: DataMember] ChatId ChatId,
        [property: DataMember] RoleId RoleId,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<RoleDiff> Change
    ) : ISessionCommand<Role>;
}
