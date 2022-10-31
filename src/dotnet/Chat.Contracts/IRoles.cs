namespace ActualChat.Chat;

public interface IRoles : IComputeService
{
    [ComputeMethod]
    Task<Role?> Get(Session session, string chatId, string roleId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<Role>> List(Session session, string chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListAuthorIds(Session session, string chatId, string roleId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Role> Change(ChangeCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId,
        [property: DataMember] string RoleId,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<RoleDiff> Change
    ) : ISessionCommand<Role>;
}
