namespace ActualChat.Chat;

/// <summary>
/// Service for managing chat roles and permissions.
/// </summary>
public interface IRoles : IComputeService
{
    [ComputeMethod]
    Task<Role?> Get(Session session, ChatId chatId, RoleId roleId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Role[]> List(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorId[]> ListAuthorIds(Session session, ChatId chatId, RoleId roleId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorId[]> ListOwnerIds(Session session, ChatId chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Role> OnChange(Roles_Change command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Roles_Change(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] RoleId RoleId,
    [property: DataMember, Key(3)] long? ExpectedVersion,
    [property: DataMember, Key(4)] Change<RoleDiff> Change
) : ISessionCommand<Role>, IApiCommand;
