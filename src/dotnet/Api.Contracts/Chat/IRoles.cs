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
    [ComputeMethod]
    Task<AuthorId[]> ListModeratorIds(Session session, ChatId chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Role> OnChange(Roles_Change command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Roles_Change : ApiCommand<Role>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required RoleId RoleId { get; init; }
    [DataMember(Order = 4), Key(4)] public required long? ExpectedVersion { get; init; }
    [DataMember(Order = 5), Key(5)] public required Change<RoleDiff> Change { get; init; }
}
